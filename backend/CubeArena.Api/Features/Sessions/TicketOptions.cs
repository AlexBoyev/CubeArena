namespace CubeArena.Api.Features.Sessions;

public class TicketOptions
{
    public const string SectionName = "Tickets";

    // PEM-encoded EC (P-256) private key. Generate one with:
    //   openssl ecparam -name prime256v1 -genkey -noout
    public required string SigningKeyPem { get; init; }
    public string Issuer { get; init; } = "cubearena-api";
    public string Audience { get; init; } = "gameserver";
    public TimeSpan TicketLifetime { get; init; } = TimeSpan.FromSeconds(60);
    public string KeyId { get; init; } = "cubearena-ticket-key-1";
}
