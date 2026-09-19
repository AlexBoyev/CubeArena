using CubeArena.Api.Data;
using CubeArena.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CubeArena.Api.Features.Auth;

public enum RegisterOutcome
{
    Success,
    EmailTaken
}

public enum LoginOutcome
{
    Success,
    InvalidCredentials,
    LockedOut
}

public record LoginResult(LoginOutcome Outcome, string? AccessToken = null, string? RefreshToken = null, TimeSpan? RetryAfter = null);

public class AuthService(
    CubeArenaDbContext db,
    PasswordHasher hasher,
    TokenService tokens,
    IOptions<AuthOptions> options,
    TimeProvider timeProvider,
    ILogger<AuthService>? logger = null)
{
    private readonly AuthOptions _options = options.Value;

    public async Task<RegisterOutcome> RegisterAsync(string email, string password, CancellationToken ct, string correlationId = "")
    {
        var normalizedEmail = email.Trim().ToLowerInvariant();
        var alreadyExists = await db.Users.AnyAsync(u => u.Email == normalizedEmail, ct);
        if (alreadyExists)
        {
            logger?.LogInformation(
                "Registration rejected: email already registered. CorrelationId={CorrelationId}", correlationId);
            return RegisterOutcome.EmailTaken;
        }

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = normalizedEmail,
            DisplayName = normalizedEmail.Split('@')[0],
            PasswordHash = hasher.Hash(password),
            CreatedAtUtc = timeProvider.GetUtcNow()
        };

        db.Users.Add(user);
        await db.SaveChangesAsync(ct);

        return RegisterOutcome.Success;
    }

    public async Task<LoginResult> LoginAsync(string email, string password, CancellationToken ct, string correlationId = "")
    {
        var normalizedEmail = email.Trim().ToLowerInvariant();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == normalizedEmail, ct);
        var now = timeProvider.GetUtcNow();

        if (user is null)
        {
            logger?.LogInformation(
                "Login rejected: unknown account. CorrelationId={CorrelationId}", correlationId);
            return new LoginResult(LoginOutcome.InvalidCredentials);
        }

        if (user.LockedUntilUtc is { } lockedUntil && lockedUntil > now)
        {
            logger?.LogWarning(
                "Login rejected: account locked out. UserId={UserId} CorrelationId={CorrelationId}",
                user.Id, correlationId);
            return new LoginResult(LoginOutcome.LockedOut, RetryAfter: lockedUntil - now);
        }

        if (!hasher.Verify(password, user.PasswordHash))
        {
            user.FailedLoginCount++;
            TimeSpan? retryAfter = null;

            if (user.FailedLoginCount >= _options.LockoutThreshold)
            {
                var delay = ComputeLockoutDelay(user.FailedLoginCount);
                user.LockedUntilUtc = now + delay;
                retryAfter = delay;
            }

            await db.SaveChangesAsync(ct);

            logger?.LogWarning(
                "Login rejected: wrong password. UserId={UserId} FailedLoginCount={FailedLoginCount} LockedOut={LockedOut} CorrelationId={CorrelationId}",
                user.Id, user.FailedLoginCount, retryAfter is not null, correlationId);

            return retryAfter is null
                ? new LoginResult(LoginOutcome.InvalidCredentials)
                : new LoginResult(LoginOutcome.LockedOut, RetryAfter: retryAfter);
        }

        user.FailedLoginCount = 0;
        user.LockedUntilUtc = null;
        await db.SaveChangesAsync(ct);

        var accessToken = tokens.CreateAccessToken(user);
        var (refreshToken, _) = await tokens.IssueRefreshTokenAsync(user.Id, familyId: null, ct);

        return new LoginResult(LoginOutcome.Success, accessToken, refreshToken);
    }

    private TimeSpan ComputeLockoutDelay(int failedCount)
    {
        var exponent = failedCount - _options.LockoutThreshold;
        var delay = _options.LockoutBaseDelay * Math.Pow(2, exponent);
        return delay > _options.LockoutMaxDelay ? _options.LockoutMaxDelay : delay;
    }
}
