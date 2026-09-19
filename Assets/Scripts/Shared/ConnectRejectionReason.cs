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
        ServerFull
    }
}
