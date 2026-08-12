using DKay.GameServerDock.Application.Abstractions;
using DKay.GameServerDock.Application.Models;
using DKay.GameServerDock.Domain;
using DKay.GameServerDock.Infrastructure.Installation;

namespace DKay.GameServerDock.Infrastructure.Games;

public sealed class PaperGameModule(PaperInstaller installer, DockOptions options) : IGameModule
{
    public GameTemplateDescriptor Descriptor { get; } = new(
        "minecraft-paper",
        "Minecraft Paper",
        "Fast, plugin-ready Minecraft Java server with automatic stable builds.",
        "Minecraft",
        "MC",
        "Java download",
        25565,
        4096,
        ["TCP"],
        GameCapability.LiveConsole | GameCapability.ConsoleInput | GameCapability.Players |
        GameCapability.Backups | GameCapability.Files | GameCapability.Plugins | GameCapability.Whitelist,
        [
            new("acceptEula", "Accept Minecraft EULA", "boolean", true, "false"),
            new("motd", "Message of the day", "text", false, "A DKay server"),
            new("maxPlayers", "Maximum players", "number", false, "10"),
            new("gamemode", "Game mode", "select", false, "survival", ["survival", "creative", "adventure", "spectator"]),
            new("difficulty", "Difficulty", "select", false, "normal", ["peaceful", "easy", "normal", "hard"]),
            new("pvp", "PvP", "boolean", false, "true")
        ]);

    public IGameInstaller Installer { get; } = installer;
    public IGameServerAdapter Adapter { get; } = new BasicGameServerAdapter("stop", "world");

    public ServerLaunchSpec BuildLaunchSpec(GameServerInstance server) => new(
        options.JavaPath,
        server.InstallDirectory,
        ["-Xms512M", $"-Xmx{server.RamLimitMb}M", "-jar", "paper.jar", "--nogui"],
        new Dictionary<string, string>());
}
