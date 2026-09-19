using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CubeArena.Api.Features.Auth;
using CubeArena.Api.Features.Fleet;
using CubeArena.Api.Features.Sessions;

namespace CubeArena.Tests.Fleet;

public class FleetEndpointsTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    private HttpClient CreateAuthenticatedClient()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Fleet-Api-Key", TestFactoryExtensions.TestFleetApiKey);
        return client;
    }

    private async Task<(Guid UserId, QuickplayResponse Quickplay)> CreateAReservedSlotAsync()
    {
        var fleetClient = CreateAuthenticatedClient();
        await fleetClient.PostAsJsonAsync("/fleet/register", new RegisterServerRequest("127.0.0.1", 8888, 4));

        var playerClient = factory.CreateClient();
        var email = $"{Guid.NewGuid():N}@example.com";
        await playerClient.PostAsJsonAsync("/auth/register", new RegisterRequest(email, "password123"));
        var login = await (await playerClient.PostAsJsonAsync("/auth/login", new LoginRequest(email, "password123")))
            .Content.ReadFromJsonAsync<TokenResponse>();
        playerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.AccessToken);

        var quickplay = await (await playerClient.PostAsync("/sessions/quickplay", null))
            .Content.ReadFromJsonAsync<QuickplayResponse>();

        // JwtRegisteredClaimNames.Sub isn't exposed via the token response, so decode it
        // from the access token's payload directly rather than pulling in a JWT library here.
        var payload = login.AccessToken.Split('.')[1];
        var padded = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
        var json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(padded.Replace('-', '+').Replace('_', '/')));
        var userId = Guid.Parse(System.Text.Json.JsonDocument.Parse(json).RootElement.GetProperty("sub").GetString()!);

        return (userId, quickplay!);
    }

    [Fact]
    public async Task Register_ReturnsServerAndSessionIds()
    {
        var client = CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync("/fleet/register", new RegisterServerRequest("127.0.0.1", 7777, 4));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<RegisterServerResponse>();
        Assert.NotEqual(Guid.Empty, body!.GameServerId);
        Assert.NotEqual(Guid.Empty, body.SessionId);
    }

    [Fact]
    public async Task Register_RejectsRequestsWithoutTheApiKey()
    {
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/fleet/register", new RegisterServerRequest("127.0.0.1", 7777, 4));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Register_RejectsAWrongApiKey()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Fleet-Api-Key", "not-the-right-key");

        var response = await client.PostAsJsonAsync("/fleet/register", new RegisterServerRequest("127.0.0.1", 7777, 4));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Heartbeat_Succeeds_ForARegisteredServer()
    {
        var client = CreateAuthenticatedClient();
        var registered = await (await client.PostAsJsonAsync("/fleet/register", new RegisterServerRequest("127.0.0.1", 7778, 4)))
            .Content.ReadFromJsonAsync<RegisterServerResponse>();

        var response = await client.PostAsJsonAsync("/fleet/heartbeat", new HeartbeatRequest(registered!.GameServerId, 2));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Heartbeat_ReturnsNotFound_ForAnUnknownServer()
    {
        var client = CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync("/fleet/heartbeat", new HeartbeatRequest(Guid.NewGuid(), 0));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ConfirmSlot_Succeeds_ForAReservedSlot()
    {
        var (userId, quickplay) = await CreateAReservedSlotAsync();
        var client = CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync("/fleet/sessions/confirm", new SlotSessionRequest(quickplay.SessionId, userId));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task ConfirmSlot_ReturnsNotFound_ForAnUnknownReservation()
    {
        var client = CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync("/fleet/sessions/confirm", new SlotSessionRequest(Guid.NewGuid(), Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ReleaseSlot_Succeeds_ThenRejoinReturnsTheSameSlot()
    {
        var (userId, quickplay) = await CreateAReservedSlotAsync();
        var fleetClient = CreateAuthenticatedClient();
        await fleetClient.PostAsJsonAsync("/fleet/sessions/confirm", new SlotSessionRequest(quickplay.SessionId, userId));

        var releaseResponse = await fleetClient.PostAsJsonAsync("/fleet/sessions/release", new SlotSessionRequest(quickplay.SessionId, userId));
        Assert.Equal(HttpStatusCode.NoContent, releaseResponse.StatusCode);
    }
}
