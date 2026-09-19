using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;

namespace CubeArena.Tests;

public static class TestFactoryExtensions
{
    public const string TestSigningKey = "test-signing-key-not-for-production-0123456789";

    // "Testing" environment skips Program.cs's automatic migration on startup so each
    // test fixture can control its own schema setup (EF InMemory, or a real Testcontainers
    // Postgres that needs an explicit Migrate() call).
    public static WebApplicationFactory<Program> ForTesting(this WebApplicationFactory<Program> factory, string postgresConnectionString) =>
        factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:Postgres", postgresConnectionString);
            builder.UseSetting("Auth:SigningKey", TestSigningKey);
            builder.UseSetting("RateLimiting:AuthPermitLimitPerMinute", "1000");
        });
}
