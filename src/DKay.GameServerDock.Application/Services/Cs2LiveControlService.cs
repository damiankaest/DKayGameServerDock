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
        new("combat-enemy-on", "Enemy damage on", "Opponents take normal damage immediately.", "Teams", "ON", "primary"),
        new("combat-enemy-off", "Enemy damage off", "Block all player damage immediately. Team damage is disabled with it.", "Teams", "OFF"),
        new("combat-team-on", "Team damage on", "Allow damage between all players, including players CS2 placed on the same team.", "Teams", "ON", "danger"),
        new("combat-team-off", "Team damage off", "Protect teammates while keeping enemy damage enabled.", "Teams", "OFF"),
        new("repair-team-damage", "Force current damage policy", "Reapply the global damage policy after a plugin or map changed it.", "Teams", "HP", "primary"),
        new("add-bot-ct", "Add CT bot", "Disable team limits and add exactly one CT bot.", "Bots", "+CT"),
        new("add-bot-t", "Add T bot", "Disable team limits and add exactly one T bot.", "Bots", "+T"),
        new("kill-bots", "Kill bots", "Enable private-server cheats and end every bot life.", "Bots", "⌁", "danger"),
        new("remove-bots", "Remove bots", "Kick every bot from the server.", "Bots", "−"),
        new("freeze-bots", "Freeze bots", "Enable cheats and stop bot movement for testing.", "Bots", "❄"),
        new("release-bots", "Release bots", "Allow frozen bots to move again.", "Bots", "☀"),
        new("enable-bhop", "Enable auto-bhop", "Enable uncapped bunnyhopping and jump automatically while jump is held.", "Movement", "↗"),
        new("disable-bhop", "Disable auto-bhop", "Return jumping and the movement speed cap to normal CS2 behavior.", "Movement", "↘"),
        new("respawn-round", "Play the round", "Dead players wait until the current round has been decided.", "Round", "RND"),
        new("respawn-instant", "Always respawn", "Players return immediately and normal round win conditions stay disabled.", "Round", "∞"),
        new("hud-hidden", "Clean screen", "Hide the SharpTimer timer, keys, speed and sync display.", "Display", "○", RequiresPlugin: true),
        new("hud-timer", "Timer only", "Show run and map timing without movement telemetry.", "Display", "◷", RequiresPlugin: true),
        new("hud-movement", "Movement HUD", "Show timing, keys, velocity and strafe sync.", "Display", "HUD", RequiresPlugin: true),
        new("practice-disabled", "Timer only", "Keep timer and rankings active, but disable player checkpoint commands.", "Practice", "T", RequiresPlugin: true),
        new("practice-ground", "Ground checkpoints", "Allow !cp and !tp while keeping SharpTimer's safe checkpoint restrictions.", "Practice", "CP", RequiresPlugin: true),
        new("practice-anywhere", "Surf practice", "Allow in-air checkpoints that preserve the player's current speed.", "Practice", "AIR", RequiresPlugin: true),
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

    private static readonly string[] CombatLiveKeys =
    [
        "mp_friendlyfire",
        "mp_teammates_are_enemies",
        "mp_damage_scale_ct_head",
        "mp_damage_scale_ct_body",
        "mp_damage_scale_t_head",
        "mp_damage_scale_t_body",
        "mp_damage_headshot_only",
        "mp_respawn_immunitytime"
    ];

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
        var liveValueKeys = new HashSet<string>(StringComparer.Ordinal);
        string? readFailureMessage = null;
        string? readFailureDetail = null;
        var modeState = await modes.GetStateAsync(server, cancellationToken);
        var activeProfile = modeState.Profiles.FirstOrDefault(profile =>
            string.Equals(profile.Id, modeState.ActiveProfileId, StringComparison.Ordinal));
        var combatOverride = store.ReadCombatModeOverride(server);
        var activeCombatMode = combatOverride ?? activeProfile?.CombatMode ?? "team";
        var combatLiveReadSucceeded = false;
        var activeHudMode = activeProfile?.HudMode ?? "hidden";
        var activePracticeMode = activeProfile?.PracticeMode ?? "disabled";
        var hudLiveReadSucceeded = false;
        var practiceLiveReadSucceeded = false;
        var sharpTimerInstalled = modeState.Packages.Any(package =>
            string.Equals(package.Id, "sharp-timer", StringComparison.Ordinal) && package.Installed);

        if (running)
        {
            var adapter = modules.GetRequired(server.TemplateId).Adapter;
            var outputs = new List<string?>();
            try
            {
                foreach (var command in BuildLiveReadCommands(store.SettingDefinitions))
                {
                    var result = await adapter.ExecuteConsoleCommandAsync(
                        server,
                        processes,
                        adapter.NormalizeConsoleCommand(command),
                        cancellationToken);
                    outputs.Add(result.Output);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                readFailureDetail = exception.Message;
            }

            var liveOutput = string.Join(Environment.NewLine, outputs);
            foreach (var setting in store.SettingDefinitions)
            {
                if (TryReadConsoleVariable(setting.Key, liveOutput, out var value) &&
                    TryNormalizeReportedValue(setting, value, out var normalized))
                {
                    values[setting.Key] = normalized;
                    liveValueKeys.Add(setting.Key);
                    liveReads++;
                }
                else
                {
                    failedReads++;
                }
            }

            combatLiveReadSucceeded = CombatLiveKeys.All(liveValueKeys.Contains);
            if (combatLiveReadSucceeded)
            {
                activeCombatMode = ResolveCombatModeFromValues(values);
            }

            if (readFailureDetail is not null)
            {
                readFailureMessage = liveReads > 0
                    ? $"Read {liveReads} settings from CS2; remaining values use their saved fallback because RCON reading stopped: {readFailureDetail}"
                    : $"Saved values are shown because live RCON reading failed: {readFailureDetail}";
            }

            if (sharpTimerInstalled && readFailureMessage is null)
            {
                try
                {
                    var sharpTimerValues = BuildHudLiveValues("movement")
                        .Concat(BuildPracticeLiveValues("anywhere"))
                        .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
                    var hudResult = await adapter.ExecuteConsoleCommandAsync(
                        server,
                        processes,
                        adapter.NormalizeConsoleCommand(BuildVerificationCommand(sharpTimerValues)),
                        cancellationToken);
                    if (TryResolveReportedHudMode(hudResult.Output, out var reportedHudMode))
                    {
                        activeHudMode = reportedHudMode;
                        hudLiveReadSucceeded = true;
                    }
                    if (TryResolveReportedPracticeMode(hudResult.Output, out var reportedPracticeMode))
                    {
                        activePracticeMode = reportedPracticeMode;
                        practiceLiveReadSucceeded = true;
                    }
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    readFailureMessage ??= $"Core values are live; the SharpTimer HUD status could not be read: {exception.Message}";
                }
            }
        }

        var message = readFailureMessage ?? (!running
            ? "Saved values are shown. They will be loaded automatically on the next server start."
            : failedReads == 0
                ? $"Read all {liveReads} settings directly from the running CS2 process."
                : $"Read {liveReads} live settings; {failedReads} unsupported values use their saved fallback.");
        return BuildState(
            server,
            running,
            values,
            running && liveReads > 0,
            message,
            liveValueKeys,
            activeHudMode,
            hudLiveReadSucceeded,
            activePracticeMode,
            practiceLiveReadSucceeded,
            sharpTimerInstalled,
            activeCombatMode,
            combatLiveReadSucceeded,
            combatOverride is not null);
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

        var currentCombatMode = store.ReadCombatModeOverride(server);
        if (currentCombatMode is null)
        {
            var currentModeState = await modes.GetStateAsync(server, cancellationToken);
            currentCombatMode = currentModeState.Profiles.FirstOrDefault(profile =>
                string.Equals(profile.Id, currentModeState.ActiveProfileId, StringComparison.Ordinal))?.CombatMode
                ?? "team";
        }
        var combatMode = ResolveCombatModeAction(request.ActionId, currentCombatMode);
        var respawnMode = ResolveRespawnModeAction(request.ActionId);
        var hudMode = ResolveHudModeAction(request.ActionId);
        var practiceMode = ResolvePracticeModeAction(request.ActionId);
        if (request.ActionId == "repair-team-damage")
        {
            combatMode = currentCombatMode;
        }
        var sharpTimerInstalled = false;
        if (combatMode is not null || hudMode is not null || practiceMode is not null)
        {
            var modeState = await modes.GetStateAsync(server, cancellationToken);
            sharpTimerInstalled = modeState.Packages.Any(package =>
                string.Equals(package.Id, "sharp-timer", StringComparison.Ordinal) && package.Installed);
        }

        if ((hudMode is not null || practiceMode is not null) && !sharpTimerInstalled)
        {
            throw new InvalidOperationException("Install SharpTimer before changing its in-game timer or practice policy.");
        }

        if (combatMode is not null)
        {
            // Persist this first so every configuration writer sees the global administrator
            // decision instead of the active preset's default while this action is running.
            store.SaveCombatModeOverride(server, combatMode);
            await modes.SetActiveCombatModeAsync(server, combatMode, cancellationToken);
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

        if (respawnMode is not null)
        {
            await modes.SetActiveRespawnModeAsync(server, respawnMode, cancellationToken);
            var persistentValues = store.ReadLiveSettings(server).ToDictionary(
                pair => pair.Key,
                pair => pair.Value,
                StringComparer.Ordinal);
            foreach (var (key, value) in BuildRespawnLiveValues(respawnMode))
            {
                persistentValues[key] = value;
            }

            store.SaveLiveSettings(server, persistentValues);
        }

        if (hudMode is not null)
        {
            await modes.SetActiveHudModeAsync(server, hudMode, cancellationToken);
        }

        if (practiceMode is not null)
        {
            await modes.SetActivePracticeModeAsync(server, practiceMode, cancellationToken);
        }

        var command = request.ActionId switch
        {
            "change-map" => BuildChangeMapCommand(request.Value),
            _ when combatMode is not null => BuildCombatApplyCommand(combatMode, sharpTimerInstalled),
            _ when respawnMode is not null => BuildApplyCommand(BuildRespawnLiveValues(respawnMode)),
            _ when hudMode is not null => BuildApplyCommand(BuildHudLiveValues(hudMode)),
            _ when practiceMode is not null => BuildApplyCommand(BuildPracticeLiveValues(practiceMode)),
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
                null,
                verification.Output);
            if (failures.Count > 0)
            {
                throw new InvalidOperationException(
                    $"CS2 accepted the combat action, but live verification failed for {string.Join(", ", failures)}. " +
                    "The saved profile remains intact; use Reapply combat profile or inspect the Console for a plugin override.");
            }

            result = result with
            {
                Output = $"Global damage policy '{combatMode}' applied immediately. CS2 engine values were verified" +
                    (sharpTimerInstalled ? " and SharpTimer's damage hook was forced to the matching state" : string.Empty) +
                    ". It remains authoritative after preset and map changes."
            };
        }
        else
        {
            IReadOnlyDictionary<string, string>? expectedValues = request.ActionId switch
            {
                "enable-bhop" => BuildBhopLiveValues(true),
                "disable-bhop" => BuildBhopLiveValues(false),
                _ when respawnMode is not null => BuildRespawnLiveValues(respawnMode),
                _ when hudMode is not null => BuildHudLiveValues(hudMode),
                _ when practiceMode is not null => BuildPracticeVerificationValues(practiceMode),
                _ => null
            };
            if (expectedValues is not null)
            {
                var verification = await adapter.ExecuteConsoleCommandAsync(
                    server,
                    processes,
                    adapter.NormalizeConsoleCommand(BuildVerificationCommand(expectedValues)),
                    cancellationToken);
                var failures = FindVerificationFailures(expectedValues, verification.Output);
                if (failures.Count > 0)
                {
                    throw new InvalidOperationException(
                        $"CS2 accepted the action, but live verification failed for {string.Join(", ", failures)}. " +
                        "Refresh the live values and inspect the Console for a map or plugin override.");
                }

                var policy = hudMode is not null ? $"HUD mode '{hudMode}'"
                    : practiceMode is not null ? $"in-game practice mode '{practiceMode}'"
                    : respawnMode is not null ? $"respawn mode '{respawnMode}'"
                    : request.ActionId == "enable-bhop" ? "auto-bhop enabled" : "auto-bhop disabled";
                result = result with { Output = $"Live {policy} applied and verified." };
            }
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

    internal static string? ResolveCombatModeAction(string actionId, string currentCombatMode = "team") => actionId switch
    {
        "combat-peaceful" => "peaceful",
        "combat-team" => "team",
        "combat-ffa" => "ffa",
        "combat-enemy-on" => currentCombatMode == "ffa" ? "ffa" : "team",
        "combat-enemy-off" => "peaceful",
        "combat-team-on" => "ffa",
        "combat-team-off" => currentCombatMode == "peaceful" ? "peaceful" : "team",
        _ => null
    };

    internal static string? ResolveRespawnModeAction(string actionId) => actionId switch
    {
        "respawn-round" => "round",
        "respawn-instant" => "instant",
        _ => null
    };

    internal static string? ResolveHudModeAction(string actionId) => actionId switch
    {
        "hud-hidden" => "hidden",
        "hud-timer" => "timer",
        "hud-movement" => "movement",
        _ => null
    };

    internal static string? ResolvePracticeModeAction(string actionId) => actionId switch
    {
        "practice-disabled" => "disabled",
        "practice-ground" => "ground",
        "practice-anywhere" => "anywhere",
        _ => null
    };

    internal static IReadOnlyDictionary<string, string> BuildRespawnLiveValues(string respawnMode)
    {
        if (respawnMode is not ("round" or "instant"))
        {
            throw new ArgumentException("Respawn mode must be round or instant.", nameof(respawnMode));
        }

        var enabled = respawnMode == "instant" ? "1" : "0";
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["mp_respawn_on_death_t"] = enabled,
            ["mp_respawn_on_death_ct"] = enabled,
            ["mp_ignore_round_win_conditions"] = enabled
        };
    }

    internal static IReadOnlyDictionary<string, string> BuildBhopLiveValues(bool enabled) =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["sv_enablebunnyhopping"] = enabled ? "1" : "0",
            ["sv_autobunnyhopping"] = enabled ? "1" : "0"
        };

    internal static IReadOnlyDictionary<string, string> BuildHudLiveValues(string hudMode)
    {
        if (hudMode is not ("hidden" or "timer" or "movement"))
        {
            throw new ArgumentException("SharpTimer HUD mode must be hidden, timer or movement.", nameof(hudMode));
        }

        var timerVisible = hudMode == "hidden" ? "0" : "1";
        var movementVisible = hudMode == "movement" ? "1" : "0";
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["sharptimer_enable_timer_hud"] = timerVisible,
            ["sharptimer_enable_keys_hud"] = movementVisible,
            ["sharptimer_enable_velocity_hud"] = movementVisible,
            ["sharptimer_enable_strafesync_hud"] = movementVisible,
            ["sharptimer_enable_rankicons_hud"] = movementVisible,
            ["sharptimer_enable_map_tier_hud"] = timerVisible,
            ["sharptimer_enable_map_type_hud"] = timerVisible,
            ["sharptimer_enable_map_name_hud"] = timerVisible
        };
    }

    internal static IReadOnlyDictionary<string, string> BuildPracticeLiveValues(string practiceMode)
    {
        if (practiceMode is not ("disabled" or "ground" or "anywhere"))
        {
            throw new ArgumentException("Practice mode must be disabled, ground or anywhere.", nameof(practiceMode));
        }

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["sharptimer_checkpoints_enabled"] = practiceMode == "disabled" ? "0" : "1",
            ["sharptimer_remove_checkpoints_restrictions"] = practiceMode == "anywhere" ? "1" : "0",
            ["sharptimer_checkpoints_only_when_timer_stopped"] = "0",
            ["sharptimer_respawn_enabled"] = "1",
            ["sharptimer_top_enabled"] = "1",
            ["sharptimer_rank_enabled"] = "1",
            ["sharptimer_stage_times_enabled"] = "1",
            ["sharptimer_stage_sr_enabled"] = "1",
            ["sharptimer_connect_commands_msg_enabled"] = "1",
            ["sharptimer_replays_enabled"] = "0",
            ["sharptimer_replay_bot_enabled"] = "0",
            ["sharptimer_hud_updates_per_second"] = "16"
        };
    }

    internal static bool TryResolveReportedPracticeMode(string? output, out string practiceMode)
    {
        practiceMode = string.Empty;
        if (!TryReadConsoleVariable("sharptimer_checkpoints_enabled", output, out var checkpoints) ||
            !TryReadConsoleVariable("sharptimer_remove_checkpoints_restrictions", output, out var unrestricted))
        {
            return false;
        }

        practiceMode = !ConsoleValuesMatch("1", checkpoints)
            ? "disabled"
            : ConsoleValuesMatch("1", unrestricted)
                ? "anywhere"
                : "ground";
        return true;
    }

    private static IReadOnlyDictionary<string, string> BuildPracticeVerificationValues(string practiceMode)
    {
        var values = BuildPracticeLiveValues(practiceMode);
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["sharptimer_checkpoints_enabled"] = values["sharptimer_checkpoints_enabled"],
            ["sharptimer_remove_checkpoints_restrictions"] = values["sharptimer_remove_checkpoints_restrictions"]
        };
    }

    internal static string BuildApplyCommand(IReadOnlyDictionary<string, string> values) =>
        string.Join("; ", values.Select(pair => $"{pair.Key} {pair.Value}"));

    internal static string BuildVerificationCommand(IReadOnlyDictionary<string, string> values) =>
        string.Join("; ", values.Keys);

    internal static IReadOnlyList<string> FindVerificationFailures(
        IReadOnlyDictionary<string, string> expectedValues,
        string? output) =>
        expectedValues
            .Where(pair => !TryReadConsoleVariable(pair.Key, output, out var reported) ||
                           !ConsoleValuesMatch(pair.Value, reported))
            .Select(pair => pair.Key)
            .ToArray();

    internal static bool TryResolveReportedHudMode(string? output, out string hudMode)
    {
        hudMode = string.Empty;
        var expectedKeys = BuildHudLiveValues("movement").Keys;
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var key in expectedKeys)
        {
            if (!TryReadConsoleVariable(key, output, out var value))
            {
                return false;
            }

            values[key] = value;
        }

        var movementVisible = new[]
        {
            "sharptimer_enable_keys_hud",
            "sharptimer_enable_velocity_hud",
            "sharptimer_enable_strafesync_hud",
            "sharptimer_enable_rankicons_hud"
        }.Any(key => ConsoleValuesMatch("1", values[key]));
        var timerVisible = new[]
        {
            "sharptimer_enable_timer_hud",
            "sharptimer_enable_map_tier_hud",
            "sharptimer_enable_map_type_hud",
            "sharptimer_enable_map_name_hud"
        }.Any(key => ConsoleValuesMatch("1", values[key]));
        hudMode = movementVisible
            ? "movement"
            : timerVisible
                ? "timer"
                : "hidden";
        return true;
    }

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
            ["mp_damage_headshot_only"] = "0",
            // A map or mode may leave a respawned player protected indefinitely. Enabling a
            // combat policy must make hits effective immediately, not only after a hidden timer.
            ["mp_respawn_immunitytime"] = "0"
        };
    }

    internal static string BuildCombatApplyCommand(string combatMode, bool sharpTimerInstalled)
    {
        var commands = BuildCombatLiveValues(combatMode)
            .Select(pair => $"{pair.Key} {pair.Value}")
            .ToList();
        if (sharpTimerInstalled)
        {
            // poor-sharptimer's fake convar reliably mutates its damage hook only with literal
            // booleans. Using 1 can leave the previous value unchanged.
            commands.Add($"sharptimer_remove_damage {(combatMode == "peaceful" ? "true" : "false")}");
        }

        return string.Join("; ", commands);
    }

    internal static string BuildCombatVerificationCommand(bool sharpTimerInstalled)
    {
        _ = sharpTimerInstalled;
        // SharpTimer fake convars are commands and do not print their current state when queried.
        // Treating the echoed command as a value produced false green status. Engine ConVars are
        // queried here; the SharpTimer hook is authoritatively set on every apply/start/map change.
        var keys = BuildCombatLiveValues("team").Keys.ToList();
        return string.Join("; ", keys);
    }

    internal static string ResolveCombatModeFromValues(IReadOnlyDictionary<string, string> values)
    {
        var damageDisabled = new[]
        {
            "mp_damage_scale_ct_head",
            "mp_damage_scale_ct_body",
            "mp_damage_scale_t_head",
            "mp_damage_scale_t_body"
        }.All(key => values.TryGetValue(key, out var value) && ConsoleValuesMatch("0", value));
        if (damageDisabled)
        {
            return "peaceful";
        }

        var teamDamageEnabled = new[] { "mp_friendlyfire", "mp_teammates_are_enemies" }
            .Any(key => values.TryGetValue(key, out var value) && ConsoleValuesMatch("1", value));
        return teamDamageEnabled ? "ffa" : "team";
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
        string liveReadMessage,
        IReadOnlySet<string> liveValueKeys,
        string activeHudMode,
        bool hudLiveReadSucceeded,
        string activePracticeMode,
        bool practiceLiveReadSucceeded,
        bool sharpTimerInstalled,
        string activeCombatMode,
        bool combatLiveReadSucceeded,
        bool combatOverrideActive) => new(
            running,
            liveReadSucceeded,
            liveReadMessage,
            clock.UtcNow,
            liveValueKeys.OrderBy(key => key, StringComparer.Ordinal).ToArray(),
            store.SettingDefinitions,
            values,
            ActionDescriptors,
            store.GetGsltState(server),
            mapChanges.GetState(server.Id),
            activeHudMode,
            hudLiveReadSucceeded,
            activePracticeMode,
            practiceLiveReadSucceeded,
            sharpTimerInstalled,
            activeCombatMode,
            combatLiveReadSucceeded,
            combatOverrideActive);

    internal static IReadOnlyList<string> BuildLiveReadCommands(
        IReadOnlyList<Cs2LiveSettingDescriptor> settings,
        int maximumLength = 480)
    {
        var commands = new List<string>();
        var keys = new List<string>();
        var length = 0;
        foreach (var setting in settings)
        {
            var addedLength = setting.Key.Length + (keys.Count == 0 ? 0 : 2);
            if (keys.Count > 0 && length + addedLength > maximumLength)
            {
                commands.Add(string.Join("; ", keys));
                keys.Clear();
                length = 0;
                addedLength = setting.Key.Length;
            }

            keys.Add(setting.Key);
            length += addedLength;
        }

        if (keys.Count > 0)
        {
            commands.Add(string.Join("; ", keys));
        }

        return commands;
    }

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
            $"(?:^|[\\r\\n\\s])\\\"?{Regex.Escape(key)}\\\"?\\s*=\\s*\\\"?(?<value>[^\\\"\\s\\(\\r\\n]*)",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            return false;
        }

        value = match.Groups["value"].Value.Trim();
        return true;
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
