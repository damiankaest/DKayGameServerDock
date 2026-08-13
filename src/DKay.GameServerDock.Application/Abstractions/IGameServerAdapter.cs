using DKay.GameServerDock.Application.Models;
using DKay.GameServerDock.Domain;

namespace DKay.GameServerDock.Application.Abstractions;

public interface IGameServerAdapter
{
    string GracefulStopCommand { get; }
    bool HandlesCommandsExternally { get; }
    Task<IReadOnlyList<PlayerInfo>> GetPlayersAsync(GameServerInstance server, CancellationToken cancellationToken);
    Task<string?> GetCurrentMapAsync(GameServerInstance server, CancellationToken cancellationToken);
    string NormalizeConsoleCommand(string command);
    Task<ConsoleCommandResult> ExecuteConsoleCommandAsync(
        GameServerInstance server,
        IProcessSupervisor processes,
        string command,
        CancellationToken cancellationToken);
}
