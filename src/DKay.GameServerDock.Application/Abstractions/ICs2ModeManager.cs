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
    Cs2WorkshopAccessState GetWorkshopAccessState(GameServerInstance server);
    Cs2WorkshopAccessState SaveWorkshopApiKey(GameServerInstance server, string key);
    Task<Cs2WorkshopSearchResult> SearchWorkshopMapsAsync(
        GameServerInstance server,
        string query,
        int take,
        CancellationToken cancellationToken);
    Task RepairAfterGameUpdateAsync(GameServerInstance server, CancellationToken cancellationToken);
    IReadOnlyList<string> ResolveAutomaticInstallOrder(IEnumerable<string> packageIds);
    Task InstallPackageAsync(
        GameServerInstance server,
        string packageId,
        Func<InstallationProgress, CancellationToken, Task> reportProgress,
        CancellationToken cancellationToken);
}
