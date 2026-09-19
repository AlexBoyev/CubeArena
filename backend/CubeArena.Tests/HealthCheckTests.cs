using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace CubeArena.Tests;

public class HealthCheckTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public HealthCheckTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("ConnectionStrings:Postgres", "Host=localhost;Port=5432;Database=test;Username=test;Password=test"));
    }

    [Fact]
    public async Task Live_ReturnsHealthy_WithoutTouchingDependencies()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }
}
