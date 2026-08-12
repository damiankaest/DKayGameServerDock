using DKay.GameServerDock.Application.Models;
using DKay.GameServerDock.Application.Services;

namespace DKay.GameServerDock.Tests;

public sealed class ResourceValidatorTests
{
    [Fact]
    public void Accepts_start_when_memory_and_disk_have_headroom()
    {
        var host = CreateHost(availableMemoryGb: 8, availableDiskGb: 50);

        var result = ResourceValidator.ValidateStart(host, 4096);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Rejects_start_when_host_reserve_would_be_consumed()
    {
        var host = CreateHost(availableMemoryGb: 4, availableDiskGb: 50);

        var result = ResourceValidator.ValidateStart(host, 4096);

        Assert.False(result.IsValid);
        Assert.Contains("RAM", result.Reason);
    }

    [Fact]
    public void Rejects_start_when_disk_is_almost_full()
    {
        var host = CreateHost(availableMemoryGb: 8, availableDiskGb: 1);

        var result = ResourceValidator.ValidateStart(host, 2048);

        Assert.False(result.IsValid);
        Assert.Contains("disk", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    private static HostSnapshot CreateHost(int availableMemoryGb, int availableDiskGb) =>
        new(
            "test-host",
            "Test OS",
            "X64",
            ["192.168.1.2"],
            TimeSpan.FromHours(1),
            10,
            16L * 1024 * 1024 * 1024,
            availableMemoryGb * 1024L * 1024 * 1024,
            [new DiskSnapshot("C", "C:/", 100L * 1024 * 1024 * 1024, availableDiskGb * 1024L * 1024 * 1024)]);
}

