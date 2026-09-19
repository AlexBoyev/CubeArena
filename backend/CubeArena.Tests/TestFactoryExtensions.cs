using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;

namespace CubeArena.Tests;

public static class TestFactoryExtensions
{
    public const string TestSigningKey = "test-signing-key-not-for-production-0123456789";
    public const string TestFleetApiKey = "test-fleet-api-key-not-for-production";

    public const string TestTicketSigningKeyPem = """
        -----BEGIN EC PRIVATE KEY-----
        MHcCAQEEIPL41AQaBDyEWp74wh4x7HHJTLk/H+wGMuX8vhClj5BboAoGCCqGSM49
        AwEHoUQDQgAEu1onIul7buAJHIFBughJQsNefqRgfZtEkRifDqF9/ogN8HMejaZK
        XtRU0QIkBnTqI04BXCQKH4y+H2jUTvVfLw==
        -----END EC PRIVATE KEY-----
        """;

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
            builder.UseSetting("Fleet:ApiKey", TestFleetApiKey);
            builder.UseSetting("Tickets:SigningKeyPem", TestTicketSigningKeyPem);
        });
}
