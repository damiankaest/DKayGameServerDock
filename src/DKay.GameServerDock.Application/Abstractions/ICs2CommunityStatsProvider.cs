using DKay.GameServerDock.Application.Models;
using DKay.GameServerDock.Domain;

namespace DKay.GameServerDock.Application.Abstractions;

public interface ICs2CommunityStatsProvider
{
    Task<Cs2CommunityStats> GetAsync(
        GameServerInstance server,
        Cs2ModeState modeState,
        IReadOnlyList<ServerEvent> events,
        string? currentMap,
        CancellationToken cancellationToken);
}
