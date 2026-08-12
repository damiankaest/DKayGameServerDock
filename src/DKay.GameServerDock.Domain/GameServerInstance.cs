namespace DKay.GameServerDock.Domain;

public sealed class GameServerInstance
{
    private GameServerInstance()
    {
    }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string TemplateId { get; private set; } = string.Empty;
    public string InstallDirectory { get; private set; } = string.Empty;
    public string Version { get; private set; } = "latest";
    public int Port { get; private set; }
    public int? QueryPort { get; private set; }
    public int? RconPort { get; private set; }
    public int RamLimitMb { get; private set; }
    public string SettingsJson { get; private set; } = "{}";
    public bool Autostart { get; private set; }
    public bool AutoRestart { get; private set; }
    public ServerStatus Status { get; private set; }
    public int? ProcessId { get; private set; }
    public int? ExitCode { get; private set; }
    public string? LastError { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }

    public static GameServerInstance Create(
        Guid id,
        string name,
        string templateId,
        string installDirectory,
        string version,
        int port,
        int? queryPort,
        int? rconPort,
        int ramLimitMb,
        string settingsJson,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(templateId);
        ArgumentException.ThrowIfNullOrWhiteSpace(installDirectory);

        if (port is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port));
        }

        if (ramLimitMb < 512)
        {
            throw new ArgumentOutOfRangeException(nameof(ramLimitMb), "A server requires at least 512 MB RAM.");
        }

        return new GameServerInstance
        {
            Id = id == Guid.Empty ? throw new ArgumentException("Server id cannot be empty.", nameof(id)) : id,
            Name = name.Trim(),
            TemplateId = templateId,
            InstallDirectory = installDirectory,
            Version = string.IsNullOrWhiteSpace(version) ? "latest" : version.Trim(),
            Port = port,
            QueryPort = queryPort,
            RconPort = rconPort,
            RamLimitMb = ramLimitMb,
            SettingsJson = settingsJson,
            Status = ServerStatus.Installing,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    public void ChangeStatus(ServerStatus status, DateTimeOffset now, string? error = null)
    {
        Status = status;
        UpdatedAt = now;
        LastError = error;

        if (status == ServerStatus.Running)
        {
            StartedAt = now;
        }
    }

    public void TrackProcess(int? processId, int? exitCode, DateTimeOffset now)
    {
        ProcessId = processId;
        ExitCode = exitCode;
        UpdatedAt = now;
    }

    public void UpdateSettings(string name, int ramLimitMb, string settingsJson, bool autostart, bool autoRestart, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (ramLimitMb < 512)
        {
            throw new ArgumentOutOfRangeException(nameof(ramLimitMb));
        }

        Name = name.Trim();
        RamLimitMb = ramLimitMb;
        SettingsJson = settingsJson;
        Autostart = autostart;
        AutoRestart = autoRestart;
        UpdatedAt = now;
    }

    public void UpdatePublication(string settingsJson, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsJson);
        SettingsJson = settingsJson;
        UpdatedAt = now;
    }
}
