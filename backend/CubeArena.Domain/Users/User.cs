namespace CubeArena.Domain.Users;

public class User
{
    public Guid Id { get; init; }
    public required string Email { get; init; }
    public required string DisplayName { get; set; }
    public required string PasswordHash { get; set; }
    public DateTimeOffset CreatedAtUtc { get; init; }

    public int FailedLoginCount { get; set; }
    public DateTimeOffset? LockedUntilUtc { get; set; }
}
