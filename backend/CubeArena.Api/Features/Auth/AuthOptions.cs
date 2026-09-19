namespace CubeArena.Api.Features.Auth;

public class AuthOptions
{
    public const string SectionName = "Auth";

    public required string SigningKey { get; init; }
    public string Issuer { get; init; } = "cubearena-api";
    public string Audience { get; init; } = "api";
    public TimeSpan AccessTokenLifetime { get; init; } = TimeSpan.FromMinutes(15);
    public TimeSpan RefreshTokenLifetime { get; init; } = TimeSpan.FromDays(30);

    public int LockoutThreshold { get; init; } = 5;
    public TimeSpan LockoutBaseDelay { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan LockoutMaxDelay { get; init; } = TimeSpan.FromHours(1);
}
