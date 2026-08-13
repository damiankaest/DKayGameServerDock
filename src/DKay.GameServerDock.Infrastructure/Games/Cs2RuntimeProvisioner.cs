using System.Security.Cryptography;
using DKay.GameServerDock.Domain;

namespace DKay.GameServerDock.Infrastructure.Games;

public sealed class Cs2RuntimeProvisioner(DockOptions options)
{
    private static readonly string[] WindowsSteamRuntimeFiles =
    [
        "steamclient64.dll",
        "tier0_s64.dll",
        "vstdlib_s64.dll"
    ];

    public void Prepare(GameServerInstance server)
    {
        ArgumentNullException.ThrowIfNull(server);
        Directory.CreateDirectory(server.InstallDirectory);

        if (OperatingSystem.IsWindows())
        {
            CopyWindowsSteamRuntime(options.SteamCmdPath, server.InstallDirectory);
        }

        File.WriteAllText(Path.Combine(server.InstallDirectory, "steam_appid.txt"), "730\n");
        WriteRconConfiguration(server, GetOrCreateRconPassword(server));
        EnsureRconAutoexec(server);
    }

    public string GetRconPassword(GameServerInstance server)
    {
        var secretPath = GetRconSecretPath(server);
        if (!File.Exists(secretPath))
        {
            throw new InvalidOperationException(
                "The CS2 command-channel secret is missing. Stop the server, run Update server once and start it again.");
        }

        var password = File.ReadAllText(secretPath).Trim();
        if (string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException("The CS2 command-channel secret is empty.");
        }

        return password;
    }

    public static void CopyWindowsSteamRuntime(string steamCmdPath, string serverInstallDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(steamCmdPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(serverInstallDirectory);

        var steamCmdDirectory = Path.GetDirectoryName(Path.GetFullPath(steamCmdPath));
        if (string.IsNullOrWhiteSpace(steamCmdDirectory))
        {
            throw new InvalidOperationException($"SteamCMD has no parent directory: '{steamCmdPath}'.");
        }

        var destination = Path.Combine(serverInstallDirectory, "game", "bin", "win64");
        if (!Directory.Exists(destination))
        {
            throw new InvalidOperationException(
                $"The CS2 Windows runtime directory is missing at '{destination}'. Run Update server to repair the installation.");
        }

        var missingFiles = WindowsSteamRuntimeFiles
            .Where(file => !File.Exists(Path.Combine(steamCmdDirectory, file)))
            .ToArray();
        if (missingFiles.Length > 0)
        {
            throw new InvalidOperationException(
                $"SteamCMD is missing its Windows server runtime ({string.Join(", ", missingFiles)}). Run '{steamCmdPath} +quit' once and retry.");
        }

        foreach (var file in WindowsSteamRuntimeFiles)
        {
            File.Copy(Path.Combine(steamCmdDirectory, file), Path.Combine(destination, file), overwrite: true);
        }
    }

    private static string GetOrCreateRconPassword(GameServerInstance server)
    {
        var secretPath = GetRconSecretPath(server);
        if (File.Exists(secretPath))
        {
            var existing = File.ReadAllText(secretPath).Trim();
            if (!string.IsNullOrWhiteSpace(existing))
            {
                return existing;
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(secretPath)!);
        var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        File.WriteAllText(secretPath, password + Environment.NewLine);
        return password;
    }

    private static void WriteRconConfiguration(GameServerInstance server, string password)
    {
        var configDirectory = Path.Combine(server.InstallDirectory, "game", "csgo", "cfg");
        Directory.CreateDirectory(configDirectory);
        File.WriteAllText(
            Path.Combine(configDirectory, "dkay-rcon.cfg"),
            $"// Managed by DKay Game Server Dock. Do not share this file.{Environment.NewLine}rcon_password \"{password}\"{Environment.NewLine}");
    }

    private static void EnsureRconAutoexec(GameServerInstance server)
    {
        const string directive = "exec dkay-rcon.cfg";
        var configDirectory = Path.Combine(server.InstallDirectory, "game", "csgo", "cfg");
        Directory.CreateDirectory(configDirectory);
        var autoexecPath = Path.Combine(configDirectory, "autoexec.cfg");
        if (File.Exists(autoexecPath) && File.ReadLines(autoexecPath).Any(line =>
                line.Trim().Equals(directive, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var separator = File.Exists(autoexecPath) && new FileInfo(autoexecPath).Length > 0
            ? Environment.NewLine
            : string.Empty;
        File.AppendAllText(
            autoexecPath,
            $"{separator}// Load the private local administrator command channel before the first map.{Environment.NewLine}{directive}{Environment.NewLine}");
    }

    private static string GetRconSecretPath(GameServerInstance server) =>
        Path.Combine(server.InstallDirectory, ".dkay", "rcon-password");
}
