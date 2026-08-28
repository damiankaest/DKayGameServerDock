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
    IReadOnlyList<Cs2ConVarDescriptor> Settings,
    string DefaultCombatMode,
    string DefaultAmmoMode,
    string DefaultHudMode,
    string DefaultRespawnMode,
    string DefaultPracticeMode);

public sealed record Cs2ManagedPackageDescriptor(
    string Id,
    string Name,
    string Kind,
    string Description,
    string Publisher,
    string ProjectUrl,
    bool AutomaticInstall,
    bool Experimental,
    IReadOnlyList<string> DependencyIds,
    string? MetamodPluginVdf = null);

public sealed record ApplyCs2ModePresetRequest(
    string PresetId,
    string MapName,
    string? WorkshopId,
    int BotQuota,
    int BotDifficulty,
    bool InstallRecommendedPackages,
    IReadOnlyDictionary<string, string> Overrides,
    string? CombatMode = null,
    string? AmmoMode = null,
    string? HudMode = null,
    string? RespawnMode = null,
    string? PracticeMode = null);

public sealed record Cs2ModeProfile(
    string Id,
    string PresetId,
    string PresetName,
    string MapName,
    string? WorkshopId,
    string? WorkshopTitle,
    string? WorkshopPreviewUrl,
    string WorkshopInstallState,
    int BotQuota,
    int BotDifficulty,
    IReadOnlyDictionary<string, string> Overrides,
    IReadOnlyList<string> RecommendedPackageIds,
    DateTimeOffset UpdatedAt,
    string? CombatMode = null,
    string? AmmoMode = null,
    string? HudMode = null,
    string? RespawnMode = null,
    string? PracticeMode = null);

public sealed record Cs2WorkshopAccessState(
    bool Configured,
    string? MaskedKey,
    bool ProtectedFromGameUpdates,
    string Message);

public sealed record Cs2WorkshopMap(
    string PublishedFileId,
    string Title,
    string MapName,
    string? PreviewUrl,
    string WorkshopUrl,
    long FileSize,
    long Subscriptions,
    DateTimeOffset? UpdatedAt,
    IReadOnlyList<string> Tags);

public sealed record Cs2WorkshopSearchResult(
    string Query,
    int Total,
    IReadOnlyList<Cs2WorkshopMap> Items);

public sealed record ConfigureCs2WorkshopKeyRequest(string Key);

public sealed record ConfigureCs2WorkshopKeyResult(
    Cs2WorkshopAccessState State,
    string Message);

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
    bool Enabled,
    string? InstalledVersion,
    DateTimeOffset? InstalledAt,
    IReadOnlyList<string> DependencyIds);

public sealed record Cs2ModeState(
    string? ActiveProfileId,
    IReadOnlyList<Cs2ModeProfile> Profiles,
    IReadOnlyList<Cs2ManagedPackageState> Packages,
    Cs2WorkshopAccessState Workshop);

public sealed record Cs2ModeApplyResult(
    Cs2ModeState State,
    IReadOnlyList<string> QueuedPackageIds);
