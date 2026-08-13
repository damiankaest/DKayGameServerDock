using DKay.GameServerDock.Application.Abstractions;
using DKay.GameServerDock.Application.Models;
using DKay.GameServerDock.Domain;
using DKay.GameServerDock.Infrastructure.Installation;

namespace DKay.GameServerDock.Infrastructure.Games;

public sealed class Cs2GameModule(
    Cs2Installer installer,
    ICs2ModeManager modes,
    Cs2RuntimeProvisioner runtime,
    Cs2RconClient rcon) : IGameModule
{
    public GameTemplateDescriptor Descriptor { get; } = new(
        "counter-strike-2",
        "Counter-Strike 2",
        "Native CS2 dedicated server installed and updated through SteamCMD.",
        "Valve",
        "CS2",
        "SteamCMD",
        27015,
        4096,
        ["UDP", "TCP"],
        GameCapability.LiveConsole | GameCapability.ConsoleInput | GameCapability.Players |
        GameCapability.CurrentMap | GameCapability.Backups | GameCapability.Files |
        GameCapability.Workshop | GameCapability.Plugins,
        [
            new("hostname", "Server name", "text", true, "DKay CS2 Server"),
            new("password", "Server password", "password", false, null, null, true),
            new("maxPlayers", "Maximum players", "number", false, "10"),
            new("initialMap", "Initial map", "select", false, "de_mirage", ["de_mirage", "de_inferno", "de_ancient", "de_nuke", "de_dust2", "de_anubis", "de_train", "de_overpass"])
        ]);

    public IGameInstaller Installer { get; } = installer;
    public IGameServerAdapter Adapter { get; } = new Cs2GameServerAdapter(rcon);

    public ServerLaunchSpec BuildLaunchSpec(GameServerInstance server)
    {
        runtime.Prepare(server);
        var settings = GameSettings.Read(server);
        var activeProfile = modes.GetActiveProfile(server);
        var executable = OperatingSystem.IsWindows()
            ? Path.Combine(server.InstallDirectory, "game", "bin", "win64", "cs2.exe")
            : Path.Combine(server.InstallDirectory, "game", "bin", "linuxsteamrt64", "cs2");

        var arguments = new List<string>
        {
                "-dedicated",
                "-console",
                "-usercon",
                "-port",
                server.Port.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
        // Keep this before +map: CS2 only opens its RCON listener and Steam identity after the
        // protected bootstrap file has loaded. The file is regenerated from .dkay on every start.
        arguments.Add("+exec");
        arguments.Add("dkay-bootstrap.cfg");
        if (!string.IsNullOrWhiteSpace(activeProfile?.WorkshopId))
        {
            // Executing the request from a generated cfg makes CS2 process it after Source2 has
            // entered its console loop. It also gives the live log an explicit, non-secret marker.
            runtime.WriteWorkshopLaunchConfiguration(server, activeProfile.WorkshopId);
            arguments.Add("+exec");
            arguments.Add("dkay-workshop-start.cfg");
        }
        else
        {
            arguments.Add("+map");
            arguments.Add(activeProfile?.MapName ?? settings.Get("initialMap", "de_mirage"));
            arguments.Add("+exec");
            arguments.Add("dkay-server.cfg");
            // Live Control values intentionally run after the selected map preset so the admin's
            // explicit runtime overrides remain authoritative across preset and game updates.
            arguments.Add("+exec");
            arguments.Add("dkay-live.cfg");
        }

        return new ServerLaunchSpec(
            executable,
            server.InstallDirectory,
            arguments,
            new Dictionary<string, string>
            {
                ["SteamAppId"] = "730",
                ["SteamGameId"] = "730"
            });
    }
}
