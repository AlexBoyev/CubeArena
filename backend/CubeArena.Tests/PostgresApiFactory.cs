using CubeArena.Api.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Xunit;

namespace CubeArena.Tests;

// Spins up a real Postgres container so integration tests exercise the same
// EF Core/Npgsql path as production, per docs/ROADMAP.md's testing bar.
public class PostgresApiFactory : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    private WebApplicationFactory<Program>? _factory;

    public HttpClient CreateClient() => Factory.CreateClient();
    public IServiceProvider Services => Factory.Services;

    private WebApplicationFactory<Program> Factory =>
        _factory ?? throw new InvalidOperationException("Factory not initialized yet.");

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        _factory = new WebApplicationFactory<Program>().ForTesting(_container.GetConnectionString());

        using var scope = _factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<CubeArenaDbContext>().Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        await _container.DisposeAsync();
    }
}
