using DKay.GameServerDock.Application.Abstractions;
using DKay.GameServerDock.Application.Models;
using DKay.GameServerDock.Domain;

namespace DKay.GameServerDock.Application.Services;

public sealed class Cs2ModeService(
    IServerRepository servers,
    ICs2ModeManager modes,
    IServerEventSink events,
    IClock clock)
{
    public IReadOnlyList<Cs2ModePresetDescriptor> Presets => modes.Presets;
    public IReadOnlyList<Cs2ManagedPackageDescriptor> Packages => modes.Packages;
    public IReadOnlyList<string> ResolveAutomaticInstallOrder(IEnumerable<string> packageIds) =>
        modes.ResolveAutomaticInstallOrder(packageIds);

    public async Task<Cs2ModeState> GetStateAsync(Guid serverId, CancellationToken cancellationToken)
    {
        var server = await GetCs2ServerAsync(serverId, cancellationToken);
        return await modes.GetStateAsync(server, cancellationToken);
    }

    public async Task<Cs2ModeApplyResult> ApplyPresetAsync(
        Guid serverId,
        ApplyCs2ModePresetRequest request,
        CancellationToken cancellationToken)
    {
        var server = await GetCs2ServerAsync(serverId, cancellationToken);
        EnsureStopped(server, "Stop the server before applying a map preset.");

        var state = await modes.ApplyPresetAsync(server, request, cancellationToken);
        var active = state.Profiles.Single(profile => profile.Id == state.ActiveProfileId);
        await events.RecordAsync(
            ServerEvent.Create(
                server.Id,
                ServerEventType.ModePresetApplied,
                $"Applied {active.PresetName} preset to {active.MapName}.",
                clock.UtcNow),
            cancellationToken);

        var queuedPackages = request.InstallRecommendedPackages
            ? modes.ResolveAutomaticInstallOrder(active.RecommendedPackageIds)
            : [];
        return new Cs2ModeApplyResult(state, queuedPackages);
    }

    public async Task InstallPackageAsync(Guid serverId, string packageId, CancellationToken cancellationToken)
    {
        var server = await GetCs2ServerAsync(serverId, cancellationToken);
        EnsureStopped(server, "Stop the server before installing or updating mods.");
        ServerStateMachine.EnsureCanTransition(server.Status, ServerStatus.Updating);
        server.ChangeStatus(ServerStatus.Updating, clock.UtcNow);
        await servers.SaveAsync(server, cancellationToken);
        await events.PublishStatusAsync(server.Id, ServerStatus.Updating, cancellationToken);

        try
        {
            await modes.InstallPackageAsync(server, packageId, ReportProgressAsync, cancellationToken);
            server.ChangeStatus(ServerStatus.Stopped, clock.UtcNow);
            await servers.SaveAsync(server, cancellationToken);
            await events.PublishStatusAsync(server.Id, ServerStatus.Stopped, cancellationToken);
            await events.RecordAsync(
                ServerEvent.Create(server.Id, ServerEventType.PluginInstalled, $"Installed managed package '{packageId}'.", clock.UtcNow),
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Managed mods are optional. Keep the base game startable when one package fails.
            server.ChangeStatus(ServerStatus.Stopped, clock.UtcNow, exception.Message);
            await servers.SaveAsync(server, cancellationToken);
            await events.PublishStatusAsync(server.Id, ServerStatus.Error, cancellationToken);
            await events.RecordAsync(
                ServerEvent.Create(server.Id, ServerEventType.PluginInstallFailed, exception.Message, clock.UtcNow),
                cancellationToken);
            throw;
        }

        return;

        async Task ReportProgressAsync(InstallationProgress progress, CancellationToken token)
        {
            await events.RecordAsync(
                ServerEvent.Create(server.Id, ServerEventType.InstallationProgress, progress.Message, clock.UtcNow),
                token);
            await events.PublishInstallationProgressAsync(server.Id, progress, token);
        }
    }

    private async Task<GameServerInstance> GetCs2ServerAsync(Guid serverId, CancellationToken cancellationToken)
    {
        var server = await servers.FindAsync(serverId, cancellationToken)
            ?? throw new KeyNotFoundException($"Server '{serverId}' was not found.");
        if (!string.Equals(server.TemplateId, "counter-strike-2", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Mode presets are only available for Counter-Strike 2 servers.");
        }

        return server;
    }

    private static void EnsureStopped(GameServerInstance server, string message)
    {
        if (server.Status is not (ServerStatus.Stopped or ServerStatus.Crashed or ServerStatus.Error))
        {
            throw new InvalidOperationException(message);
        }
    }
}
