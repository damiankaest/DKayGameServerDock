using DKay.GameServerDock.Application.Abstractions;
using DKay.GameServerDock.Domain;
using Microsoft.EntityFrameworkCore;

namespace DKay.GameServerDock.Infrastructure.Persistence;

public sealed class ServerRepository(AppDbContext database) : IServerRepository
{
    public async Task<IReadOnlyList<GameServerInstance>> ListAsync(CancellationToken cancellationToken) =>
        await database.Servers.AsNoTracking().OrderBy(server => server.Name).ToListAsync(cancellationToken);

    public async Task<GameServerInstance?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        await database.Servers.FindAsync([id], cancellationToken);

    public async Task AddAsync(GameServerInstance server, CancellationToken cancellationToken)
    {
        database.Servers.Add(server);
        await database.SaveChangesAsync(cancellationToken);
    }

    public async Task SaveAsync(GameServerInstance server, CancellationToken cancellationToken)
    {
        if (database.Entry(server).State == EntityState.Detached)
        {
            database.Servers.Update(server);
        }

        await database.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(GameServerInstance server, CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        await database.ServerEvents
            .Where(serverEvent => serverEvent.ServerId == server.Id)
            .ExecuteDeleteAsync(cancellationToken);
        database.Servers.Remove(server);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public Task<bool> IsPortAllocatedAsync(int port, CancellationToken cancellationToken) =>
        database.Servers.AnyAsync(server => server.Port == port, cancellationToken);

    public async Task<IReadOnlyList<ServerEvent>> GetEventsAsync(
        Guid? serverId,
        int take,
        CancellationToken cancellationToken)
    {
        var query = database.ServerEvents.AsNoTracking();
        if (serverId.HasValue)
        {
            query = query.Where(serverEvent => serverEvent.ServerId == serverId.Value);
        }

        return await query
            // SQLite cannot translate ordering by DateTimeOffset. Event IDs are generated in
            // insertion order, so they provide the same chronology without client-side loading.
            .OrderByDescending(serverEvent => serverEvent.Id)
            .Take(Math.Clamp(take, 1, 1000))
            .ToListAsync(cancellationToken);
    }

    public async Task AddEventAsync(ServerEvent serverEvent, CancellationToken cancellationToken)
    {
        database.ServerEvents.Add(serverEvent);
        await database.SaveChangesAsync(cancellationToken);
    }
}
