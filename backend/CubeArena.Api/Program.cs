using System.Threading.RateLimiting;
using CubeArena.Api.Data;
using CubeArena.Api.Features.Auth;
using CubeArena.Api.Features.Fleet;
using CubeArena.Api.Features.Sessions;
using HealthChecks.NpgSql;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole();

var postgresConnectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException("Missing ConnectionStrings:Postgres configuration.");

builder.Services.AddHealthChecks()
    .AddNpgSql(postgresConnectionString, name: "postgres", tags: ["ready"]);

builder.Services.AddDbContext<CubeArenaDbContext>(o => o.UseNpgsql(postgresConnectionString));

var authOptions = builder.Configuration.GetSection(AuthOptions.SectionName).Get<AuthOptions>()
    ?? throw new InvalidOperationException("Missing Auth configuration section.");
builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection(AuthOptions.SectionName));

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<PasswordHasher>();
builder.Services.AddScoped<TokenService>();
builder.Services.AddScoped<AuthService>();

builder.Services.Configure<FleetOptions>(builder.Configuration.GetSection(FleetOptions.SectionName));
builder.Services.AddScoped<FleetService>();
builder.Services.AddHostedService<StaleFleetSweepService>();

builder.Services.Configure<TicketOptions>(builder.Configuration.GetSection(TicketOptions.SectionName));
builder.Services.AddSingleton<TicketService>();
builder.Services.AddScoped<SessionService>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.MapInboundClaims = false;
        o.TokenValidationParameters = TokenService.BuildValidationParameters(authOptions);
    });
builder.Services.AddAuthorization();

var authPermitLimitPerMinute = builder.Configuration.GetValue("RateLimiting:AuthPermitLimitPerMinute", 10);

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("auth", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = authPermitLimitPerMinute,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
});

var app = builder.Build();

// Tier 0 (docs/HOSTING.md): the API always serves plain HTTP directly — TLS
// termination, when present, lives in front of it (Caddy/nginx, see tier 2/3).
// LAN_MODE doesn't change that; it's an explicit, loud acknowledgement that
// this plain-HTTP endpoint is intentionally reachable beyond localhost, on a
// trusted network only. See SECURITY.md's "LAN_MODE" section.
if (builder.Configuration.GetValue("LAN_MODE", false))
{
    Console.WriteLine("""

        ================================================================
         LAN_MODE is ON — serving plain HTTP with no TLS.
         Passwords, tokens, and connect tickets travel UNENCRYPTED.
         Only use this on a trusted local network you control (e.g. a
         LAN party). Never expose this configuration to the public
         internet. See SECURITY.md's "LAN_MODE" section.
        ================================================================
        """);
    app.Logger.LogWarning("LAN_MODE is enabled: serving plain HTTP, trusted-network-only. See SECURITY.md.");
}

if (!app.Environment.IsEnvironment("Testing"))
{
    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<CubeArenaDbContext>().Database.Migrate();
}

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapAuthEndpoints();
app.MapFleetEndpoints();
app.MapSessionEndpoints();

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});

app.Run();

public partial class Program;
