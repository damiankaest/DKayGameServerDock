namespace DKay.GameServerDock.Domain;

[Flags]
public enum GameCapability
{
    None = 0,
    LiveConsole = 1 << 0,
    ConsoleInput = 1 << 1,
    Players = 1 << 2,
    CurrentMap = 1 << 3,
    Backups = 1 << 4,
    Files = 1 << 5,
    Workshop = 1 << 6,
    Plugins = 1 << 7,
    Whitelist = 1 << 8
}

