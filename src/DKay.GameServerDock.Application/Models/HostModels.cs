namespace DKay.GameServerDock.Application.Models;

public sealed record DiskSnapshot(string Name, string RootPath, long TotalBytes, long AvailableBytes);

public sealed record HostSnapshot(
    string HostName,
    string OperatingSystem,
    string Architecture,
    IReadOnlyList<string> LanAddresses,
    TimeSpan Uptime,
    double CpuPercent,
    long TotalMemoryBytes,
    long AvailableMemoryBytes,
    IReadOnlyList<DiskSnapshot> Disks);

public sealed record DirectoryReadiness(
    string Path,
    bool Exists,
    bool Writable,
    string Message);

public sealed record RuntimeReadiness(
    string Id,
    string Name,
    string Purpose,
    string ConfiguredValue,
    string? ResolvedPath,
    bool Available,
    string? Version,
    string Message);

public sealed record HostReadinessSnapshot(
    bool Ready,
    DirectoryReadiness DataRoot,
    DirectoryReadiness ServersRoot,
    IReadOnlyList<RuntimeReadiness> Runtimes,
    DateTimeOffset CheckedAt);

public sealed record ResourceValidationResult(bool IsValid, string? Reason)
{
    public static ResourceValidationResult Success() => new(true, null);
    public static ResourceValidationResult Failure(string reason) => new(false, reason);
}
