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

public sealed record ResourceValidationResult(bool IsValid, string? Reason)
{
    public static ResourceValidationResult Success() => new(true, null);
    public static ResourceValidationResult Failure(string reason) => new(false, reason);
}

