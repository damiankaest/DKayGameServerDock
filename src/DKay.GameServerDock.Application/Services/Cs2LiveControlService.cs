using System.Text.RegularExpressions;
using DKay.GameServerDock.Application.Abstractions;
using DKay.GameServerDock.Application.Models;
using DKay.GameServerDock.Domain;

namespace DKay.GameServerDock.Application.Services;

public sealed partial class Cs2LiveControlService(
    IServerRepository servers,
    IGameModuleRegistry modules,
    IProcessSupervisor processes,
    ICs2RuntimeControlStore store,
    IServerEventSink events,
    IClock clock)
{
    private static readonly IReadOnlyList<Cs2QuickActionDescriptor> ActionDescriptors =
    [
        new("start-warmup", "Start warmup", "Open a fresh warmup using the configured duration.", "Round", "◷"),
        new("end-warmup", "Start match", "End warmup and restart the game after one second.", "Round", "▶", "primary"),
        new("restart-round", "Restart round", "Restart the current round after one second.", "Round", "↻"),
        new("pause-match", "Pause match", "Pause the match at the next safe game state.", "Round", "Ⅱ"),
        new("resume-match", "Resume match", "Continue a previously paused match.", "Round", "▷"),
        new("swap-teams", "Swap teams", "Move Terrorists and Counter-Terrorists to the opposite side.", "Teams", "⇄"),
        new("scramble-teams", "Scramble teams", "Redistribute the current players across both teams.", "Teams", "⤨"),
        new("add-bot-ct", "Add CT bot", "Disable team limits and add exactly one CT bot.", "Bots", "+CT"),
        new("add-bot-t", "Add T bot", "Disable team limits and add exactly one T bot.", "Bots", "+T"),
        new("kill-bots", "Kill bots", "Enable private-server cheats and end every bot life.", "Bots", "⌁", "danger"),
        new("remove-bots", "Remove bots", "Kick every bot from the server.", "Bots", "−"),
        new("freeze-bots", "Freeze bots", "Enable cheats and stop bot movement for testing.", "Bots", "❄"),
        new("release-bots", "Release bots", "Allow frozen bots to move again.", "Bots", "☀"),
        new("rtv", "Start RTV vote", "Ask a compatible CounterStrikeSharp map-vote plugin to start RTV.", "Maps", "☑", RequiresPlugin: true)
    ];

    private static readonly IReadOnlyDictionary<string, string> ActionCommands =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["start-warmup"] = "mp_warmup_start",
            ["end-warmup"] = "mp_warmup_end; mp_restartgame 1",
            ["restart-round"] = "mp_restartgame 1",
            ["pause-match"] = "mp_pause_match",
            ["resume-match"] = "mp_unpause_match",
            ["swap-teams"] = "mp_swapteams",
            ["scramble-teams"] = "mp_scrambleteams",
            ["add-bot-ct"] = "mp_autoteambalance 0; mp_limitteams 0; bot_quota_mode normal; bot_add_ct",
            ["add-bot-t"] = "mp_autoteambalance 0; mp_limitteams 0; bot_quota_mode normal; bot_add_t",
            ["kill-bots"] = "sv_cheats 1; bot_kill",
            ["remove-bots"] = "bot_kick; bot_quota 0",
            ["freeze-bots"] = "sv_cheats 1; bot_stop 1",
            ["release-bots"] = "bot_stop 0",
            ["rtv"] = "css_rtv"
        };

    public async Task<Cs2LiveControlState> GetStateAsync(Guid serverId, CancellationToken cancellationToken)
    {
        var server = await GetCs2ServerAsync(serverId, cancellationToken);
        var snapshot = processes.GetSnapshot(server.Id);
        var running = server.Status == ServerStatus.Running && snapshot.IsRunning;
        var values = store.ReadLiveSettings(server).ToDictionary(
            pair => pair.Key,
            pair => pair.Value,
            StringComparer.Ordinal);
        var liveReads = 0;
        var failedReads = 0;

        if (running)
        {
            var adapter = modules.GetRequired(server.TemplateId).Adapter;
            foreach (var setting in store.SettingDefinitions)
            {
                try
                {
                    var result = await adapter.ExecuteConsoleCommandAsync(
                        server,
                        processes,
                        adapter.NormalizeConsoleCommand(setting.Key),
                        cancellationToken);
                    if (TryReadConsoleVariable(setting.Key, result.Output, out var value))
                    {
                        values[setting.Key] = value;
                        liveReads++;
                    }
                    else
                    {
                        failedReads++;
                    }
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    failedReads++;
                    if (liveReads == 0)
                    {
                        return BuildState(
                            server,
                            running,
                            values,
                            false,
                            $"Saved values are shown because live RCON reading failed: {exception.Message}");
                    }
                }
            }
        }

        var message = !running
            ? "Saved values are shown. They will be loaded automatically on the next server start."
            : failedReads == 0
                ? $"Read all {liveReads} settings directly from the running CS2 process."
                : $"Read {liveReads} live settings; {failedReads} unsupported values use their saved fallback.";
        return BuildState(server, running, values, running && liveReads > 0, message);
    }

    public async Task<Cs2LiveConfigurationApplyResult> ApplyAsync(
        Guid serverId,
        ApplyCs2LiveConfigurationRequest request,
        CancellationToken cancellationToken)
    {
        var server = await GetCs2ServerAsync(serverId, cancellationToken);
        var values = store.SaveLiveSettings(server, request.Values);
        var snapshot = processes.GetSnapshot(server.Id);
        var running = server.Status == ServerStatus.Running && snapshot.IsRunning;
        ConsoleCommandResult? result = null;

        if (running)
        {
            var adapter = modules.GetRequired(server.TemplateId).Adapter;
            result = await adapter.ExecuteConsoleCommandAsync(
                server,
                processes,
                adapter.NormalizeConsoleCommand("exec dkay-live.cfg"),
                cancellationToken);
        }

        var message = running
            ? "Live configuration applied to the running server and saved for every restart."
            : "Live configuration saved and queued for the next server start.";
        await events.RecordAsync(
            ServerEvent.Create(server.Id, ServerEventType.ConfigurationChanged, message, clock.UtcNow),
            cancellationToken);
        return new Cs2LiveConfigurationApplyResult(values, running, message, result?.Output);
    }

    public async Task<ConsoleCommandResult> RunActionAsync(
        Guid serverId,
        RunCs2QuickActionRequest request,
        CancellationToken cancellationToken)
    {
        var server = await GetCs2ServerAsync(serverId, cancellationToken);
        var snapshot = processes.GetSnapshot(server.Id);
        if (server.Status != ServerStatus.Running || !snapshot.IsRunning)
        {
            throw new InvalidOperationException("Start the CS2 server before using live controls.");
        }

        var command = request.ActionId switch
        {
            "change-map" => BuildChangeMapCommand(request.Value),
            _ when ActionCommands.TryGetValue(request.ActionId, out var knownCommand) => knownCommand,
            _ => throw new InvalidOperationException($"Unknown CS2 quick action '{request.ActionId}'.")
        };
        if (request.ActionId is "kill-bots" or "freeze-bots")
        {
            var persistentValues = store.ReadLiveSettings(server).ToDictionary(
                pair => pair.Key,
                pair => pair.Value,
                StringComparer.Ordinal);
            persistentValues["sv_cheats"] = "1";
            store.SaveLiveSettings(server, persistentValues);
        }

        var adapter = modules.GetRequired(server.TemplateId).Adapter;
        var result = await adapter.ExecuteConsoleCommandAsync(
            server,
            processes,
            adapter.NormalizeConsoleCommand(command),
            cancellationToken);
        await events.RecordAsync(
            ServerEvent.Create(
                server.Id,
                ServerEventType.ConfigurationChanged,
                $"CS2 live action '{request.ActionId}' executed.",
                clock.UtcNow),
            cancellationToken);
        return result;
    }

    public async Task<ConfigureCs2GsltResult> ConfigureGsltAsync(
        Guid serverId,
        ConfigureCs2GsltRequest request,
        CancellationToken cancellationToken)
    {
        var server = await GetCs2ServerAsync(serverId, cancellationToken);
        var state = store.SaveGsltToken(server, request.Token);
        var snapshot = processes.GetSnapshot(server.Id);
        var running = server.Status == ServerStatus.Running && snapshot.IsRunning;
        ConsoleCommandResult? result = null;
        if (running)
        {
            var adapter = modules.GetRequired(server.TemplateId).Adapter;
            result = await adapter.ExecuteConsoleCommandAsync(
                server,
                processes,
                adapter.NormalizeConsoleCommand("exec dkay-gslt.cfg"),
                cancellationToken);
        }

        var message = running
            ? "GSLT stored outside Steam-managed files and loaded into the running server. Restart once if Steam still reports LAN-only mode."
            : "GSLT stored outside Steam-managed files. It will load before the first map on the next start.";
        await events.RecordAsync(
            ServerEvent.Create(server.Id, ServerEventType.ConfigurationChanged, "CS2 Steam identity updated securely.", clock.UtcNow),
            cancellationToken);
        return new ConfigureCs2GsltResult(state, running, message, result?.Output);
    }

    private Cs2LiveControlState BuildState(
        GameServerInstance server,
        bool running,
        IReadOnlyDictionary<string, string> values,
        bool liveReadSucceeded,
        string liveReadMessage) => new(
            running,
            liveReadSucceeded,
            liveReadMessage,
            store.SettingDefinitions,
            values,
            ActionDescriptors,
            store.GetGsltState(server));

    private async Task<GameServerInstance> GetCs2ServerAsync(Guid serverId, CancellationToken cancellationToken)
    {
        var server = await servers.FindAsync(serverId, cancellationToken)
            ?? throw new KeyNotFoundException($"Server '{serverId}' was not found.");
        if (!string.Equals(server.TemplateId, "counter-strike-2", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Live CS2 controls are only available for Counter-Strike 2 servers.");
        }

        return server;
    }

    private static string BuildChangeMapCommand(string? value)
    {
        var map = value?.Trim() ?? string.Empty;
        if (!SafeMapName().IsMatch(map))
        {
            throw new InvalidOperationException("Map names may only contain letters, numbers, underscores and hyphens.");
        }

        return $"changelevel {map}";
    }

    internal static bool TryReadConsoleVariable(string key, string? output, out string value)
    {
        value = string.Empty;
        if (string.IsNullOrWhiteSpace(output))
        {
            return false;
        }

        var match = Regex.Match(
            output,
            $"(?:^|[\\r\\n\\s])\\\"?{Regex.Escape(key)}\\\"?\\s*=\\s*\\\"?(?<value>[^\\\"\\s\\(\\r\\n]+)",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            return false;
        }

        value = match.Groups["value"].Value.Trim();
        return value.Length > 0;
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{1,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeMapName();
}
