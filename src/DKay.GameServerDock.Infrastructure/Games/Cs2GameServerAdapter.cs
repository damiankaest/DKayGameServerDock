using DKay.GameServerDock.Application.Abstractions;
using DKay.GameServerDock.Application.Models;
using DKay.GameServerDock.Domain;

namespace DKay.GameServerDock.Infrastructure.Games;

public sealed class Cs2GameServerAdapter(Cs2RconClient rcon) : IGameServerAdapter
{
    private readonly BasicGameServerAdapter _validation = new("quit");

    public string GracefulStopCommand => "quit";
    public bool HandlesCommandsExternally => true;

    public Task<IReadOnlyList<PlayerInfo>> GetPlayersAsync(
        GameServerInstance server,
        CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<PlayerInfo>>([]);

    public Task<string?> GetCurrentMapAsync(GameServerInstance server, CancellationToken cancellationToken) =>
        Task.FromResult<string?>(null);

    public string NormalizeConsoleCommand(string command) => _validation.NormalizeConsoleCommand(command);

    public async Task<ConsoleCommandResult> ExecuteConsoleCommandAsync(
        GameServerInstance server,
        IProcessSupervisor processes,
        string command,
        CancellationToken cancellationToken)
    {
        var uptime = processes.GetSnapshot(server.Id).Uptime;
        var listenerWait = uptime is not null && uptime < TimeSpan.FromMinutes(2)
            ? TimeSpan.FromSeconds(30)
            : TimeSpan.FromSeconds(2);
        var output = await rcon.ExecuteAsync(server, command, cancellationToken, listenerWait);
        return new ConsoleCommandResult("local-rcon", output);
    }
}
