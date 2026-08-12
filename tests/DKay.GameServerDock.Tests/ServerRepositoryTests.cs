using DKay.GameServerDock.Domain;
using DKay.GameServerDock.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DKay.GameServerDock.Tests;

public sealed class ServerRepositoryTests
{
    [Fact]
    public async Task GetEventsAsync_orders_and_limits_events_on_sqlite()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(CancellationToken.None);
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var database = new AppDbContext(options);
        await database.Database.EnsureCreatedAsync(CancellationToken.None);
        var repository = new ServerRepository(database);
        var serverId = Guid.NewGuid();
        var otherServerId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        await repository.AddEventAsync(ServerEvent.Create(serverId, ServerEventType.ServerStarted, "first", now), CancellationToken.None);
        await repository.AddEventAsync(ServerEvent.Create(otherServerId, ServerEventType.ServerStarted, "other", now.AddSeconds(1)), CancellationToken.None);
        await repository.AddEventAsync(ServerEvent.Create(serverId, ServerEventType.MapChanged, "second", now.AddSeconds(2)), CancellationToken.None);
        await repository.AddEventAsync(ServerEvent.Create(serverId, ServerEventType.ServerStopped, "third", now.AddSeconds(3)), CancellationToken.None);

        var events = await repository.GetEventsAsync(serverId, 2, CancellationToken.None);

        Assert.Collection(
            events,
            item => Assert.Equal("third", item.Message),
            item => Assert.Equal("second", item.Message));
    }

    [Fact]
    public async Task DeleteAsync_removes_the_server_and_its_events()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(CancellationToken.None);
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var database = new AppDbContext(options);
        await database.Database.EnsureCreatedAsync(CancellationToken.None);
        var repository = new ServerRepository(database);
        var server = GameServerInstance.Create(
            Guid.NewGuid(),
            "Disposable server",
            "counter-strike-2",
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")),
            "latest",
            27015,
            null,
            null,
            2048,
            "{}",
            DateTimeOffset.UtcNow);
        await repository.AddAsync(server, CancellationToken.None);
        await repository.AddEventAsync(
            ServerEvent.Create(server.Id, ServerEventType.InstallationStarted, "queued", DateTimeOffset.UtcNow),
            CancellationToken.None);

        await repository.DeleteAsync(server, CancellationToken.None);

        Assert.Null(await repository.FindAsync(server.Id, CancellationToken.None));
        Assert.Empty(await repository.GetEventsAsync(server.Id, 10, CancellationToken.None));
    }
}
