using DKay.GameServerDock.Application.Models;
using DKay.GameServerDock.Application.Services;

namespace DKay.GameServerDock.Tests;

public sealed class Cs2LiveControlServiceTests
{
    private static readonly Cs2LiveSettingDescriptor BotQuotaMode = new(
        "bot_quota_mode",
        "Bot quota mode",
        "Teams & bots",
        "select",
        "normal",
        "Bot population strategy.",
        Options: ["normal", "fill", "match"]);

    [Theory]
    [InlineData("fill", "fill")]
    [InlineData("Fill", "fill")]
    [InlineData("\"MATCH\"", "match")]
    public void Reported_select_values_are_canonicalized(string reported, string expected)
    {
        var valid = Cs2LiveControlService.TryNormalizeReportedValue(BotQuotaMode, reported, out var normalized);

        Assert.True(valid);
        Assert.Equal(expected, normalized);
    }

    [Fact]
    public void Unsupported_reported_select_value_is_rejected_before_reaching_the_editor()
    {
        var valid = Cs2LiveControlService.TryNormalizeReportedValue(BotQuotaMode, "competitive", out var normalized);

        Assert.False(valid);
        Assert.Empty(normalized);
    }
}
