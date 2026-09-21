using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CubeArena.Api.Features.Auth;
using CubeArena.Api.Features.Fleet;
using CubeArena.Api.Features.Sessions;
using Microsoft.IdentityModel.Tokens;

namespace CubeArena.Tests.Sessions;

public class SessionEndpointsTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    private async Task<HttpClient> CreateClientForNewPlayerAsync()
    {
        var client = factory.CreateClient();
        var email = $"{Guid.NewGuid():N}@example.com";
        await client.PostAsJsonAsync("/auth/register", new RegisterRequest(email, "password123"));
        var login = await (await client.PostAsJsonAsync("/auth/login", new LoginRequest(email, "password123")))
            .Content.ReadFromJsonAsync<TokenResponse>();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.AccessToken);
        return client;
    }

    private async Task RegisterAGameServerAsync(string host = "127.0.0.1", int port = 9999, int capacity = 6)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Fleet-Api-Key", TestFactoryExtensions.TestFleetApiKey);
        var response = await client.PostAsJsonAsync("/fleet/register", new RegisterServerRequest(host, port, capacity));
        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Quickplay_RequiresAuthentication()
    {
        var client = factory.CreateClient();

        var response = await client.PostAsync("/sessions/quickplay", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Quickplay_ReturnsATicket_WhenCapacityExists()
    {
        await RegisterAGameServerAsync();
        var client = await CreateClientForNewPlayerAsync();

        var response = await client.PostAsync("/sessions/quickplay", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<QuickplayResponse>();
        Assert.False(string.IsNullOrEmpty(body!.Ticket));
        Assert.InRange(body.SlotIndex, 0, 5);
    }

    [Fact]
    public async Task Quickplay_IssuedTicket_ValidatesAgainstThePublishedJwks()
    {
        await RegisterAGameServerAsync();
        var client = await CreateClientForNewPlayerAsync();
        var quickplay = await (await client.PostAsync("/sessions/quickplay", null))
            .Content.ReadFromJsonAsync<QuickplayResponse>();

        var jwks = await factory.CreateClient().GetFromJsonAsync<JsonWebKeySet>("/.well-known/jwks.json");
        var jwk = Assert.Single(jwks!.Keys);
        var publicKey = new ECDsaSecurityKey(
            System.Security.Cryptography.ECDsa.Create(new System.Security.Cryptography.ECParameters
            {
                Curve = System.Security.Cryptography.ECCurve.NamedCurves.nistP256,
                Q = new System.Security.Cryptography.ECPoint
                {
                    X = Base64UrlEncoder.DecodeBytes(jwk.X),
                    Y = Base64UrlEncoder.DecodeBytes(jwk.Y)
                }
            }))
        { KeyId = jwk.Kid };

        var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler { MapInboundClaims = false };
        var principal = handler.ValidateToken(quickplay!.Ticket, new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = "cubearena-api",
            ValidateAudience = true,
            ValidAudience = "gameserver",
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = publicKey
        }, out _);

        Assert.Equal(quickplay.SessionId.ToString(), principal.FindFirst("sid")!.Value);
        Assert.Equal(quickplay.SlotIndex.ToString(), principal.FindFirst("slot")!.Value);
    }
}
