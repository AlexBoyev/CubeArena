namespace CubeArena.Shared
{
    // Sent back to the client as NetworkManager's disconnect reason string (by name) when
    // a connect ticket fails validation. Mirrors the checks in the brief's section 3.2.
    public enum ConnectRejectionReason
    {
        InvalidTicketFormat,
        InvalidSignature,
        Expired,
        WrongIssuer,
        WrongAudience,
        WrongSession,
        ReplayedJti,
        ServerFull,
        // A malformed/stale ticket claiming a slot outside 0..capacity-1 — previously
        // this degraded silently instead of failing loudly (PlayerColors/SpawnPoints both
        // clamp out-of-range slots to the last valid one rather than erroring).
        InvalidSlot
    }
}
