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
    ICs2ModeManager modes,
    ICs2MapChangeScheduler mapChanges,
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
        new("combat-peaceful", "Peaceful", "Weapons stay available, but nobody can deal player damage.", "Teams", "☮"),
        new("combat-team", "CT vs T", "Opponents take normal damage while teammates remain protected.", "Teams", "VS"),
        new("combat-ffa", "Free for all", "Every other player is an enemy, independent of the assigned team.", "Teams", "FFA"),
        new("repair-team-damage", "Reapply combat profile", "Restore the selected peaceful, team or FFA policy after a plugin or map changed it.", "Teams", "HP", "primary"),
        new("add-bot-ct", "Add CT bot", "Disable team limits and add exactly one CT bot.", "Bots", "+CT"),
        new("add-bot-t", "Add T bot", "Disable team limits and add exactly one T bot.", "Bots", "+T"),
        new("kill-bots", "Kill bots", "Enable private-server cheats and end every bot life.", "Bots", "⌁", "danger"),
        new("remove-bots", "Remove bots", "Kick every bot from the server.", "Bots", "−"),
        new("freeze-bots", "Freeze bots", "Enable cheats and stop bot movement for testing.", "Bots", "❄"),
        new("release-bots", "Release bots", "Allow frozen bots to move again.", "Bots", "☀"),
        new("enable-bhop", "Enable auto-bhop", "Enable uncapped bunnyhopping and jump automatically while jump is held.", "Movement", "↗", "primary"),
        new("disable-bhop", "Disable auto-bhop", "Return jumping and the movement speed cap to normal CS2 behavior.", "Movement", "↘"),
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
            ["enable-bhop"] = "sv_enablebunnyhopping 1; sv_autobunnyhopping 1",
            ["disable-bhop"] = "sv_autobunnyhopping 0; sv_enablebunnyhopping 0",
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
                    if (TryReadConsoleVariable(setting.Key, result.Output, out var value) &&
                        TryNormalizeReportedValue(setting, value, out var normalized))
                    {
                        values[setting.Key] = normalized;
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
        var previousValues = store.ReadLiveSettings(server);
        var knownKeys = store.SettingDefinitions.Select(setting => setting.Key).ToHashSet(StringComparer.Ordinal);
        var unknownChangedKey = request.ChangedKeys?.FirstOrDefault(key => !knownKeys.Contains(key));
        if (unknownChangedKey is not null)
        {
            throw new InvalidOperationException($"Live setting '{unknownChangedKey}' is not supported.");
        }

        var values = store.SaveLiveSettings(server, request.Values);
        var changedKeys = request.ChangedKeys is null
            ? values.Where(pair => !previousValues.TryGetValue(pair.Key, out var previous) || previous != pair.Value)
                .Select(pair => pair.Key)
                .ToHashSet(StringComparer.Ordinal)
            : request.ChangedKeys.ToHashSet(StringComparer.Ordinal);
        var liveApplyCommand = BuildLiveApplyCommand(changedKeys);
        var botsChanged = liveApplyCommand.StartsWith("bot_kick", StringComparison.Ordinal);
        var snapshot = processes.GetSnapshot(server.Id);
        var running = server.Status == ServerStatus.Running && snapshot.IsRunning;
        ConsoleCommandResult? result = null;

        if (running)
        {
            var adapter = modules.GetRequired(server.TemplateId).Adapter;
            result = await adapter.ExecuteConsoleCommandAsync(
                server,
                processes,
                adapter.NormalizeConsoleCommand(liveApplyCommand),
                cancellationToken);
        }

        var message = running && botsChanged
            ? "Live configuration applied. Bots were recreated so quota, movement state and difficulty take effect immediately."
            : running
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

        var combatMode = ResolveCombatModeAction(request.ActionId);
        if (request.ActionId == "repair-team-damage")
        {
            var savedState = await modes.GetStateAsync(server, cancellationToken);
            combatMode = savedState.Profiles.FirstOrDefault(profile =>
                string.Equals(profile.Id, savedState.ActiveProfileId, StringComparison.Ordinal))?.CombatMode
                ?? throw new InvalidOperationException("Select and activate a map profile before reapplying its combat mode.");
        }
        var sharpTimerInstalled = false;
        if (combatMode is not null)
        {
            await modes.SetActiveCombatModeAsync(server, combatMode, cancellationToken);
            var modeState = await modes.GetStateAsync(server, cancellationToken);
            sharpTimerInstalled = modeState.Packages.Any(package =>
                string.Equals(package.Id, "sharp-timer", StringComparison.Ordinal) && package.Installed);
            var persistentValues = store.ReadLiveSettings(server).ToDictionary(
                pair => pair.Key,
                pair => pair.Value,
                StringComparer.Ordinal);
            foreach (var (key, value) in BuildCombatLiveValues(combatMode))
            {
                persistentValues[key] = value;
            }

            store.SaveLiveSettings(server, persistentValues);
        }

        var command = request.ActionId switch
        {
            "change-map" => BuildChangeMapCommand(request.Value),
            _ when combatMode is not null => BuildCombatApplyCommand(combatMode, sharpTimerInstalled),
            _ when ActionCommands.TryGetValue(request.ActionId, out var knownCommand) => knownCommand,
            _ => throw new InvalidOperationException($"Unknown CS2 quick action '{request.ActionId}'.")
        };

        if (request.ActionId is "kill-bots" or "freeze-bots" or "release-bots" or "enable-bhop" or "disable-bhop")
        {
            var persistentValues = store.ReadLiveSettings(server).ToDictionary(
                pair => pair.Key,
                pair => pair.Value,
                StringComparer.Ordinal);
            if (request.ActionId is "kill-bots" or "freeze-bots")
            {
                persistentValues["sv_cheats"] = "1";
            }

            if (request.ActionId == "freeze-bots") persistentValues["bot_stop"] = "1";
            if (request.ActionId == "release-bots") persistentValues["bot_stop"] = "0";
            if (request.ActionId == "enable-bhop")
            {
                persistentValues["sv_enablebunnyhopping"] = "1";
                persistentValues["sv_autobunnyhopping"] = "1";
            }

            if (request.ActionId == "disable-bhop")
            {
                persistentValues["sv_autobunnyhopping"] = "0";
                persistentValues["sv_enablebunnyhopping"] = "0";
            }

            store.SaveLiveSettings(server, persistentValues);
        }

        var adapter = modules.GetRequired(server.TemplateId).Adapter;
        var result = await adapter.ExecuteConsoleCommandAsync(
            server,
            processes,
            adapter.NormalizeConsoleCommand(command),
            cancellationToken);
        if (combatMode is not null)
        {
            var verificationCommand = BuildCombatVerificationCommand(sharpTimerInstalled);
            var verification = await adapter.ExecuteConsoleCommandAsync(
                server,
                processes,
                adapter.NormalizeConsoleCommand(verificationCommand),
                cancellationToken);
            var failures = FindCombatVerificationFailures(
                BuildCombatLiveValues(combatMode),
                sharpTimerInstalled ? (combatMode == "peaceful" ? "1" : "0") : null,
                verification.Output);
            if (failures.Count > 0)
            {
                throw new InvalidOperationException(
                    $"CS2 accepted the combat action, but live verification failed for {string.Join(", ", failures)}. " +
                    "The saved profile remains intact; use Reapply combat profile or inspect the Console for a plugin override.");
            }

            result = result with
            {
                Output = $"Live combat mode '{combatMode}' applied and verified without restarting the round."
            };
        }
        await events.RecordAsync(
            ServerEvent.Create(
                server.Id,
                ServerEventType.ConfigurationChanged,
                $"CS2 live action '{request.ActionId}' executed.",
                clock.UtcNow),
            cancellationToken);
        return result;
    }

    internal static string BuildLiveApplyCommand(IReadOnlySet<string> changedKeys) =>
        changedKeys.Overlaps(new[] { "bot_quota", "bot_difficulty", "bot_quota_mode", "bot_stop" })
            ? "bot_kick; exec dkay-live.cfg"
            : "exec dkay-live.cfg";

    internal static string? ResolveCombatModeAction(string actionId) => actionId switch
    {
        "combat-peaceful" => "peaceful",
        "combat-team" => "team",
        "combat-ffa" => "ffa",
        _ => null
    };

    internal static IReadOnlyDictionary<string, string> BuildCombatLiveValues(string combatMode)
    {
        if (combatMode is not ("peaceful" or "team" or "ffa"))
        {
            throw new ArgumentException("Combat mode must be peaceful, team or ffa.", nameof(combatMode));
        }

        var damageScale = combatMode == "peaceful" ? "0" : "1";
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["mp_friendlyfire"] = combatMode == "ffa" ? "1" : "0",
            ["mp_teammates_are_enemies"] = combatMode == "ffa" ? "1" : "0",
            ["mp_damage_scale_ct_head"] = damageScale,
            ["mp_damage_scale_ct_body"] = damageScale,
            ["mp_damage_scale_t_head"] = damageScale,
            ["mp_damage_scale_t_body"] = damageScale,
            ["mp_damage_headshot_only"] = "0"
        };
    }

    internal static string BuildCombatApplyCommand(string combatMode, bool sharpTimerInstalled)
    {
        var commands = BuildCombatLiveValues(combatMode)
            .Select(pair => $"{pair.Key} {pair.Value}")
            .ToList();
        if (sharpTimerInstalled)
        {
            commands.Add($"sharptimer_remove_damage {(combatMode == "peaceful" ? "1" : "0")}");
        }

        return string.Join("; ", commands);
    }

    internal static string BuildCombatVerificationCommand(bool sharpTimerInstalled)
    {
        var keys = BuildCombatLiveValues("team").Keys.ToList();
        if (sharpTimerInstalled)
        {
            keys.Add("sharptimer_remove_damage");
        }

        return string.Join("; ", keys);
    }

    internal static IReadOnlyList<string> FindCombatVerificationFailures(
        IReadOnlyDictionary<string, string> expectedValues,
        string? expectedSharpTimerRemoveDamage,
        string? output)
    {
        var failures = expectedValues
            .Where(pair => !TryReadConsoleVariable(pair.Key, output, out var reported) ||
                           !ConsoleValuesMatch(pair.Value, reported))
            .Select(pair => pair.Key)
            .ToList();
        if (expectedSharpTimerRemoveDamage is not null &&
            (!TryReadConsoleVariable("sharptimer_remove_damage", output, out var sharpTimerValue) ||
             !ConsoleValuesMatch(expectedSharpTimerRemoveDamage, sharpTimerValue)))
        {
            failures.Add("sharptimer_remove_damage");
        }

        return failures;
    }

    private static bool ConsoleValuesMatch(string expected, string reported)
    {
        reported = reported.Trim().Trim('"');
        reported = reported.ToLowerInvariant() switch
        {
            "true" => "1",
            "false" => "0",
            _ => reported
        };
        return decimal.TryParse(expected, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var expectedNumber) &&
               decimal.TryParse(reported, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var reportedNumber)
            ? expectedNumber == reportedNumber
            : string.Equals(expected, reported, StringComparison.OrdinalIgnoreCase);
    }

    public async Task<Cs2MapChangeState> GetMapChangeStateAsync(
        Guid serverId,
        CancellationToken cancellationToken)
    {
        _ = await GetCs2ServerAsync(serverId, cancellationToken);
        return mapChanges.GetState(serverId);
    }

    public async Task<Cs2MapChangeState> ScheduleMapChangeAsync(
        Guid serverId,
        ScheduleCs2MapChangeRequest request,
        CancellationToken cancellationToken)
    {
        var server = await GetCs2ServerAsync(serverId, cancellationToken);
        var snapshot = processes.GetSnapshot(server.Id);
        if (server.Status != ServerStatus.Running || !snapshot.IsRunning)
        {
            throw new InvalidOperationException("Start the CS2 server before scheduling a map change.");
        }

        int[] allowedDelays = [0, 10, 30, 60, 120, 300];
        if (!allowedDelays.Contains(request.DelaySeconds))
        {
            throw new InvalidOperationException("Choose a map-change delay of 0, 10, 30, 60, 120 or 300 seconds.");
        }

        var modeState = await modes.GetStateAsync(server, cancellationToken);
        var profile = modeState.Profiles.FirstOrDefault(item =>
            string.Equals(item.Id, request.ProfileId, StringComparison.Ordinal))
            ?? throw new KeyNotFoundException($"CS2 map profile '{request.ProfileId}' was not found.");
        return await mapChanges.ScheduleAsync(
            server,
            profile,
            TimeSpan.FromSeconds(request.DelaySeconds),
            cancellationToken);
    }

    public async Task<Cs2MapChangeState> CancelMapChangeAsync(
        Guid serverId,
        CancellationToken cancellationToken)
    {
        var server = await GetCs2ServerAsync(serverId, cancellationToken);
        return await mapChanges.CancelAsync(server, cancellationToken);
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
            store.GetGsltState(server),
            mapChanges.GetState(server.Id));

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

    internal static bool TryNormalizeReportedValue(
        Cs2LiveSettingDescriptor setting,
        string value,
        out string normalized)
    {
        normalized = string.Empty;
        value = value.Trim().Trim('"');
        if (setting.Type == "boolean")
        {
            normalized = value.ToLowerInvariant() switch
            {
                "1" or "true" => "1",
                "0" or "false" => "0",
                _ => string.Empty
            };
            return normalized.Length > 0;
        }

        if (setting.Options is { Count: > 0 })
        {
            normalized = setting.Options.FirstOrDefault(option =>
                string.Equals(option, value, StringComparison.OrdinalIgnoreCase)) ?? string.Empty;
            return normalized.Length > 0;
        }

        if (setting.Type is "integer" or "decimal")
        {
            if (!decimal.TryParse(value, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var number) ||
                setting.Type == "integer" && number != decimal.Truncate(number) ||
                setting.Minimum is { } minimum && number < minimum ||
                setting.Maximum is { } maximum && number > maximum)
            {
                return false;
            }

            normalized = number.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return true;
        }

        normalized = value;
        return normalized.Length > 0;
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{1,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeMapName();
}
