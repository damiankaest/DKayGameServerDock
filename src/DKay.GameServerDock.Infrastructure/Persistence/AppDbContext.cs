using DKay.GameServerDock.Domain;
using Microsoft.EntityFrameworkCore;

namespace DKay.GameServerDock.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<GameServerInstance> Servers => Set<GameServerInstance>();
    public DbSet<ServerEvent> ServerEvents => Set<ServerEvent>();
    public DbSet<LocalUser> Users => Set<LocalUser>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<GameServerInstance>(entity =>
        {
            entity.HasKey(server => server.Id);
            entity.Property(server => server.Name).HasMaxLength(120);
            entity.Property(server => server.TemplateId).HasMaxLength(80);
            entity.Property(server => server.InstallDirectory).HasMaxLength(1024);
            entity.Property(server => server.Version).HasMaxLength(80);
            entity.Property(server => server.Status).HasConversion<string>().HasMaxLength(32);
            entity.HasIndex(server => server.Port).IsUnique();
            entity.HasIndex(server => server.Status);
        });

        modelBuilder.Entity<ServerEvent>(entity =>
        {
            entity.HasKey(serverEvent => serverEvent.Id);
            entity.Property(serverEvent => serverEvent.Type).HasConversion<string>().HasMaxLength(64);
            entity.Property(serverEvent => serverEvent.Message).HasMaxLength(4000);
            entity.HasIndex(serverEvent => new { serverEvent.ServerId, serverEvent.OccurredAt });
        });

        modelBuilder.Entity<LocalUser>(entity =>
        {
            entity.HasKey(user => user.Id);
            entity.Property(user => user.UserName).HasMaxLength(80);
            entity.Property(user => user.PasswordHash).HasMaxLength(1024);
            entity.HasIndex(user => user.UserName).IsUnique();
        });
    }
}

