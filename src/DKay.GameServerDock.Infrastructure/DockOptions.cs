namespace DKay.GameServerDock.Infrastructure;

public sealed class DockOptions
{
    public const string SectionName = "Dock";

    public string DataRoot { get; set; } = string.Empty;
    public string ServersRoot { get; set; } = string.Empty;
    public string SteamCmdPath { get; set; } = string.Empty;
    public string JavaPath { get; set; } = "java";
    public bool PublicPortalEnabled { get; set; }
    public int PublicPortalPort { get; set; } = 5081;
    public string PublicHost { get; set; } = string.Empty;
    public string PublicPortalName { get; set; } = "DKay Game Servers";
}
