using DKay.GameServerDock.Domain;

namespace DKay.GameServerDock.Application.Models;

public sealed record CreateServerRequest(
    string Name,
    string TemplateId,
    string Version,
    int Port,
    int? QueryPort,
    int? RconPort,
    int RamLimitMb,
    IReadOnlyDictionary<string, string> Settings);

public sealed record UpdateServerRequest(
    string Name,
    int RamLimitMb,
    bool Autostart,
    bool AutoRestart,
    IReadOnlyDictionary<string, string> Settings);

public sealed record UpdateServerPublicationRequest(bool Published, int? PublicPort);

public sealed record ServerPublicationState(bool Published, int PublicPort);

public sealed record ServerLaunchSpec(
    string FileName,
    string WorkingDirectory,
    IReadOnlyList<string> Arguments,
    IReadOnlyDictionary<string, string> Environment);

public sealed record ProcessSnapshot(
    bool IsRunning,
    int? ProcessId,
    int? ExitCode,
    DateTimeOffset? StartedAt,
    TimeSpan? Uptime,
    double CpuPercent,
    long MemoryBytes);

public sealed record InstallationProgress(int Percent, string Stage, string Message);

public sealed record ProcessOutputLine(DateTimeOffset Timestamp, string Stream, string Text);

public sealed record ConsoleCommandResult(string Transport, string? Output);

public sealed record ServerSelfTestResult(
    bool Passed,
    string Transport,
    int Port,
    int? ProcessId,
    string Message,
    string? Output,
    DateTimeOffset CheckedAt);

public sealed record PlayerInfo(string Name, string Id, int? Ping, TimeSpan? ConnectionTime);

public sealed record ServerRuntimeStatus(
    GameServerInstance Server,
    ProcessSnapshot Process,
    IReadOnlyList<PlayerInfo> Players,
    string? CurrentMap);
