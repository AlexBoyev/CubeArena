namespace CubeArena.Domain.Sessions;

public class Session
{
    public Guid Id { get; init; }
    public required Guid GameServerId { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
}
