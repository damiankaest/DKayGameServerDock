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

    [Fact]
    public void Live_setting_reads_are_batched_without_exceeding_the_console_command_limit()
    {
        var settings = Enumerable.Range(1, 40)
            .Select(index => new Cs2LiveSettingDescriptor(
                $"setting_{index:D2}_with_a_reasonably_long_name",
                $"Setting {index}",
                "Test",
                "integer",
                "0",
                "Test setting."))
            .ToArray();

        var commands = Cs2LiveControlService.BuildLiveReadCommands(settings);

        Assert.True(commands.Count > 1);
        Assert.All(commands, command => Assert.InRange(command.Length, 1, 480));
        Assert.All(settings, setting => Assert.Contains(setting.Key, string.Join("; ", commands)));
    }

    [Fact]
    public void Bot_changes_recreate_bots_before_loading_the_live_configuration()
    {
        var command = Cs2LiveControlService.BuildLiveApplyCommand(
            new HashSet<string>(["bot_difficulty"], StringComparer.Ordinal));

        Assert.Equal("bot_kick; exec dkay-live.cfg", command);
    }

    [Fact]
    public void Movement_changes_apply_without_disrupting_current_bots()
    {
        var command = Cs2LiveControlService.BuildLiveApplyCommand(
            new HashSet<string>(["sv_autobunnyhopping"], StringComparer.Ordinal));

        Assert.Equal("exec dkay-live.cfg", command);
    }

    [Theory]
    [InlineData("combat-peaceful", "peaceful", "0", "0", "0")]
    [InlineData("combat-team", "team", "0", "0", "1")]
    [InlineData("combat-ffa", "ffa", "1", "1", "1")]
    public void Combat_actions_map_to_persisted_live_damage_rules(
        string actionId,
        string expectedMode,
        string friendlyFire,
        string teammatesAreEnemies,
        string damageScale)
    {
        var mode = Cs2LiveControlService.ResolveCombatModeAction(actionId);
        var values = Cs2LiveControlService.BuildCombatLiveValues(mode!);

        Assert.Equal(expectedMode, mode);
        Assert.Equal(friendlyFire, values["mp_friendlyfire"]);
        Assert.Equal(teammatesAreEnemies, values["mp_teammates_are_enemies"]);
        Assert.Equal(damageScale, values["mp_damage_scale_ct_body"]);
        Assert.Equal(damageScale, values["mp_damage_scale_t_head"]);
        Assert.Equal("0", values["mp_damage_headshot_only"]);
    }

    [Fact]
    public void Live_combat_command_sets_engine_and_sharptimer_values_directly_without_restarting()
    {
        var command = Cs2LiveControlService.BuildCombatApplyCommand("team", sharpTimerInstalled: true);

        Assert.Contains("mp_friendlyfire 0", command);
        Assert.Contains("mp_teammates_are_enemies 0", command);
        Assert.Contains("mp_damage_scale_t_body 1", command);
        Assert.Contains("sharptimer_remove_damage 0", command);
        Assert.DoesNotContain("exec ", command);
        Assert.DoesNotContain("mp_restartgame", command);
    }

    [Fact]
    public void Live_combat_verification_accepts_boolean_and_decimal_console_formats()
    {
        const string output = """
            "mp_friendlyfire" = "false"
            "mp_teammates_are_enemies" = "0"
            "mp_damage_scale_ct_head" = "1.000000"
            "mp_damage_scale_ct_body" = "1"
            "mp_damage_scale_t_head" = "1.0"
            "mp_damage_scale_t_body" = "1"
            "mp_damage_headshot_only" = "false"
            "sharptimer_remove_damage" = "false"
            """;

        var failures = Cs2LiveControlService.FindCombatVerificationFailures(
            Cs2LiveControlService.BuildCombatLiveValues("team"),
            "0",
            output);

        Assert.Empty(failures);
    }

    [Fact]
    public void Live_combat_verification_reports_values_overridden_by_a_plugin()
    {
        const string output = """
            "mp_friendlyfire" = "0"
            "mp_teammates_are_enemies" = "0"
            "mp_damage_scale_ct_head" = "1"
            "mp_damage_scale_ct_body" = "1"
            "mp_damage_scale_t_head" = "1"
            "mp_damage_scale_t_body" = "1"
            "mp_damage_headshot_only" = "0"
            "sharptimer_remove_damage" = "true"
            """;

        var failures = Cs2LiveControlService.FindCombatVerificationFailures(
            Cs2LiveControlService.BuildCombatLiveValues("team"),
            "0",
            output);

        Assert.Equal("sharptimer_remove_damage", Assert.Single(failures));
    }

    [Theory]
    [InlineData("round", "0")]
    [InlineData("instant", "1")]
    public void Respawn_actions_keep_both_teams_and_round_rules_consistent(string mode, string expected)
    {
        var values = Cs2LiveControlService.BuildRespawnLiveValues(mode);

        Assert.Equal(expected, values["mp_respawn_on_death_t"]);
        Assert.Equal(expected, values["mp_respawn_on_death_ct"]);
        Assert.Equal(expected, values["mp_ignore_round_win_conditions"]);
    }

    [Theory]
    [InlineData("0", "0", "0", "hidden")]
    [InlineData("1", "0", "0", "timer")]
    [InlineData("1", "1", "1", "movement")]
    public void Sharptimer_hud_status_is_derived_from_reported_plugin_values(
        string timer,
        string keys,
        string velocity,
        string expected)
    {
        var output = $"""
            "sharptimer_enable_timer_hud" = "{timer}"
            "sharptimer_enable_keys_hud" = "{keys}"
            "sharptimer_enable_velocity_hud" = "{velocity}"
            "sharptimer_enable_strafesync_hud" = "{velocity}"
            "sharptimer_enable_rankicons_hud" = "{velocity}"
            "sharptimer_enable_map_tier_hud" = "{timer}"
            "sharptimer_enable_map_type_hud" = "{timer}"
            "sharptimer_enable_map_name_hud" = "{timer}"
            """;

        var resolved = Cs2LiveControlService.TryResolveReportedHudMode(output, out var hudMode);

        Assert.True(resolved);
        Assert.Equal(expected, hudMode);
    }
}
