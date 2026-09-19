using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using CubeArena.Api.Data;
using CubeArena.Domain.Auth;
using CubeArena.Domain.Users;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace CubeArena.Api.Features.Auth;

public enum RefreshOutcome
{
    Success,
    NotFound,
    Expired,
    Reused,
    Revoked
}

public record RefreshResult(RefreshOutcome Outcome, User? User = null, string? AccessToken = null, string? RefreshToken = null);

public class TokenService(
    CubeArenaDbContext db,
    IOptions<AuthOptions> options,
    TimeProvider timeProvider,
    ILogger<TokenService>? logger = null)
{
    private readonly AuthOptions _options = options.Value;

    public TokenValidationParameters ValidationParameters => BuildValidationParameters(_options, timeProvider);

    // Microsoft.IdentityModel.Tokens 7.x has no TimeProvider hook, so lifetime validation
    // is wired through LifetimeValidator to keep it testable with a fake clock.
    public static TokenValidationParameters BuildValidationParameters(AuthOptions options, TimeProvider? timeProvider = null)
    {
        var clock = timeProvider ?? TimeProvider.System;
        var parameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = options.Issuer,
            ValidateAudience = true,
            ValidAudience = options.Audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey)),
            ClockSkew = TimeSpan.FromSeconds(5)
        };

        parameters.LifetimeValidator = (notBefore, expires, _, @params) =>
        {
            var now = clock.GetUtcNow().UtcDateTime;
            if (notBefore is not null && now < notBefore.Value - @params.ClockSkew) return false;
            if (expires is not null && now > expires.Value + @params.ClockSkew) return false;
            return true;
        };

        return parameters;
    }

    public string CreateAccessToken(User user)
    {
        var now = timeProvider.GetUtcNow();
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim("name", user.DisplayName)
        };

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: (now + _options.AccessTokenLifetime).UtcDateTime,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public ClaimsPrincipal? ValidateAccessToken(string token)
    {
        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        try
        {
            return handler.ValidateToken(token, ValidationParameters, out _);
        }
        catch (SecurityTokenException)
        {
            return null;
        }
    }

    public async Task<(string RawToken, RefreshToken Entity)> IssueRefreshTokenAsync(
        Guid userId, Guid? familyId, CancellationToken ct)
    {
        var rawToken = GenerateOpaqueToken();
        var now = timeProvider.GetUtcNow();

        var entity = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            FamilyId = familyId ?? Guid.NewGuid(),
            TokenHash = Hash(rawToken),
            CreatedAtUtc = now,
            ExpiresAtUtc = now + _options.RefreshTokenLifetime
        };

        db.RefreshTokens.Add(entity);
        await db.SaveChangesAsync(ct);

        return (rawToken, entity);
    }

    public async Task<RefreshResult> RotateRefreshTokenAsync(string rawToken, CancellationToken ct, string correlationId = "")
    {
        var tokenHash = Hash(rawToken);
        var existing = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == tokenHash, ct);

        if (existing is null)
        {
            logger?.LogWarning(
                "Refresh rejected: token not found. Reason={Reason} CorrelationId={CorrelationId}",
                RefreshOutcome.NotFound, correlationId);
            return new RefreshResult(RefreshOutcome.NotFound);
        }

        var now = timeProvider.GetUtcNow();

        if (existing.RevokedAtUtc is not null)
        {
            logger?.LogWarning(
                "Refresh rejected: token family already revoked. FamilyId={FamilyId} CorrelationId={CorrelationId}",
                existing.FamilyId, correlationId);
            return new RefreshResult(RefreshOutcome.Revoked);
        }

        if (existing.ConsumedAtUtc is not null)
        {
            // Reuse of an already-rotated token: revoke the whole family.
            logger?.LogWarning(
                "Refresh token reuse detected, revoking family. FamilyId={FamilyId} UserId={UserId} CorrelationId={CorrelationId}",
                existing.FamilyId, existing.UserId, correlationId);
            await RevokeFamilyAsync(existing.FamilyId, ct);
            return new RefreshResult(RefreshOutcome.Reused);
        }

        if (existing.ExpiresAtUtc <= now)
        {
            logger?.LogInformation(
                "Refresh rejected: token expired. FamilyId={FamilyId} CorrelationId={CorrelationId}",
                existing.FamilyId, correlationId);
            return new RefreshResult(RefreshOutcome.Expired);
        }

        var user = await db.Users.FindAsync([existing.UserId], ct);
        if (user is null)
        {
            return new RefreshResult(RefreshOutcome.NotFound);
        }

        existing.ConsumedAtUtc = now;
        var (newRawToken, _) = await IssueRefreshTokenAsync(existing.UserId, existing.FamilyId, ct);
        await db.SaveChangesAsync(ct);

        var accessToken = CreateAccessToken(user);
        return new RefreshResult(RefreshOutcome.Success, user, accessToken, newRawToken);
    }

    public async Task RevokeByRawTokenAsync(string rawToken, CancellationToken ct)
    {
        var tokenHash = Hash(rawToken);
        var existing = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == tokenHash, ct);
        if (existing is not null)
        {
            await RevokeFamilyAsync(existing.FamilyId, ct);
        }
    }

    public async Task RevokeFamilyAsync(Guid familyId, CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();
        var tokens = await db.RefreshTokens
            .Where(t => t.FamilyId == familyId && t.RevokedAtUtc == null)
            .ToListAsync(ct);

        foreach (var token in tokens)
        {
            token.RevokedAtUtc = now;
        }

        await db.SaveChangesAsync(ct);
    }

    public static string Hash(string rawToken)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string GenerateOpaqueToken() =>
        WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
}
