using DKay.GameServerDock.Application.Abstractions;
using DKay.GameServerDock.Application.Models;
using DKay.GameServerDock.Domain;
using DKay.GameServerDock.Infrastructure.Persistence;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace DKay.GameServerDock.Api.Hubs;

public sealed class SignalRServerEventSink(
    IDbContextFactory<AppDbContext> databaseFactory,
    IHubContext<ServerEventsHub> hub) : IServerEventSink
{
    public async Task RecordAsync(ServerEvent serverEvent, CancellationToken cancellationToken)
    {
        await using var database = await databaseFactory.CreateDbContextAsync(cancellationToken);
        database.ServerEvents.Add(serverEvent);
        await database.SaveChangesAsync(cancellationToken);

        var group = ServerEventsHub.GroupName(serverEvent.ServerId);
        if (serverEvent.Type == ServerEventType.ConsoleOutput)
        {
            await hub.Clients.Group(group).SendAsync(
                "consoleLine",
                new { serverEvent.OccurredAt, serverEvent.Message, serverEvent.DataJson },
                cancellationToken);
        }

        await hub.Clients.All.SendAsync("activity", new
        {
            serverEvent.ServerId,
            Type = serverEvent.Type.ToString(),
            serverEvent.Message,
            serverEvent.OccurredAt
        }, cancellationToken);
    }

    public Task PublishInstallationProgressAsync(
        Guid serverId,
        InstallationProgress progress,
        CancellationToken cancellationToken) =>
        hub.Clients.Group(ServerEventsHub.GroupName(serverId)).SendAsync("installationProgress", progress, cancellationToken);

    public Task PublishStatusAsync(Guid serverId, ServerStatus status, CancellationToken cancellationToken) =>
        hub.Clients.Group(ServerEventsHub.GroupName(serverId)).SendAsync("statusChanged", status.ToString(), cancellationToken);
}

