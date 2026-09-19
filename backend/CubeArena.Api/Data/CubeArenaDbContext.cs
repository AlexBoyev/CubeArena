using CubeArena.Domain.Auth;
using CubeArena.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace CubeArena.Api.Data;

public class CubeArenaDbContext(DbContextOptions<CubeArenaDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

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
    }
}
