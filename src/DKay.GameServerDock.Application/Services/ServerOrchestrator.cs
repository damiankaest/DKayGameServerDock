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
        await events.RecordAsync(
            ServerEvent.Create(server.Id, ServerEventType.ServerUpdateStarted, "Server update started.", clock.UtcNow),
            cancellationToken);

        try
        {
            await module.Installer.UpdateAsync(
                server,
                ReportProgressAsync,
                cancellationToken);
            await TransitionAsync(server, ServerStatus.Stopped, cancellationToken);
            await events.RecordAsync(
                ServerEvent.Create(server.Id, ServerEventType.ServerUpdated, "Server update completed.", clock.UtcNow),
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await TransitionAsync(server, ServerStatus.Error, cancellationToken, exception.Message);
            await events.RecordAsync(
                ServerEvent.Create(server.Id, ServerEventType.ServerUpdateFailed, exception.Message, clock.UtcNow),
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

    public async Task<ProcessSnapshot> StartAsync(Guid serverId, CancellationToken cancellationToken)
    {
        var server = await GetRequiredAsync(serverId, cancellationToken);
        ServerStateMachine.EnsureCanTransition(server.Status, ServerStatus.Starting);
        await events.RecordAsync(
            ServerEvent.Create(
                server.Id,
                ServerEventType.ServerStartRequested,
                $"Start requested for {server.TemplateId} on port {server.Port} with a {server.RamLimitMb} MB memory limit.",
                clock.UtcNow),
            cancellationToken);

        var host = await hostMetrics.GetSnapshotAsync(cancellationToken);
        var validation = ResourceValidator.ValidateStart(host, server.RamLimitMb);
        if (!validation.IsValid)
        {
            await events.RecordAsync(
                ServerEvent.Create(server.Id, ServerEventType.ServerStartProgress, $"Resource validation failed: {validation.Reason}", clock.UtcNow),
                cancellationToken);
            throw new InvalidOperationException(validation.Reason);
        }

        await events.RecordAsync(
            ServerEvent.Create(
                server.Id,
                ServerEventType.ServerStartProgress,
                $"Resource validation passed. Host has {host.AvailableMemoryBytes / 1024 / 1024} MB available memory.",
                clock.UtcNow),
            cancellationToken);

        await TransitionAsync(server, ServerStatus.Starting, cancellationToken);

        try
        {
            var module = modules.GetRequired(server.TemplateId);
            var launchSpec = module.BuildLaunchSpec(server);
            await events.RecordAsync(
                ServerEvent.Create(
                    server.Id,
                    ServerEventType.ServerStartProgress,
                    $"Launching {Path.GetFileName(launchSpec.FileName)} from '{launchSpec.WorkingDirectory}'.",
                    clock.UtcNow),
                cancellationToken);
            var snapshot = await processes.StartAsync(server, launchSpec, cancellationToken);
            server.TrackProcess(snapshot.ProcessId, snapshot.ExitCode, clock.UtcNow);
            await TransitionAsync(server, ServerStatus.Running, cancellationToken);
            await events.RecordAsync(
                ServerEvent.Create(server.Id, ServerEventType.ServerStarted, $"Server started with process ID {snapshot.ProcessId}.", clock.UtcNow),
                cancellationToken);

            var reapplyCommand = module.Adapter.PolicyReapplyCommand;
            if (!string.IsNullOrWhiteSpace(reapplyCommand))
            {
                _ = ReapplyPolicyAfterWarmupAsync(server, module.Adapter, reapplyCommand, processes, events, clock);
            }

            return snapshot;
        }
        catch (Exception exception)
        {
            await TransitionAsync(server, ServerStatus.Error, cancellationToken, exception.Message);
            await events.RecordAsync(
                ServerEvent.Create(server.Id, ServerEventType.ServerStartProgress, $"Start failed: {exception.Message}", clock.UtcNow),
                cancellationToken);
            throw;
        }
    }

    private static async Task ReapplyPolicyAfterWarmupAsync(
        GameServerInstance server,
        IGameServerAdapter adapter,
        string command,
        IProcessSupervisor processes,
        IServerEventSink events,
        IClock clock)
    {
        try
        {
            // Metamod and CounterStrikeSharp plugins load during map load and apply their own
            // ConVars after the launch +exec chain. Wait for them to settle, then re-assert the
            // administrator's saved policy so it outranks plugin defaults.
            await Task.Delay(TimeSpan.FromSeconds(20));
            await adapter.ExecuteConsoleCommandAsync(server, processes, command, CancellationToken.None);
            await events.RecordAsync(
                ServerEvent.Create(
                    server.Id,
                    ServerEventType.ConfigurationChanged,
                    "Reapplied the saved policy after plugin warmup.",
                    clock.UtcNow),
                CancellationToken.None);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Best effort. The launch +exec chain already applied the values once, and the
            // administrator can reapply manually from Live control.
        }
    }

    public async Task DeleteAsync(Guid serverId, bool deleteFiles, CancellationToken cancellationToken)
    {
        var server = await GetRequiredAsync(serverId, cancellationToken);
        if (server.Status is not (ServerStatus.Stopped or ServerStatus.Crashed or ServerStatus.Error))
        {
            throw new InvalidOperationException($"Stop the server before deleting it (current state: {server.Status}).");
        }

        if (processes.GetSnapshot(server.Id).IsRunning)
        {
            throw new InvalidOperationException("The server process is still running. Stop it before deleting the instance.");
        }

        if (deleteFiles)
        {
            var directory = paths.ValidateServerDirectory(server.InstallDirectory);
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        await servers.DeleteAsync(server, cancellationToken);
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
        var gracefulCommand = module.Adapter.GracefulStopCommand;
        if (!force && module.Adapter.HandlesCommandsExternally)
        {
            try
            {
                await module.Adapter.ExecuteConsoleCommandAsync(server, processes, gracefulCommand, cancellationToken);
                gracefulCommand = string.Empty;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                await events.RecordAsync(
                    ServerEvent.Create(
                        server.Id,
                        ServerEventType.ServerStartProgress,
                        $"Graceful command channel failed; the process will be stopped after the safety timeout: {exception.Message}",
                        clock.UtcNow),
                    cancellationToken);
                gracefulCommand = string.Empty;
            }
        }

        var snapshot = await processes.StopAsync(server, gracefulCommand, force, cancellationToken);
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

    public async Task<ConsoleCommandResult> SendCommandAsync(
        Guid serverId,
        string command,
        CancellationToken cancellationToken)
    {
        var server = await GetRequiredAsync(serverId, cancellationToken);
        if (server.Status != ServerStatus.Running)
        {
            throw new InvalidOperationException("Console commands can only be sent to a running server.");
        }

        var adapter = modules.GetRequired(server.TemplateId).Adapter;
        var normalized = adapter.NormalizeConsoleCommand(command);
        return await adapter.ExecuteConsoleCommandAsync(server, processes, normalized, cancellationToken);
    }

    public async Task<ServerSelfTestResult> TestCommandChannelAsync(
        Guid serverId,
        CancellationToken cancellationToken)
    {
        var server = await GetRequiredAsync(serverId, cancellationToken);
        var snapshot = processes.GetSnapshot(serverId);
        if (server.Status != ServerStatus.Running || !snapshot.IsRunning)
        {
            throw new InvalidOperationException("Start the server before running its self-test.");
        }

        var adapter = modules.GetRequired(server.TemplateId).Adapter;
        var marker = $"DKAY_COMMAND_PROBE_{Guid.NewGuid():N}";
        var result = await adapter.ExecuteConsoleCommandAsync(
            server,
            processes,
            adapter.NormalizeConsoleCommand($"echo {marker}"),
            cancellationToken);
        var passed = result.Output?.Contains(marker, StringComparison.Ordinal) == true;
        var message = passed
            ? "Process, local game port and administrator command channel responded successfully."
            : "The process is running, but the command channel did not return the expected acknowledgement.";

        return new ServerSelfTestResult(
            passed,
            result.Transport,
            server.Port,
            snapshot.ProcessId,
            message,
            result.Output,
            clock.UtcNow);
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
            ServerPublicationSettings.MergeGameSettings(server, request.Settings),
            request.Autostart,
            request.AutoRestart,
            clock.UtcNow);
        await servers.SaveAsync(server, cancellationToken);
        await events.RecordAsync(
            ServerEvent.Create(server.Id, ServerEventType.ConfigurationChanged, "Server settings updated.", clock.UtcNow),
            cancellationToken);
    }

    public async Task<ServerPublicationState> UpdatePublicationAsync(
        Guid serverId,
        UpdateServerPublicationRequest request,
        CancellationToken cancellationToken)
    {
        var server = await GetRequiredAsync(serverId, cancellationToken);
        server.UpdatePublication(ServerPublicationSettings.Apply(server, request), clock.UtcNow);
        await servers.SaveAsync(server, cancellationToken);

        var publication = ServerPublicationSettings.Read(server);
        await events.RecordAsync(
            ServerEvent.Create(
                server.Id,
                ServerEventType.ConfigurationChanged,
                publication.Published
                    ? $"Server published for guest access on port {publication.PublicPort}."
                    : "Server removed from guest access.",
                clock.UtcNow),
            cancellationToken);
        return publication;
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
