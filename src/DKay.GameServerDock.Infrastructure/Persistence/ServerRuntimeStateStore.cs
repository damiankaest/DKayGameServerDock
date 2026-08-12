using DKay.GameServerDock.Application.Abstractions;
using DKay.GameServerDock.Domain;
using Microsoft.EntityFrameworkCore;

namespace DKay.GameServerDock.Infrastructure.Persistence;

public sealed class ServerRuntimeStateStore(
    IDbContextFactory<AppDbContext> databaseFactory,
    IServerEventSink events,
    IClock clock) : IServerRuntimeStateStore
{
    public async Task MarkExitedAsync(Guid serverId, int exitCode, CancellationToken cancellationToken)
    {
        await using var database = await databaseFactory.CreateDbContextAsync(cancellationToken);
        var server = await database.Servers.FindAsync([serverId], cancellationToken);
        if (server is null)
        {
            return;
        }

        var expectedExit = server.Status == ServerStatus.Stopping;
        server.TrackProcess(null, exitCode, clock.UtcNow);
        server.ChangeStatus(expectedExit ? ServerStatus.Stopped : ServerStatus.Crashed, clock.UtcNow);
        await database.SaveChangesAsync(cancellationToken);

        var eventType = expectedExit ? ServerEventType.ServerStopped : ServerEventType.ServerCrashed;
        var message = expectedExit
            ? $"Server exited with code {exitCode}."
            : $"Server crashed with exit code {exitCode}.";
        await events.RecordAsync(ServerEvent.Create(serverId, eventType, message, clock.UtcNow), cancellationToken);
        await events.PublishStatusAsync(serverId, server.Status, cancellationToken);
    }
}

