using DKay.GameServerDock.Application.Models;
using DKay.GameServerDock.Infrastructure.Games;

namespace DKay.GameServerDock.Tests;

public sealed class Cs2ModeCatalogTests
{
    [Fact]
    public void BuildConVars_AppliesOnlyAllowedOverridesAndBotSettings()
    {
        var surf = Cs2ModeCatalog.Presets.Single(preset => preset.Id == "surf");
        var request = new ApplyCs2ModePresetRequest(
            surf.Id,
            "surf_utopia_njv",
            "3072851041",
            3,
            4,
            true,
            new Dictionary<string, string> { ["sv_airaccelerate"] = "250" });

        var result = Cs2ModeCatalog.BuildConVars(surf, request);

        Assert.Equal("250", result["sv_airaccelerate"]);
        Assert.Equal("3", result["bot_quota"]);
        Assert.Equal("4", result["bot_difficulty"]);
        Assert.Equal("0", result["sv_cheats"]);
    }

    [Fact]
    public void BuildConVars_RejectsConVarsOutsidePresetAllowlist()
    {
        var surf = Cs2ModeCatalog.Presets.Single(preset => preset.Id == "surf");
        var request = new ApplyCs2ModePresetRequest(
            surf.Id,
            "surf_safe",
            null,
            0,
            1,
            false,
            new Dictionary<string, string> { ["rcon_password"] = "stolen" });

        var exception = Assert.Throws<ArgumentException>(() => Cs2ModeCatalog.BuildConVars(surf, request));

        Assert.Contains("not allowed", exception.Message);
    }

    [Theory]
    [InlineData("../server.cfg", null)]
    [InlineData("surf_good", "not-a-workshop-id")]
    [InlineData("+quit", null)]
    public void ValidateMap_RejectsUnsafeLaunchValues(string mapName, string? workshopId)
    {
        Assert.Throws<ArgumentException>(() => Cs2ModeCatalog.ValidateMap(mapName, workshopId));
    }

    [Fact]
    public void ResolveAutomaticInstallOrder_InstallsDependenciesBeforeSharpTimer()
    {
        using var httpClient = new HttpClient();
        var manager = new Cs2ModeManager(httpClient);

        var order = manager.ResolveAutomaticInstallOrder(["sharp-timer", "movement-unlocker"]).ToList();

        Assert.True(order.IndexOf("metamod-source") < order.IndexOf("counterstrikesharp"));
        Assert.True(order.IndexOf("counterstrikesharp") < order.IndexOf("cs2-tags"));
        Assert.True(order.IndexOf("cs2-tags") < order.IndexOf("sharp-timer"));
        Assert.Contains("movement-unlocker", order);
        Assert.Equal(order.Count, order.Distinct().Count());
    }
}
