namespace DKay.GameServerDock.Application.Models;

public sealed record Cs2ConVarDescriptor(
    string Key,
    string Label,
    string Type,
    string DefaultValue,
    bool Editable,
    string Description,
    decimal? Minimum = null,
    decimal? Maximum = null,
    IReadOnlyList<string>? Options = null);

public sealed record Cs2ModePresetDescriptor(
    string Id,
    string Name,
    string Category,
    string Icon,
    string Description,
    IReadOnlyList<string> MapPrefixes,
    IReadOnlyList<string> RecommendedPackageIds,
    IReadOnlyList<Cs2ConVarDescriptor> Settings);

public sealed record Cs2ManagedPackageDescriptor(
    string Id,
    string Name,
    string Kind,
    string Description,
    string Publisher,
    string ProjectUrl,
    bool AutomaticInstall,
    bool Experimental,
    IReadOnlyList<string> DependencyIds);

public sealed record ApplyCs2ModePresetRequest(
    string PresetId,
    string MapName,
    string? WorkshopId,
    int BotQuota,
    int BotDifficulty,
    bool InstallRecommendedPackages,
    IReadOnlyDictionary<string, string> Overrides);

public sealed record Cs2ModeProfile(
    string Id,
    string PresetId,
    string PresetName,
    string MapName,
    string? WorkshopId,
    int BotQuota,
    int BotDifficulty,
    IReadOnlyDictionary<string, string> Overrides,
    IReadOnlyList<string> RecommendedPackageIds,
    DateTimeOffset UpdatedAt);

public sealed record Cs2ManagedPackageState(
    string Id,
    string Name,
    string Kind,
    string Description,
    string Publisher,
    string ProjectUrl,
    bool AutomaticInstall,
    bool Experimental,
    bool Installed,
    string? InstalledVersion,
    DateTimeOffset? InstalledAt,
    IReadOnlyList<string> DependencyIds);

public sealed record Cs2ModeState(
    string? ActiveProfileId,
    IReadOnlyList<Cs2ModeProfile> Profiles,
    IReadOnlyList<Cs2ManagedPackageState> Packages);

public sealed record Cs2ModeApplyResult(
    Cs2ModeState State,
    IReadOnlyList<string> QueuedPackageIds);
