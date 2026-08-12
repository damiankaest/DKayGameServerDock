using DKay.GameServerDock.Domain;

namespace DKay.GameServerDock.Application.Abstractions;

public interface IServerRepository
{
    Task<IReadOnlyList<GameServerInstance>> ListAsync(CancellationToken cancellationToken);
    Task<GameServerInstance?> FindAsync(Guid id, CancellationToken cancellationToken);
    Task AddAsync(GameServerInstance server, CancellationToken cancellationToken);
    Task SaveAsync(GameServerInstance server, CancellationToken cancellationToken);
    Task DeleteAsync(GameServerInstance server, CancellationToken cancellationToken);
    Task<bool> IsPortAllocatedAsync(int port, CancellationToken cancellationToken);
    Task<IReadOnlyList<ServerEvent>> GetEventsAsync(Guid? serverId, int take, CancellationToken cancellationToken);
    Task AddEventAsync(ServerEvent serverEvent, CancellationToken cancellationToken);
}

public interface IUserRepository
{
    Task<bool> AnyAsync(CancellationToken cancellationToken);
    Task<LocalUser?> FindByNameAsync(string userName, CancellationToken cancellationToken);
    Task AddAsync(LocalUser user, CancellationToken cancellationToken);
}
