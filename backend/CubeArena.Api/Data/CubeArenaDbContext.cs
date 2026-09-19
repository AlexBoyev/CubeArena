using CubeArena.Domain.Auth;
using CubeArena.Domain.Fleet;
using CubeArena.Domain.Sessions;
using CubeArena.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace CubeArena.Api.Data;

public class CubeArenaDbContext(DbContextOptions<CubeArenaDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<GameServer> GameServers => Set<GameServer>();
    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<SessionSlot> SessionSlots => Set<SessionSlot>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasIndex(u => u.Email).IsUnique();
            entity.Property(u => u.Email).HasMaxLength(320).IsRequired();
            entity.Property(u => u.DisplayName).HasMaxLength(64).IsRequired();
            entity.Property(u => u.PasswordHash).IsRequired();
        });

        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.HasIndex(t => t.TokenHash).IsUnique();
            entity.HasIndex(t => t.FamilyId);
            entity.Property(t => t.TokenHash).HasMaxLength(64).IsRequired();
            entity.HasOne<User>().WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<GameServer>(entity =>
        {
            entity.Property(g => g.Host).HasMaxLength(255).IsRequired();
        });

        modelBuilder.Entity<Session>(entity =>
        {
            entity.HasIndex(s => s.GameServerId).IsUnique();
            entity.HasOne<GameServer>().WithMany().HasForeignKey(s => s.GameServerId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SessionSlot>(entity =>
        {
            entity.HasIndex(s => s.SessionId);
            entity.HasIndex(s => new { s.SessionId, s.SlotIndex });
            entity.HasOne<Session>().WithMany().HasForeignKey(s => s.SessionId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
