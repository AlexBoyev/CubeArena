namespace CubeArena.Api.Features.Fleet;

public class FleetOptions
{
    public const string SectionName = "Fleet";

    public required string ApiKey { get; init; }
    public TimeSpan HeartbeatInterval { get; init; } = TimeSpan.FromSeconds(10);
    public int MissedHeartbeatsBeforeStale { get; init; } = 3;

    public TimeSpan StaleThreshold => HeartbeatInterval * MissedHeartbeatsBeforeStale;
}
