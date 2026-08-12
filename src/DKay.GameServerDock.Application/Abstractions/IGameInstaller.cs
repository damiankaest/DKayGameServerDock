using DKay.GameServerDock.Application.Models;
using DKay.GameServerDock.Domain;

namespace DKay.GameServerDock.Application.Abstractions;

public interface IGameInstaller
{
    Task InstallAsync(
        GameServerInstance server,
        Func<InstallationProgress, CancellationToken, Task> reportProgress,
        CancellationToken cancellationToken);

    Task UpdateAsync(
        GameServerInstance server,
        Func<InstallationProgress, CancellationToken, Task> reportProgress,
        CancellationToken cancellationToken);
}
