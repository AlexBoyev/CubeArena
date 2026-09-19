using System.Net;
using System.Net.Http.Json;
using CubeArena.Api.Features.Fleet;

namespace CubeArena.Tests.Fleet;

public class FleetEndpointsTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    private HttpClient CreateAuthenticatedClient()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Fleet-Api-Key", TestFactoryExtensions.TestFleetApiKey);
        return client;
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
}
