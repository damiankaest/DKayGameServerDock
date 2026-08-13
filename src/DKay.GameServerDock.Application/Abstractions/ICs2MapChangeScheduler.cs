using DKay.GameServerDock.Application.Models;
using DKay.GameServerDock.Domain;

namespace DKay.GameServerDock.Application.Abstractions;

public interface ICs2MapChangeScheduler
{
    Cs2MapChangeState GetState(Guid serverId);
    Task<Cs2MapChangeState> ScheduleAsync(
        GameServerInstance server,
        Cs2ModeProfile profile,
        TimeSpan delay,
        CancellationToken cancellationToken);
    Task<Cs2MapChangeState> CancelAsync(
        GameServerInstance server,
        CancellationToken cancellationToken);
}
