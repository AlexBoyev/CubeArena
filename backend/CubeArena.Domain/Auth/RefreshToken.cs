namespace CubeArena.Domain.Auth;

public class RefreshToken
{
    public Guid Id { get; init; }
    public required Guid UserId { get; init; }
    public required Guid FamilyId { get; init; }
    public required string TokenHash { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset ExpiresAtUtc { get; init; }
    public DateTimeOffset? ConsumedAtUtc { get; set; }
    public DateTimeOffset? RevokedAtUtc { get; set; }

    public bool IsActive(DateTimeOffset now) =>
        ConsumedAtUtc is null && RevokedAtUtc is null && ExpiresAtUtc > now;
}
