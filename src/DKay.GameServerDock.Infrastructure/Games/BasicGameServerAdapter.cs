using DKay.GameServerDock.Application.Abstractions;
using DKay.GameServerDock.Application.Models;
using DKay.GameServerDock.Domain;

namespace DKay.GameServerDock.Infrastructure.Games;

public sealed class BasicGameServerAdapter(string gracefulStopCommand, string? fixedMap = null) : IGameServerAdapter
{
    public string GracefulStopCommand { get; } = gracefulStopCommand;

    public Task<IReadOnlyList<PlayerInfo>> GetPlayersAsync(
        GameServerInstance server,
        CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<PlayerInfo>>([]);

    public Task<string?> GetCurrentMapAsync(GameServerInstance server, CancellationToken cancellationToken) =>
        Task.FromResult(fixedMap);

    public string NormalizeConsoleCommand(string command)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        var normalized = command.Trim();
        if (normalized.Length > 512 || normalized.Contains('\r') || normalized.Contains('\n') || normalized.Contains('\0'))
        {
            throw new InvalidOperationException("Console commands must be a single line with at most 512 characters.");
        }

        return normalized;
    }
}

