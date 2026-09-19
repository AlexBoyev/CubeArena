using System.IdentityModel.Tokens.Jwt;
using CubeArena.Api.Data;
using CubeArena.Api.Features.Auth;
using CubeArena.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace CubeArena.Tests.Auth;

public class TokenServiceTests
{
    private static readonly AuthOptions TestOptions = new()
    {
        SigningKey = "unit-test-signing-key-0123456789-0123456789",
        Issuer = "cubearena-api-test",
        Audience = "api",
        AccessTokenLifetime = TimeSpan.FromMinutes(15),
        RefreshTokenLifetime = TimeSpan.FromDays(30)
    };

    private static CubeArenaDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<CubeArenaDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static User CreateUser() => new()
    {
        Id = Guid.NewGuid(),
        Email = "player@example.com",
        DisplayName = "player",
        PasswordHash = "irrelevant-for-token-tests",
        CreatedAtUtc = DateTimeOffset.UtcNow
    };

    [Fact]
    public void CreateAccessToken_ContainsSpecifiedClaims()
    {
        var time = new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        var service = new TokenService(CreateDb(), Options.Create(TestOptions), time);
        var user = CreateUser();

        var token = service.CreateAccessToken(user);
        var principal = service.ValidateAccessToken(token);

        Assert.NotNull(principal);
        Assert.Equal(user.Id.ToString(), principal!.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
        Assert.Equal(user.DisplayName, principal.FindFirst("name")!.Value);
    }

    [Fact]
    public void ValidateAccessToken_ReturnsNull_AfterExpiry()
    {
        var time = new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        var service = new TokenService(CreateDb(), Options.Create(TestOptions), time);
        var token = service.CreateAccessToken(CreateUser());

        time.Advance(TimeSpan.FromMinutes(16));

        Assert.Null(service.ValidateAccessToken(token));
    }

    [Fact]
    public void ValidateAccessToken_ReturnsNull_ForTamperedToken()
    {
        var time = new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        var service = new TokenService(CreateDb(), Options.Create(TestOptions), time);
        var token = service.CreateAccessToken(CreateUser());

        var tampered = token[..^2] + (token[^2] == 'A' ? "B" : "A") + token[^1];

        Assert.Null(service.ValidateAccessToken(tampered));
    }

    [Fact]
    public async Task RotateRefreshTokenAsync_ReturnsNotFound_ForUnknownToken()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var service = new TokenService(CreateDb(), Options.Create(TestOptions), time);

        var result = await service.RotateRefreshTokenAsync("not-a-real-token", CancellationToken.None);

        Assert.Equal(RefreshOutcome.NotFound, result.Outcome);
    }

    [Fact]
    public async Task RotateRefreshTokenAsync_Succeeds_AndInvalidatesTheOldToken()
    {
        var db = CreateDb();
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var service = new TokenService(db, Options.Create(TestOptions), time);
        var user = CreateUser();
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var (firstToken, _) = await service.IssueRefreshTokenAsync(user.Id, familyId: null, CancellationToken.None);

        var rotated = await service.RotateRefreshTokenAsync(firstToken, CancellationToken.None);
        Assert.Equal(RefreshOutcome.Success, rotated.Outcome);
        Assert.NotEqual(firstToken, rotated.RefreshToken);

        var secondAttempt = await service.RotateRefreshTokenAsync(firstToken, CancellationToken.None);
        Assert.Equal(RefreshOutcome.Reused, secondAttempt.Outcome);
    }

    [Fact]
    public async Task RotateRefreshTokenAsync_DetectsReuse_AndRevokesWholeFamily()
    {
        var db = CreateDb();
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var service = new TokenService(db, Options.Create(TestOptions), time);
        var user = CreateUser();
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var (firstToken, _) = await service.IssueRefreshTokenAsync(user.Id, familyId: null, CancellationToken.None);
        var firstRotation = await service.RotateRefreshTokenAsync(firstToken, CancellationToken.None);
        Assert.Equal(RefreshOutcome.Success, firstRotation.Outcome);
        var secondToken = firstRotation.RefreshToken!;

        // Replay the already-consumed first token.
        var replay = await service.RotateRefreshTokenAsync(firstToken, CancellationToken.None);
        Assert.Equal(RefreshOutcome.Reused, replay.Outcome);

        // The entire family, including the legitimately-rotated second token, must now be dead.
        var afterReuse = await service.RotateRefreshTokenAsync(secondToken, CancellationToken.None);
        Assert.Equal(RefreshOutcome.Revoked, afterReuse.Outcome);
    }

    [Fact]
    public async Task RotateRefreshTokenAsync_ReturnsExpired_PastExpiry()
    {
        var db = CreateDb();
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var shortLivedOptions = new AuthOptions
        {
            SigningKey = TestOptions.SigningKey,
            RefreshTokenLifetime = TimeSpan.FromMinutes(1)
        };
        var service = new TokenService(db, Options.Create(shortLivedOptions), time);
        var user = CreateUser();
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var (token, _) = await service.IssueRefreshTokenAsync(user.Id, familyId: null, CancellationToken.None);

        time.Advance(TimeSpan.FromMinutes(2));

        var result = await service.RotateRefreshTokenAsync(token, CancellationToken.None);
        Assert.Equal(RefreshOutcome.Expired, result.Outcome);
    }
}
