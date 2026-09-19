namespace CubeArena.Domain.Sessions;

// A reservation of one of a session's 0..3 slots. It counts against capacity
// until it expires (the player never actually connected within the ticket's
// lifetime) — there is no separate "confirmed" state yet, since nothing reports
// a successful game-server connection back to the backend until Phase 4.
public class SessionSlot
{
    public Guid Id { get; init; }
    public required Guid SessionId { get; init; }
    public required Guid UserId { get; init; }
    public required int SlotIndex { get; init; }
    public DateTimeOffset ReservedAtUtc { get; init; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
}
