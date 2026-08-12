using System.Diagnostics;
using DKay.GameServerDock.Application.Abstractions;
using DKay.GameServerDock.Application.Models;

namespace DKay.GameServerDock.Infrastructure.Monitoring;

public sealed class HostReadinessProvider(DockOptions options) : IHostReadinessProvider
{
    public HostReadinessSnapshot GetSnapshot()
    {
        var dataRoot = ProbeDirectory(options.DataRoot);
        var serversRoot = ProbeDirectory(options.ServersRoot);
        var runtimes = new[]
        {
            ProbeRuntime("java", "Java", "Minecraft Paper", options.JavaPath),
            ProbeRuntime("steamcmd", "SteamCMD", "Counter-Strike 2", options.SteamCmdPath)
        };

        return new HostReadinessSnapshot(
            dataRoot.Writable && serversRoot.Writable,
            dataRoot,
            serversRoot,
            runtimes,
            DateTimeOffset.UtcNow);
    }

    private static DirectoryReadiness ProbeDirectory(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            var probePath = Path.Combine(path, $".dkay-write-test-{Guid.NewGuid():N}.tmp");
            try
            {
                File.WriteAllText(probePath, "DKayGameServerDock readiness probe");
            }
            finally
            {
                File.Delete(probePath);
            }

            return new DirectoryReadiness(path, true, true, "Directory exists and is writable.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new DirectoryReadiness(path, Directory.Exists(path), false, exception.Message);
        }
    }

    private static RuntimeReadiness ProbeRuntime(string id, string name, string purpose, string configuredValue)
    {
        var resolvedPath = ResolveExecutable(configuredValue);
        if (resolvedPath is null)
        {
            var message = string.IsNullOrWhiteSpace(configuredValue)
                ? $"Configure {name} before installing {purpose}."
                : $"{name} could not be resolved from '{configuredValue}'.";
            return new RuntimeReadiness(id, name, purpose, configuredValue, null, false, null, message);
        }

        string? version = null;
        try
        {
            version = FileVersionInfo.GetVersionInfo(resolvedPath).FileVersion;
        }
        catch (Exception exception) when (exception is FileNotFoundException or UnauthorizedAccessException)
        {
            // Availability is the important readiness signal; version metadata is optional.
        }

        return new RuntimeReadiness(
            id,
            name,
            purpose,
            configuredValue,
            resolvedPath,
            true,
            string.IsNullOrWhiteSpace(version) ? null : version,
            $"Ready for {purpose}.");
    }

    private static string? ResolveExecutable(string configuredValue)
    {
        if (string.IsNullOrWhiteSpace(configuredValue))
        {
            return null;
        }

        var candidate = configuredValue.Trim().Trim('"');
        if (Path.IsPathRooted(candidate) || candidate.Contains(Path.DirectorySeparatorChar) || candidate.Contains(Path.AltDirectorySeparatorChar))
        {
            try
            {
                var fullPath = Path.GetFullPath(candidate);
                return File.Exists(fullPath) ? fullPath : null;
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return null;
            }
        }

        var pathValue = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var extensions = GetExecutableExtensions(candidate);
        foreach (var directory in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            foreach (var extension in extensions)
            {
                var executable = Path.Combine(directory.Trim('"'), candidate + extension);
                if (File.Exists(executable))
                {
                    return Path.GetFullPath(executable);
                }
            }
        }

        return null;
    }

    private static IReadOnlyList<string> GetExecutableExtensions(string candidate)
    {
        if (!OperatingSystem.IsWindows() || Path.HasExtension(candidate))
        {
            return [string.Empty];
        }

        var pathExtensions = Environment.GetEnvironmentVariable("PATHEXT");
        return string.IsNullOrWhiteSpace(pathExtensions)
            ? [".exe", ".cmd", ".bat"]
            : pathExtensions.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
