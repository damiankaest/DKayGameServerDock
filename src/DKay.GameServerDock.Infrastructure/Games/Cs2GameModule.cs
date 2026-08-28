using DKay.GameServerDock.Application.Abstractions;
using DKay.GameServerDock.Application.Models;
using DKay.GameServerDock.Domain;
using DKay.GameServerDock.Infrastructure.Installation;

namespace DKay.GameServerDock.Infrastructure.Games;

public sealed class Cs2GameModule(
    Cs2Installer installer,
    ICs2ModeManager modes,
    Cs2RuntimeProvisioner runtime,
    ICs2BasicConfigStore basicConfig,
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
        basicConfig.Prepare(server);
        // Enforce the active profile's Metamod plugin set before every start so a restart cannot
        // resurrect plugins left behind by a previously applied preset.
        modes.ReconcileEnabledPlugins(server);
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
            // A stock bootstrap map initializes Steam, networking and RCON before the UGC request.
            // If Steam rejects the Workshop item, the process remains diagnosable instead of idle.
            arguments.Add("+map");
            arguments.Add(settings.Get("initialMap", "de_mirage"));
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
            // Execute the global combat layer explicitly as well as through newly generated
            // dkay-server.cfg files. This keeps existing installations authoritative immediately
            // after a Hub-only update, before the administrator runs another SteamCMD update.
            arguments.Add("+exec");
            arguments.Add("dkay-combat.cfg");
            // Live Control values intentionally run last so the admin's explicit runtime
            // overrides remain authoritative across preset and game updates.
            arguments.Add("+exec");
            arguments.Add("dkay-live.cfg");
        }

        // The deliberately small Basic Control layer is loaded last. It currently owns only
        // auto-bhop, gravity and bot quota, so these values remain easy to reason about.
        arguments.Add("+exec");
        arguments.Add("dkay-basic.cfg");

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
