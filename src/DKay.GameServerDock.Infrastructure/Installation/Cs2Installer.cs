using DKay.GameServerDock.Application.Abstractions;
using DKay.GameServerDock.Application.Models;
using DKay.GameServerDock.Domain;
using DKay.GameServerDock.Infrastructure.Games;

namespace DKay.GameServerDock.Infrastructure.Installation;

public sealed class Cs2Installer(DockOptions options) : IGameInstaller
{
    private readonly SteamCmdInstaller _steam = new(options, 730);

    public async Task InstallAsync(
        GameServerInstance server,
        Func<InstallationProgress, CancellationToken, Task> reportProgress,
        CancellationToken cancellationToken)
    {
        await _steam.InstallAsync(server, reportProgress, cancellationToken);
        await WriteServerConfigAsync(server, cancellationToken);
    }

    public async Task UpdateAsync(
        GameServerInstance server,
        Func<InstallationProgress, CancellationToken, Task> reportProgress,
        CancellationToken cancellationToken)
    {
        await _steam.UpdateAsync(server, reportProgress, cancellationToken);
        await WriteServerConfigAsync(server, cancellationToken);
    }

    private static async Task WriteServerConfigAsync(GameServerInstance server, CancellationToken cancellationToken)
    {
        var settings = GameSettings.Read(server);
        var configDirectory = Path.Combine(server.InstallDirectory, "game", "csgo", "cfg");
        Directory.CreateDirectory(configDirectory);
        var hostname = Escape(settings.SafeConfigValue("hostname", server.Name));
        var password = Escape(settings.SafeConfigValue("password"));
        var maxPlayers = int.TryParse(settings.Get("maxPlayers", "10"), out var parsed) ? Math.Clamp(parsed, 1, 64) : 10;
        var lines = new[]
        {
            $"hostname \"{hostname}\"",
            $"sv_password \"{password}\"",
            $"sv_visiblemaxplayers {maxPlayers}",
            "sv_lan 0",
            "log on"
        };
        await File.WriteAllLinesAsync(Path.Combine(configDirectory, "dkay-server.cfg"), lines, cancellationToken);
    }

    private static string Escape(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
}

