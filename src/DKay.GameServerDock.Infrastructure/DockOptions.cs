namespace DKay.GameServerDock.Infrastructure;

public sealed class DockOptions
{
    public const string SectionName = "Dock";

    public string DataRoot { get; set; } = string.Empty;
    public string ServersRoot { get; set; } = string.Empty;
    public string SteamCmdPath { get; set; } = string.Empty;
    public string JavaPath { get; set; } = "java";
}

