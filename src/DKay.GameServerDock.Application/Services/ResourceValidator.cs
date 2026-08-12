using DKay.GameServerDock.Application.Models;

namespace DKay.GameServerDock.Application.Services;

public static class ResourceValidator
{
    private const long DiskSafetyMarginBytes = 2L * 1024 * 1024 * 1024;
    private const long MemorySafetyMarginBytes = 512L * 1024 * 1024;

    public static ResourceValidationResult ValidateStart(HostSnapshot host, int requestedRamMb)
    {
        var requestedBytes = requestedRamMb * 1024L * 1024L;
        if (host.AvailableMemoryBytes < requestedBytes + MemorySafetyMarginBytes)
        {
            return ResourceValidationResult.Failure(
                $"Not enough free RAM. Required: {requestedRamMb} MB plus a 512 MB host reserve.");
        }

        if (host.Disks.Count == 0 || host.Disks.All(disk => disk.AvailableBytes < DiskSafetyMarginBytes))
        {
            return ResourceValidationResult.Failure("Less than 2 GB free disk space is available.");
        }

        return ResourceValidationResult.Success();
    }
}

