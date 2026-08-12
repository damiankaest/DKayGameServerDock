using System.Text.Json;
using DKay.GameServerDock.Application.Abstractions;
using DKay.GameServerDock.Application.Models;
using DKay.GameServerDock.Domain;

namespace DKay.GameServerDock.Application.Services;

public sealed class ServerOrchestrator(
    IServerRepository servers,
    IGameModuleRegistry modules,
    IPathPolicy paths,
    IProcessSupervisor processes,
    IHostMetricsProvider hostMetrics,
    IServerEventSink events,
    IClock clock)
{
    public async Task<GameServerInstance> CreateAsync(CreateServerRequest request, CancellationToken cancellationToken)
    {
        var module = modules.GetRequired(request.TemplateId);
        ValidateSettings(module.Descriptor, request.Settings);

        if (await servers.IsPortAllocatedAsync(request.Port, cancellationToken))
        {
            throw new InvalidOperationException($"Port {request.Port} is already allocated to another server.");
        }

        var id = Guid.NewGuid();
        var directory = paths.ResolveServerDirectory(request.Name, id);
        var server = GameServerInstance.Create(
            id,
            request.Name,
            request.TemplateId,
            directory,
            request.Version,
            request.Port,
            request.QueryPort,
            request.RconPort,
            request.RamLimitMb,
            JsonSerializer.Serialize(request.Settings),
            clock.UtcNow);

        await servers.AddAsync(server, cancellationToken);
        await events.RecordAsync(
            ServerEvent.Create(server.Id, ServerEventType.InstallationStarted, "Server installation queued.", clock.UtcNow),
            cancellationToken);
        return server;
    }

    public async Task InstallAsync(Guid serverId, CancellationToken cancellationToken)
    {
        var server = await GetRequiredAsync(serverId, cancellationToken);
        var module = modules.GetRequired(server.TemplateId);

        try
        {
            Directory.CreateDirectory(server.InstallDirectory);
            await module.Installer.InstallAsync(server, ReportProgressAsync, cancellationToken);
            await TransitionAsync(server, ServerStatus.Stopped, cancellationToken);
            await events.RecordAsync(
                ServerEvent.Create(server.Id, ServerEventType.InstallationCompleted, "Server installation completed.", clock.UtcNow),
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await TransitionAsync(server, ServerStatus.Error, cancellationToken, exception.Message);
            await events.RecordAsync(
                ServerEvent.Create(server.Id, ServerEventType.InstallationFailed, exception.Message, clock.UtcNow),
                cancellationToken);
        }

        return;

        async Task ReportProgressAsync(InstallationProgress progress, CancellationToken token)
        {
            await events.RecordAsync(
                ServerEvent.Create(
                    server.Id,
                    ServerEventType.InstallationProgress,
                    progress.Message,
                    clock.UtcNow,
                    JsonSerializer.Serialize(progress)),
                token);
            await events.PublishInstallationProgressAsync(server.Id, progress, token);
        }
    }

    public async Task UpdateAsync(Guid serverId, CancellationToken cancellationToken)
    {
        var server = await GetRequiredAsync(serverId, cancellationToken);
        await TransitionAsync(server, ServerStatus.Updating, cancellationToken);
        var module = modules.GetRequired(server.TemplateId);

        try
        {
            await module.Installer.UpdateAsync(
                server,
                (progress, token) => events.PublishInstallationProgressAsync(server.Id, progress, token),
                cancellationToken);
            await TransitionAsync(server, ServerStatus.Stopped, cancellationToken);
            await events.RecordAsync(
                ServerEvent.Create(server.Id, ServerEventType.ServerUpdated, "Server update completed.", clock.UtcNow),
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await TransitionAsync(server, ServerStatus.Error, cancellationToken, exception.Message);
        }
    }

    public async Task<ProcessSnapshot> StartAsync(Guid serverId, CancellationToken cancellationToken)
    {
        var server = await GetRequiredAsync(serverId, cancellationToken);
        ServerStateMachine.EnsureCanTransition(server.Status, ServerStatus.Starting);

        var host = await hostMetrics.GetSnapshotAsync(cancellationToken);
        var validation = ResourceValidator.ValidateStart(host, server.RamLimitMb);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException(validation.Reason);
        }

        await TransitionAsync(server, ServerStatus.Starting, cancellationToken);

        try
        {
            var module = modules.GetRequired(server.TemplateId);
            var snapshot = await processes.StartAsync(server, module.BuildLaunchSpec(server), cancellationToken);
            server.TrackProcess(snapshot.ProcessId, snapshot.ExitCode, clock.UtcNow);
            await TransitionAsync(server, ServerStatus.Running, cancellationToken);
            await events.RecordAsync(
                ServerEvent.Create(server.Id, ServerEventType.ServerStarted, "Server started.", clock.UtcNow),
                cancellationToken);
            return snapshot;
        }
        catch
        {
            await TransitionAsync(server, ServerStatus.Error, cancellationToken, "The server process could not be started.");
            throw;
        }
    }

    public async Task<ProcessSnapshot> StopAsync(Guid serverId, bool force, CancellationToken cancellationToken)
    {
        var server = await GetRequiredAsync(serverId, cancellationToken);
        if (server.Status is not (ServerStatus.Running or ServerStatus.Starting or ServerStatus.Crashed))
        {
            throw new InvalidOperationException($"Server cannot be stopped while it is {server.Status}.");
        }

        if (server.Status != ServerStatus.Crashed)
        {
            await TransitionAsync(server, ServerStatus.Stopping, cancellationToken);
        }

        var module = modules.GetRequired(server.TemplateId);
        var snapshot = await processes.StopAsync(server, module.Adapter.GracefulStopCommand, force, cancellationToken);
        server.TrackProcess(null, snapshot.ExitCode, clock.UtcNow);
        await TransitionAsync(server, ServerStatus.Stopped, cancellationToken);
        await events.RecordAsync(
            ServerEvent.Create(server.Id, ServerEventType.ServerStopped, force ? "Server force-stopped." : "Server stopped.", clock.UtcNow),
            cancellationToken);
        return snapshot;
    }

    public async Task<ProcessSnapshot> RestartAsync(Guid serverId, CancellationToken cancellationToken)
    {
        var server = await GetRequiredAsync(serverId, cancellationToken);
        if (server.Status == ServerStatus.Running)
        {
            await StopAsync(serverId, false, cancellationToken);
        }

        return await StartAsync(serverId, cancellationToken);
    }

    public async Task SendCommandAsync(Guid serverId, string command, CancellationToken cancellationToken)
    {
        var server = await GetRequiredAsync(serverId, cancellationToken);
        if (server.Status != ServerStatus.Running)
        {
            throw new InvalidOperationException("Console commands can only be sent to a running server.");
        }

        var normalized = modules.GetRequired(server.TemplateId).Adapter.NormalizeConsoleCommand(command);
        await processes.SendCommandAsync(serverId, normalized, cancellationToken);
    }

    public async Task<ServerRuntimeStatus> GetRuntimeStatusAsync(Guid serverId, CancellationToken cancellationToken)
    {
        var server = await GetRequiredAsync(serverId, cancellationToken);
        var module = modules.GetRequired(server.TemplateId);
        var snapshot = processes.GetSnapshot(serverId);
        var players = snapshot.IsRunning
            ? await module.Adapter.GetPlayersAsync(server, cancellationToken)
            : [];
        var currentMap = snapshot.IsRunning
            ? await module.Adapter.GetCurrentMapAsync(server, cancellationToken)
            : null;

        return new ServerRuntimeStatus(server, snapshot, players, currentMap);
    }

    public async Task UpdateSettingsAsync(Guid serverId, UpdateServerRequest request, CancellationToken cancellationToken)
    {
        var server = await GetRequiredAsync(serverId, cancellationToken);
        if (server.Status is not (ServerStatus.Stopped or ServerStatus.Crashed or ServerStatus.Error))
        {
            throw new InvalidOperationException("Stop the server before changing its settings.");
        }

        ValidateSettings(modules.GetRequired(server.TemplateId).Descriptor, request.Settings);
        server.UpdateSettings(
            request.Name,
            request.RamLimitMb,
            JsonSerializer.Serialize(request.Settings),
            request.Autostart,
            request.AutoRestart,
            clock.UtcNow);
        await servers.SaveAsync(server, cancellationToken);
        await events.RecordAsync(
            ServerEvent.Create(server.Id, ServerEventType.ConfigurationChanged, "Server settings updated.", clock.UtcNow),
            cancellationToken);
    }

    private async Task<GameServerInstance> GetRequiredAsync(Guid id, CancellationToken cancellationToken) =>
        await servers.FindAsync(id, cancellationToken)
        ?? throw new KeyNotFoundException($"Server '{id}' was not found.");

    private async Task TransitionAsync(
        GameServerInstance server,
        ServerStatus target,
        CancellationToken cancellationToken,
        string? error = null)
    {
        ServerStateMachine.EnsureCanTransition(server.Status, target);
        server.ChangeStatus(target, clock.UtcNow, error);
        await servers.SaveAsync(server, cancellationToken);
        await events.PublishStatusAsync(server.Id, target, cancellationToken);
    }

    private static void ValidateSettings(
        GameTemplateDescriptor descriptor,
        IReadOnlyDictionary<string, string> settings)
    {
        foreach (var definition in descriptor.Settings.Where(setting => setting.Required))
        {
            if (!settings.TryGetValue(definition.Key, out var value) || string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException($"Setting '{definition.Label}' is required.");
            }
        }
    }
}
