using DKay.GameServerDock.Application.Models;
using DKay.GameServerDock.Domain;

namespace DKay.GameServerDock.Application.Abstractions;

public interface ICs2ModeManager
{
    IReadOnlyList<Cs2ModePresetDescriptor> Presets { get; }
    IReadOnlyList<Cs2ManagedPackageDescriptor> Packages { get; }
    Cs2ModeProfile? GetActiveProfile(GameServerInstance server);

    Task<Cs2ModeState> GetStateAsync(GameServerInstance server, CancellationToken cancellationToken);
    Task<Cs2ModeState> ApplyPresetAsync(
        GameServerInstance server,
        ApplyCs2ModePresetRequest request,
        CancellationToken cancellationToken);
    Task<Cs2ModeProfile> ActivateProfileAsync(
        GameServerInstance server,
        string profileId,
        CancellationToken cancellationToken);
    Task<Cs2ModeProfile> SetActiveCombatModeAsync(
        GameServerInstance server,
        string combatMode,
        CancellationToken cancellationToken);
    Task<Cs2ModeProfile> SetActiveRespawnModeAsync(
        GameServerInstance server,
        string respawnMode,
        CancellationToken cancellationToken);
    Task<Cs2ModeProfile> SetActiveHudModeAsync(
        GameServerInstance server,
        string hudMode,
        CancellationToken cancellationToken);
    Task<Cs2ModeProfile> SetActivePracticeModeAsync(
        GameServerInstance server,
        string practiceMode,
        CancellationToken cancellationToken);
    Cs2WorkshopAccessState GetWorkshopAccessState(GameServerInstance server);
    Cs2WorkshopAccessState SaveWorkshopApiKey(GameServerInstance server, string key);

    /// <summary>
    /// Lists map files already present in the server's <c>game/csgo/maps</c> directory, optionally
    /// filtered by a free-text query, with the preset suggested for each map name.
    /// </summary>
    Cs2LocalMapSearchResult SearchLocalMaps(GameServerInstance server, string query, int take);

    Task<Cs2WorkshopSearchResult> SearchWorkshopMapsAsync(
        GameServerInstance server,
        string query,
        int take,
        CancellationToken cancellationToken);
    Task RepairAfterGameUpdateAsync(GameServerInstance server, CancellationToken cancellationToken);

    /// <summary>
    /// Aligns the on-disk Metamod autoload state with the active map profile. An installed Metamod
    /// plugin is enabled exactly when the active profile still recommends it, so a profile switch
    /// or server restart cannot resurrect plugins from a previously applied preset. Idempotent.
    /// </summary>
    void ReconcileEnabledPlugins(GameServerInstance server);

    IReadOnlyList<string> ResolveAutomaticInstallOrder(IEnumerable<string> packageIds);
    Task InstallPackageAsync(
        GameServerInstance server,
        string packageId,
        Func<InstallationProgress, CancellationToken, Task> reportProgress,
        CancellationToken cancellationToken);
}
