namespace CubeArena.Domain.Fleet;

public enum GameServerStatus
{
    Active,
    Offline
}

public class GameServer
{
    public Guid Id { get; init; }
    public required string Host { get; init; }
    public required int Port { get; init; }
    public int Capacity { get; init; } = 4;
    public GameServerStatus Status { get; set; } = GameServerStatus.Active;
    public DateTimeOffset RegisteredAtUtc { get; init; }
    public DateTimeOffset LastHeartbeatAtUtc { get; set; }
    public int ReportedPlayerCount { get; set; }
}
