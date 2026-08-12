namespace DKay.GameServerDock.Domain;

public enum ServerEventType
{
    InstallationStarted,
    InstallationProgress,
    InstallationCompleted,
    InstallationFailed,
    ServerStartRequested,
    ServerStartProgress,
    ServerStarted,
    ServerStopped,
    ServerCrashed,
    ServerUpdated,
    ServerUpdateStarted,
    ServerUpdateFailed,
    ConfigurationChanged,
    PlayerConnected,
    PlayerDisconnected,
    MapChanged,
    ModePresetApplied,
    PluginInstalled,
    PluginInstallFailed,
    ConsoleOutput
}

public sealed class ServerEvent
{
    private ServerEvent()
    {
    }

    public long Id { get; private set; }
    public Guid ServerId { get; private set; }
    public ServerEventType Type { get; private set; }
    public string Message { get; private set; } = string.Empty;
    public string? DataJson { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }

    public static ServerEvent Create(Guid serverId, ServerEventType type, string message, DateTimeOffset now, string? dataJson = null) =>
        new()
        {
            ServerId = serverId,
            Type = type,
            Message = message,
            DataJson = dataJson,
            OccurredAt = now
        };
}
