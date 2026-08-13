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
    IReadOnlyList<Cs2LiveSettingDescriptor> Settings,
    IReadOnlyDictionary<string, string> Values,
    IReadOnlyList<Cs2QuickActionDescriptor> Actions,
    Cs2GsltState Gslt);

public sealed record ApplyCs2LiveConfigurationRequest(IReadOnlyDictionary<string, string> Values);

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
