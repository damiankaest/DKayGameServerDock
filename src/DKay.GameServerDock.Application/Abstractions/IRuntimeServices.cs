using DKay.GameServerDock.Application.Models;
using DKay.GameServerDock.Domain;

namespace DKay.GameServerDock.Application.Abstractions;

public interface IProcessSupervisor
{
    Task<ProcessSnapshot> StartAsync(GameServerInstance server, ServerLaunchSpec launchSpec, CancellationToken cancellationToken);
    Task<ProcessSnapshot> StopAsync(GameServerInstance server, string gracefulCommand, bool force, CancellationToken cancellationToken);
    Task SendCommandAsync(Guid serverId, string command, CancellationToken cancellationToken);
    ProcessSnapshot GetSnapshot(Guid serverId);
}

public interface IHostMetricsProvider
{
    Task<HostSnapshot> GetSnapshotAsync(CancellationToken cancellationToken);
}

public interface IHostReadinessProvider
{
    HostReadinessSnapshot GetSnapshot();
}

public interface IPathPolicy
{
    string ResolveServerDirectory(string serverName, Guid serverId);
    string ResolveChildPath(string serverRoot, string relativePath);
    string ValidateServerDirectory(string path);
}

public interface IServerEventSink
{
    Task RecordAsync(ServerEvent serverEvent, CancellationToken cancellationToken);
    Task PublishInstallationProgressAsync(Guid serverId, InstallationProgress progress, CancellationToken cancellationToken);
    Task PublishStatusAsync(Guid serverId, ServerStatus status, CancellationToken cancellationToken);
}

public interface IServerRuntimeStateStore
{
    Task MarkExitedAsync(Guid serverId, int exitCode, CancellationToken cancellationToken);
}

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public interface IServerWorkQueue
{
    ValueTask EnqueueAsync(ServerWorkItem item, CancellationToken cancellationToken);
    ValueTask<ServerWorkItem> DequeueAsync(CancellationToken cancellationToken);
}

public enum ServerWorkKind
{
    Install,
    Update,
    InstallCs2Package
}

public sealed record ServerWorkItem(Guid ServerId, ServerWorkKind Kind, string? Argument = null);
