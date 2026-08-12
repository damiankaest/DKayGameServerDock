using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using DKay.GameServerDock.Application.Abstractions;
using DKay.GameServerDock.Application.Models;

namespace DKay.GameServerDock.Infrastructure.Monitoring;

public sealed class HostMetricsProvider : IHostMetricsProvider
{
    public async Task<HostSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        var memory = ReadMemory();
        var cpu = await ReadCpuPercentAsync(cancellationToken);
        var addresses = NetworkInterface.GetAllNetworkInterfaces()
            .Where(network => network.OperationalStatus == OperationalStatus.Up &&
                              network.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(network => network.GetIPProperties().UnicastAddresses)
            .Select(address => address.Address)
            .Where(address => address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address))
            .Select(address => address.ToString())
            .Distinct()
            .Order()
            .ToArray();

        var disks = DriveInfo.GetDrives()
            .Where(drive => drive.IsReady && drive.DriveType is DriveType.Fixed or DriveType.Network)
            .Select(drive => new DiskSnapshot(drive.Name, drive.RootDirectory.FullName, drive.TotalSize, drive.AvailableFreeSpace))
            .ToArray();

        return new HostSnapshot(
            Environment.MachineName,
            RuntimeInformation.OSDescription,
            RuntimeInformation.OSArchitecture.ToString(),
            addresses,
            TimeSpan.FromMilliseconds(Environment.TickCount64),
            cpu,
            memory.Total,
            memory.Available,
            disks);
    }

    private static (long Total, long Available) ReadMemory()
    {
        if (OperatingSystem.IsWindows())
        {
            return ReadWindowsMemory();
        }

        if (OperatingSystem.IsLinux() && File.Exists("/proc/meminfo"))
        {
            var values = File.ReadLines("/proc/meminfo")
                .Select(line => line.Split(':', 2, StringSplitOptions.TrimEntries))
                .Where(parts => parts.Length == 2)
                .ToDictionary(
                    parts => parts[0],
                    parts => long.TryParse(parts[1].Split(' ', StringSplitOptions.RemoveEmptyEntries)[0], out var value)
                        ? value * 1024L
                        : 0L,
                    StringComparer.OrdinalIgnoreCase);
            return (values.GetValueOrDefault("MemTotal"), values.GetValueOrDefault("MemAvailable"));
        }

        var fallbackTotal = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        return (fallbackTotal, Math.Max(0, fallbackTotal - Environment.WorkingSet));
    }

    private static async Task<double> ReadCpuPercentAsync(CancellationToken cancellationToken)
    {
        var first = ReadCpuCounters();
        await Task.Delay(150, cancellationToken);
        var second = ReadCpuCounters();
        var totalDelta = second.Total - first.Total;
        var idleDelta = second.Idle - first.Idle;
        return totalDelta <= 0 ? 0 : Math.Clamp((totalDelta - idleDelta) * 100d / totalDelta, 0, 100);
    }

    private static (ulong Idle, ulong Total) ReadCpuCounters()
    {
        if (OperatingSystem.IsWindows())
        {
            return ReadWindowsCpuCounters();
        }

        if (OperatingSystem.IsLinux() && File.Exists("/proc/stat"))
        {
            var line = File.ReadLines("/proc/stat").First(value => value.StartsWith("cpu ", StringComparison.Ordinal));
            var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1).Select(ulong.Parse).ToArray();
            var idle = fields.ElementAtOrDefault(3) + fields.ElementAtOrDefault(4);
            return (idle, fields.Aggregate(0UL, (total, value) => total + value));
        }

        return (0, 0);
    }

    [SupportedOSPlatform("windows")]
    private static (long Total, long Available) ReadWindowsMemory()
    {
        var status = new MemoryStatusEx();
        if (!GlobalMemoryStatusEx(status))
        {
            return (0, 0);
        }

        return ((long)status.TotalPhysical, (long)status.AvailablePhysical);
    }

    [SupportedOSPlatform("windows")]
    private static (ulong Idle, ulong Total) ReadWindowsCpuCounters()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user))
        {
            return (0, 0);
        }

        var idleValue = idle.ToUInt64();
        return (idleValue, kernel.ToUInt64() + user.ToUInt64());
    }

    #pragma warning disable SYSLIB1054 // The Windows host implementation uses classic P/Invoke for .NET 10 compatibility.
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MemoryStatusEx buffer);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out NativeFileTime idle, out NativeFileTime kernel, out NativeFileTime user);
    #pragma warning restore SYSLIB1054

    #pragma warning disable CS0649 // Values are populated by the native Windows API.
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private sealed class MemoryStatusEx
    {
        public uint Length = (uint)Marshal.SizeOf<MemoryStatusEx>();
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativeFileTime
    {
        private readonly uint _low;
        private readonly uint _high;

        public ulong ToUInt64() => ((ulong)_high << 32) | _low;
    }
    #pragma warning restore CS0649
}
