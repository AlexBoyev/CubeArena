using CubeArena.Api.Data;
using CubeArena.Api.Features.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace CubeArena.Tests.Auth;

public class AuthServiceTests
{
    private static readonly AuthOptions TestOptions = new()
    {
        SigningKey = "unit-test-signing-key-0123456789-0123456789",
        LockoutThreshold = 3,
        LockoutBaseDelay = TimeSpan.FromSeconds(30),
        LockoutMaxDelay = TimeSpan.FromHours(1)
    };

    private static (CubeArenaDbContext Db, AuthService Service, FakeTimeProvider Time) CreateService()
    {
        var db = new CubeArenaDbContext(new DbContextOptionsBuilder<CubeArenaDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var options = Options.Create(TestOptions);
        var hasher = new PasswordHasher();
        var tokens = new TokenService(db, options, time);
        var service = new AuthService(db, hasher, tokens, options, time);
        return (db, service, time);
    }

    [Fact]
    public async Task RegisterAsync_Succeeds_ForNewEmail()
    {
        var (_, service, _) = CreateService();

        var outcome = await service.RegisterAsync("Player@Example.com", "password123", CancellationToken.None);

        Assert.Equal(RegisterOutcome.Success, outcome);
    }

    [Fact]
    public async Task RegisterAsync_RejectsDuplicateEmail_CaseInsensitively()
    {
        var (_, service, _) = CreateService();
        await service.RegisterAsync("player@example.com", "password123", CancellationToken.None);

        var outcome = await service.RegisterAsync("Player@Example.com", "different-password", CancellationToken.None);

        Assert.Equal(RegisterOutcome.EmailTaken, outcome);
    }

    [Fact]
    public async Task LoginAsync_Succeeds_WithCorrectCredentials()
    {
        var (_, service, _) = CreateService();
        await service.RegisterAsync("player@example.com", "password123", CancellationToken.None);

        var result = await service.LoginAsync("player@example.com", "password123", CancellationToken.None);

        Assert.Equal(LoginOutcome.Success, result.Outcome);
        Assert.NotNull(result.AccessToken);
        Assert.NotNull(result.RefreshToken);
    }

    [Fact]
    public async Task LoginAsync_Fails_WithWrongPassword()
    {
        var (_, service, _) = CreateService();
        await service.RegisterAsync("player@example.com", "password123", CancellationToken.None);

        var result = await service.LoginAsync("player@example.com", "wrong-password", CancellationToken.None);

        Assert.Equal(LoginOutcome.InvalidCredentials, result.Outcome);
    }

    [Fact]
    public async Task LoginAsync_Fails_ForUnknownEmail()
    {
        var (_, service, _) = CreateService();

        var result = await service.LoginAsync("nobody@example.com", "password123", CancellationToken.None);

        Assert.Equal(LoginOutcome.InvalidCredentials, result.Outcome);
    }

    [Fact]
    public async Task LoginAsync_LocksAccount_AfterThresholdFailures()
    {
        var (_, service, time) = CreateService();
        await service.RegisterAsync("player@example.com", "password123", CancellationToken.None);

        // LockoutThreshold is 3.
        await service.LoginAsync("player@example.com", "wrong", CancellationToken.None);
        await service.LoginAsync("player@example.com", "wrong", CancellationToken.None);
        var thirdAttempt = await service.LoginAsync("player@example.com", "wrong", CancellationToken.None);

        Assert.Equal(LoginOutcome.LockedOut, thirdAttempt.Outcome);
        Assert.Equal(TimeSpan.FromSeconds(30), thirdAttempt.RetryAfter);

        // Even the correct password is rejected while locked out.
        var duringLockout = await service.LoginAsync("player@example.com", "password123", CancellationToken.None);
        Assert.Equal(LoginOutcome.LockedOut, duringLockout.Outcome);

        time.Advance(TimeSpan.FromSeconds(31));

        var afterLockout = await service.LoginAsync("player@example.com", "password123", CancellationToken.None);
        Assert.Equal(LoginOutcome.Success, afterLockout.Outcome);
    }
}
