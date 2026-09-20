using System.Net;
using System.Net.Http.Json;
using CubeArena.Api.Features.Auth;
using CubeArena.Tests;

namespace CubeArena.Tests.Auth;

public class AuthEndpointsTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    private static string UniqueEmail() => $"{Guid.NewGuid():N}@example.com";

    [Fact]
    public async Task Register_ReturnsCreated_ForNewUser()
    {
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/auth/register", new RegisterRequest(UniqueEmail(), "password123"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-an-email")]
    public async Task Register_ReturnsBadRequest_ForInvalidEmail(string email)
    {
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/auth/register", new RegisterRequest(email, "password123"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Register_ReturnsConflict_ForDuplicateEmail()
    {
        var client = factory.CreateClient();
        var email = UniqueEmail();
        await client.PostAsJsonAsync("/auth/register", new RegisterRequest(email, "password123"));

        var response = await client.PostAsJsonAsync("/auth/register", new RegisterRequest(email, "a-different-password"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Login_ReturnsTokens_ForValidCredentials()
    {
        var client = factory.CreateClient();
        var email = UniqueEmail();
        await client.PostAsJsonAsync("/auth/register", new RegisterRequest(email, "password123"));

        var response = await client.PostAsJsonAsync("/auth/login", new LoginRequest(email, "password123"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<TokenResponse>();
        Assert.NotNull(body);
        Assert.False(string.IsNullOrWhiteSpace(body!.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(body.RefreshToken));
    }

    [Fact]
    public async Task Login_ReturnsUnauthorized_ForWrongPassword()
    {
        var client = factory.CreateClient();
        var email = UniqueEmail();
        await client.PostAsJsonAsync("/auth/register", new RegisterRequest(email, "password123"));

        var response = await client.PostAsJsonAsync("/auth/login", new LoginRequest(email, "wrong-password"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_RotatesToken_ThenRejectsReplayOfTheOldOne()
    {
        var client = factory.CreateClient();
        var email = UniqueEmail();
        await client.PostAsJsonAsync("/auth/register", new RegisterRequest(email, "password123"));
        var login = await (await client.PostAsJsonAsync("/auth/login", new LoginRequest(email, "password123")))
            .Content.ReadFromJsonAsync<TokenResponse>();

        var refreshResponse = await client.PostAsJsonAsync("/auth/refresh", new RefreshRequest(login!.RefreshToken));
        Assert.Equal(HttpStatusCode.OK, refreshResponse.StatusCode);
        var rotated = await refreshResponse.Content.ReadFromJsonAsync<TokenResponse>();
        Assert.NotEqual(login.RefreshToken, rotated!.RefreshToken);

        // Replaying the original (already-rotated) refresh token must be rejected.
        var replayResponse = await client.PostAsJsonAsync("/auth/refresh", new RefreshRequest(login.RefreshToken));
        Assert.Equal(HttpStatusCode.Unauthorized, replayResponse.StatusCode);

        // Reuse detection revokes the whole family, so even the freshly-rotated token is now dead.
        var afterReuseResponse = await client.PostAsJsonAsync("/auth/refresh", new RefreshRequest(rotated.RefreshToken));
        Assert.Equal(HttpStatusCode.Unauthorized, afterReuseResponse.StatusCode);
    }

    [Fact]
    public async Task Logout_RevokesRefreshToken()
    {
        var client = factory.CreateClient();
        var email = UniqueEmail();
        await client.PostAsJsonAsync("/auth/register", new RegisterRequest(email, "password123"));
        var login = await (await client.PostAsJsonAsync("/auth/login", new LoginRequest(email, "password123")))
            .Content.ReadFromJsonAsync<TokenResponse>();

        var logoutResponse = await client.PostAsJsonAsync("/auth/logout", new LogoutRequest(login!.RefreshToken));
        Assert.Equal(HttpStatusCode.NoContent, logoutResponse.StatusCode);

        var refreshAfterLogout = await client.PostAsJsonAsync("/auth/refresh", new RefreshRequest(login.RefreshToken));
        Assert.Equal(HttpStatusCode.Unauthorized, refreshAfterLogout.StatusCode);
    }
}
