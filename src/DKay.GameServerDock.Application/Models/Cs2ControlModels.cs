namespace DKay.GameServerDock.Application.Models;

public sealed record Cs2LiveSettingDescriptor(
    string Key,
    string Label,
    string Group,
    string Type,
    string DefaultValue,
    string Description,
    decimal? Minimum = null,
    decimal? Maximum = null,
    decimal? Step = null,
    IReadOnlyList<string>? Options = null);

public sealed record Cs2QuickActionDescriptor(
    string Id,
    string Label,
    string Description,
    string Group,
    string Icon,
    string Tone = "default",
    bool RequiresPlugin = false);

public sealed record Cs2GsltState(
    bool Configured,
    string? MaskedToken,
    bool ProtectedFromGameUpdates,
    string Message);

public sealed record Cs2LiveControlState(
    bool Running,
    bool LiveReadSucceeded,
    string LiveReadMessage,
    DateTimeOffset ObservedAt,
    IReadOnlyList<string> LiveValueKeys,
    IReadOnlyList<Cs2LiveSettingDescriptor> Settings,
    IReadOnlyDictionary<string, string> Values,
    IReadOnlyList<Cs2QuickActionDescriptor> Actions,
    Cs2GsltState Gslt,
    Cs2MapChangeState MapChange,
    string ActiveHudMode,
    bool HudLiveReadSucceeded,
    string ActivePracticeMode,
    bool PracticeLiveReadSucceeded,
    bool SharpTimerInstalled,
    string ActiveCombatMode,
    bool CombatLiveReadSucceeded,
    bool CombatOverrideActive);

public sealed record Cs2MapChangeState(
    string Status,
    string? ProfileId,
    string? MapName,
    string? WorkshopId,
    DateTimeOffset? ExecuteAt,
    int RemainingSeconds,
    string Message);

public sealed record ScheduleCs2MapChangeRequest(string ProfileId, int DelaySeconds);

public sealed record ScheduleCs2MapByMapRequest(
    string PresetId,
    string MapName,
    string? WorkshopId,
    int DelaySeconds);

public sealed record Cs2LoadedPlugin(
    string Id,
    string Name,
    string Loader,
    string Status,
    string? Version,
    string? Author);

public sealed record Cs2PluginState(
    IReadOnlyList<Cs2LoadedPlugin> Plugins,
    IReadOnlyList<string> InstalledCssPlugins,
    bool LiveReadSucceeded,
    string? Message);

public sealed record RunCs2PluginActionRequest(
    string Action,
    string Name);

public sealed record ApplyCs2LiveConfigurationRequest(
    IReadOnlyDictionary<string, string> Values,
    IReadOnlyList<string>? ChangedKeys = null);

public sealed record Cs2LiveConfigurationApplyResult(
    IReadOnlyDictionary<string, string> Values,
    bool AppliedLive,
    string Message,
    string? Output);

public sealed record RunCs2QuickActionRequest(string ActionId, string? Value = null);

public sealed record ConfigureCs2GsltRequest(string Token);

public sealed record ConfigureCs2GsltResult(
    Cs2GsltState State,
    bool AppliedLive,
    string Message,
    string? Output);

public sealed record Cs2BasicConfiguration(
    bool AutoBhop,
    int Gravity,
    int BotQuota);

public sealed record SaveCs2BasicConfigurationRequest(
    bool AutoBhop,
    int Gravity,
    int BotQuota);

public sealed record Cs2BasicConfigurationState(
    Cs2BasicConfiguration Configuration,
    bool Running,
    bool AppliedLive,
    string Message,
    IReadOnlyDictionary<string, string> ObservedValues,
    string? Output);

public static class Cs2RuntimePolicy
{
    /// <summary>
    /// Console command that re-applies the administrator's authoritative runtime layers
    /// (global combat/respawn policy, saved live values and the small Basic Control layer)
    /// without restarting the round.
    /// Run it after plugins have finished loading so it outranks plugin defaults.
    /// </summary>
    public const string ReapplyCommand = "exec dkay-combat.cfg; exec dkay-live.cfg; exec dkay-basic.cfg";
}
