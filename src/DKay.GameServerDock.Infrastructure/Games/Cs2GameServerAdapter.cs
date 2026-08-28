using System.Text.RegularExpressions;
using DKay.GameServerDock.Application.Abstractions;
using DKay.GameServerDock.Application.Models;
using DKay.GameServerDock.Domain;

namespace DKay.GameServerDock.Infrastructure.Games;

public sealed partial class Cs2GameServerAdapter(Cs2RconClient rcon) : IGameServerAdapter
{
    private static readonly TimeSpan StatusCacheLifetime = TimeSpan.FromSeconds(3);
    private readonly BasicGameServerAdapter _validation = new("quit");
    private readonly Lock _statusCacheLock = new();
    private readonly Dictionary<Guid, StatusCacheEntry> _statusCache = [];

    public string GracefulStopCommand => "quit";
    public bool HandlesCommandsExternally => true;
    public string? PolicyReapplyCommand => Cs2RuntimePolicy.ReapplyCommand;

    public async Task<IReadOnlyList<PlayerInfo>> GetPlayersAsync(
        GameServerInstance server,
        CancellationToken cancellationToken) =>
        (await GetStatusAsync(server, cancellationToken)).Players;

    public async Task<string?> GetCurrentMapAsync(GameServerInstance server, CancellationToken cancellationToken) =>
        (await GetStatusAsync(server, cancellationToken)).Map;

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

    public static Cs2ServerStatus ParseStatus(string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return Cs2ServerStatus.Empty;
        }

        var mapMatch = CurrentMapPattern().Match(output);
        var map = mapMatch.Success ? NormalizeMapName(mapMatch.Groups["map"].Value) : null;
        var players = new List<PlayerInfo>();
        foreach (Match match in PlayerLinePattern().Matches(output))
        {
            var name = match.Groups["name"].Value.Replace("\\\"", "\"", StringComparison.Ordinal).Trim();
            var steamId = match.Groups["id"].Value;
            var tokens = match.Groups["details"].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var connectionIndex = Array.FindIndex(tokens, IsConnectionTime);
            var connectionTime = connectionIndex >= 0 ? ParseConnectionTime(tokens[connectionIndex]) : null;
            int? ping = connectionIndex >= 0 && connectionIndex + 1 < tokens.Length && int.TryParse(tokens[connectionIndex + 1], out var parsedPing)
                ? parsedPing
                : null;
            var playerId = string.Equals(steamId, "BOT", StringComparison.OrdinalIgnoreCase)
                ? $"BOT:{match.Groups["userid"].Value.Trim()}"
                : steamId;

            if (name.Length > 0)
            {
                players.Add(new PlayerInfo(name, playerId, ping, connectionTime));
            }
        }

        return new Cs2ServerStatus(map, players);
    }

    private async Task<Cs2ServerStatus> GetStatusAsync(
        GameServerInstance server,
        CancellationToken cancellationToken)
    {
        lock (_statusCacheLock)
        {
            if (_statusCache.TryGetValue(server.Id, out var cached) &&
                DateTimeOffset.UtcNow - cached.ReadAt < StatusCacheLifetime)
            {
                return cached.Status;
            }
        }

        Cs2ServerStatus status;
        try
        {
            status = ParseStatus(await rcon.ExecuteAsync(server, "status", cancellationToken, TimeSpan.Zero));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Runtime discovery is supporting information. A temporarily unavailable RCON listener
            // must not make the server detail or public guest page fail.
            status = Cs2ServerStatus.Empty;
        }

        lock (_statusCacheLock)
        {
            _statusCache[server.Id] = new StatusCacheEntry(DateTimeOffset.UtcNow, status);
        }

        return status;
    }

    private static string? NormalizeMapName(string value)
    {
        var normalized = value.Trim().Replace('\\', '/').Trim('/');
        if (normalized.Length == 0)
        {
            return null;
        }

        var separator = normalized.LastIndexOf('/');
        return separator >= 0 ? normalized[(separator + 1)..] : normalized;
    }

    private static bool IsConnectionTime(string value) => ConnectionTimePattern().IsMatch(value);

    private static TimeSpan? ParseConnectionTime(string value)
    {
        var parts = value.Split(':');
        if (parts.Length is < 2 or > 3 || parts.Any(part => !int.TryParse(part, out _)))
        {
            return null;
        }

        var numbers = parts.Select(int.Parse).ToArray();
        return parts.Length == 2
            ? new TimeSpan(0, numbers[0], numbers[1])
            : new TimeSpan(numbers[0], numbers[1], numbers[2]);
    }

    [GeneratedRegex(@"(?im)^\s*map\s*:\s*(?<map>[A-Za-z0-9_./\\-]+)", RegexOptions.CultureInvariant)]
    private static partial Regex CurrentMapPattern();

    [GeneratedRegex("""(?im)^\s*#\s+(?<userid>\d+(?:\s+\d+)?)\s+"(?<name>(?:\\.|[^"])*)"\s+(?<id>\S+)(?<details>[^\r\n]*)$""", RegexOptions.CultureInvariant)]
    private static partial Regex PlayerLinePattern();

    [GeneratedRegex(@"^\d{1,3}:\d{2}(?::\d{2})?$", RegexOptions.CultureInvariant)]
    private static partial Regex ConnectionTimePattern();

    private sealed record StatusCacheEntry(DateTimeOffset ReadAt, Cs2ServerStatus Status);
}

public sealed record Cs2ServerStatus(string? Map, IReadOnlyList<PlayerInfo> Players)
{
    public static Cs2ServerStatus Empty { get; } = new(null, []);
}
