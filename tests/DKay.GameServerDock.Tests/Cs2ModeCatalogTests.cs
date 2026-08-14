using System.IO.Compression;
using System.Net;
using System.Text;
using DKay.GameServerDock.Application.Models;
using DKay.GameServerDock.Domain;
using DKay.GameServerDock.Infrastructure;
using DKay.GameServerDock.Infrastructure.Games;

namespace DKay.GameServerDock.Tests;

public sealed class Cs2ModeCatalogTests
{
    [Theory]
    [InlineData("disabled", "0", "0")]
    [InlineData("ground", "1", "0")]
    [InlineData("anywhere", "1", "1")]
    public void BuildSharpTimerPracticeCommands_keeps_practice_inside_the_game(
        string practiceMode,
        string checkpointsEnabled,
        string unrestricted)
    {
        var commands = Cs2ModeCatalog.BuildSharpTimerPracticeCommands(practiceMode);

        Assert.Equal(checkpointsEnabled, commands["sharptimer_checkpoints_enabled"]);
        Assert.Equal(unrestricted, commands["sharptimer_remove_checkpoints_restrictions"]);
        Assert.Equal("1", commands["sharptimer_top_enabled"]);
        Assert.Equal("1", commands["sharptimer_rank_enabled"]);
        Assert.Equal("0", commands["sharptimer_replays_enabled"]);
        Assert.Equal("0", commands["sharptimer_replay_bot_enabled"]);
        Assert.Equal("16", commands["sharptimer_hud_updates_per_second"]);
    }

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

    [Fact]
    public void RpgArena_UsesTeamCombatWithoutRespawnImmunity()
    {
        var preset = Cs2ModeCatalog.Presets.Single(item => item.Id == "rpg-arena");
        var request = new ApplyCs2ModePresetRequest(
            preset.Id,
            "fy_pool_day",
            null,
            0,
            1,
            false,
            new Dictionary<string, string>());

        var result = Cs2ModeCatalog.BuildConVars(preset, request);

        Assert.Equal("0", result["mp_friendlyfire"]);
        Assert.Equal("0", result["mp_teammates_are_enemies"]);
        Assert.Equal("0", result["mp_respawn_immunitytime"]);
        Assert.Equal("1", result["mp_damage_scale_ct_head"]);
        Assert.Equal("1", result["mp_damage_scale_ct_body"]);
        Assert.Equal("1", result["mp_damage_scale_t_head"]);
        Assert.Equal("1", result["mp_damage_scale_t_body"]);
        Assert.Equal("0", result["mp_damage_headshot_only"]);
        Assert.Equal("any", result["bot_join_team"]);
        Assert.Equal("1", result["mp_ignore_round_win_conditions"]);
        Assert.Equal("1", result["mp_autoteambalance"]);
        Assert.Equal("1", result["mp_limitteams"]);
        Assert.Equal("0", result["sv_infinite_ammo"]);
    }

    [Theory]
    [InlineData("bhop", "team", "0", "0", "1")]
    [InlineData("bhop", "ffa", "1", "1", "1")]
    [InlineData("rpg-arena", "peaceful", "0", "0", "0")]
    public void Combat_policy_is_configurable_independently_from_preset(
        string presetId,
        string combatMode,
        string friendlyFire,
        string teammatesAreEnemies,
        string damageScale)
    {
        var preset = Cs2ModeCatalog.Presets.Single(item => item.Id == presetId);
        var result = Cs2ModeCatalog.BuildConVars(
            preset,
            new ApplyCs2ModePresetRequest(
                preset.Id,
                presetId == "bhop" ? "bhop_test" : "fy_pool_day",
                null,
                0,
                1,
                false,
                new Dictionary<string, string>(),
                combatMode,
                "standard"));

        Assert.Equal(friendlyFire, result["mp_friendlyfire"]);
        Assert.Equal(teammatesAreEnemies, result["mp_teammates_are_enemies"]);
        Assert.Equal(damageScale, result["mp_damage_scale_ct_body"]);
        Assert.Equal(damageScale, result["mp_damage_scale_t_head"]);
        Assert.Equal("0", result["sv_infinite_ammo"]);
    }

    [Theory]
    [InlineData("standard", "0", "false")]
    [InlineData("infinite-reserve", "2", "false")]
    [InlineData("infinite-magazine", "1", "true")]
    public void Ammo_policy_controls_cs2_and_sharptimer_independently(
        string ammoMode,
        string infiniteAmmo,
        string sharpTimerInfiniteAmmo)
    {
        var preset = Cs2ModeCatalog.Presets.Single(item => item.Id == "bhop");
        var result = Cs2ModeCatalog.BuildConVars(
            preset,
            new ApplyCs2ModePresetRequest(
                preset.Id,
                "bhop_test",
                null,
                0,
                1,
                false,
                new Dictionary<string, string>(),
                "team",
                ammoMode));
        var sharpTimer = Cs2ModeCatalog.BuildSharpTimerCombatCommands("team", ammoMode);

        Assert.Equal(infiniteAmmo, result["sv_infinite_ammo"]);
        Assert.Equal(sharpTimerInfiniteAmmo, sharpTimer["sharptimer_apply_infinite_ammo"]);
        Assert.Equal("false", sharpTimer["sharptimer_remove_damage"]);
    }

    [Theory]
    [InlineData("hidden", "0", "0")]
    [InlineData("timer", "1", "0")]
    [InlineData("movement", "1", "1")]
    public void Sharptimer_hud_is_configurable_per_profile(
        string hudMode,
        string timerVisible,
        string movementDetailsVisible)
    {
        var commands = Cs2ModeCatalog.BuildSharpTimerHudCommands(hudMode);

        Assert.Equal(timerVisible, commands["sharptimer_enable_timer_hud"]);
        Assert.Equal(movementDetailsVisible, commands["sharptimer_enable_velocity_hud"]);
        Assert.Equal(movementDetailsVisible, commands["sharptimer_enable_strafesync_hud"]);
        Assert.Equal(movementDetailsVisible, commands["sharptimer_enable_keys_hud"]);
    }

    [Theory]
    [InlineData("round", "0")]
    [InlineData("instant", "1")]
    public void Respawn_policy_overrides_historic_preset_values(string respawnMode, string expected)
    {
        var preset = Cs2ModeCatalog.Presets.Single(item => item.Id == "rpg-arena");
        var result = Cs2ModeCatalog.BuildConVars(
            preset,
            new ApplyCs2ModePresetRequest(
                preset.Id,
                "fy_pool_day",
                null,
                0,
                1,
                false,
                new Dictionary<string, string>(),
                "team",
                "standard",
                "hidden",
                respawnMode));

        Assert.Equal(expected, result["mp_respawn_on_death_t"]);
        Assert.Equal(expected, result["mp_respawn_on_death_ct"]);
        Assert.Equal(expected, result["mp_ignore_round_win_conditions"]);
    }

    [Fact]
    public async Task Active_combat_policy_is_reapplied_after_sharptimer_without_overwriting_custom_config()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dkay-sharptimer-combat-{Guid.NewGuid():N}");
        var server = CreateServer(root);
        var markerRoot = Path.Combine(root, "game", "csgo", "addons", ".dkay");
        var sharpTimerRoot = Path.Combine(root, "game", "csgo", "cfg", "SharpTimer");
        var mapExecRoot = Path.Combine(sharpTimerRoot, "MapData", "MapExecs");
        Directory.CreateDirectory(markerRoot);
        Directory.CreateDirectory(mapExecRoot);
        await File.WriteAllTextAsync(
            Path.Combine(markerRoot, "sharp-timer.json"),
            "{\"installed\":true,\"version\":\"test\",\"installedAt\":\"2026-01-01T00:00:00Z\"}");
        var customExecPath = Path.Combine(sharpTimerRoot, "custom_exec.cfg");
        await File.WriteAllTextAsync(customExecPath, "// administrator setting\nsv_staminamax 0\n");
        var mapExecPath = Path.Combine(mapExecRoot, "example.bhop_.cfg");
        await File.WriteAllTextAsync(mapExecPath, "// upstream movement settings\nsv_airaccelerate 1000\nsharptimer_remove_damage true\n");

        try
        {
            using var httpClient = new HttpClient();
            var manager = new Cs2ModeManager(httpClient);
            var request = new ApplyCs2ModePresetRequest(
                "bhop",
                "bhop_test",
                null,
                0,
                1,
                false,
                new Dictionary<string, string>(),
                "team",
                "standard");

            await manager.ApplyPresetAsync(server, request, CancellationToken.None);
            await manager.ApplyPresetAsync(server, request, CancellationToken.None);

            var combatConfig = await File.ReadAllTextAsync(Path.Combine(root, "game", "csgo", "cfg", "dkay-combat.cfg"));
            Assert.Contains("mp_damage_scale_t_body 1", combatConfig);
            Assert.Contains("sv_infinite_ammo 0", combatConfig);
            Assert.Contains("sharptimer_remove_damage false", combatConfig);
            Assert.Contains("sharptimer_apply_infinite_ammo false", combatConfig);
            Assert.Contains("sharptimer_enable_velocity_hud 1", combatConfig);

            var customExec = await File.ReadAllTextAsync(customExecPath);
            Assert.Contains("sv_staminamax 0", customExec);
            Assert.Equal(1, customExec.Split("exec dkay-combat.cfg", StringSplitOptions.None).Length - 1);
            Assert.Equal(1, customExec.Split("exec dkay-live.cfg", StringSplitOptions.None).Length - 1);

            var mapExec = await File.ReadAllTextAsync(mapExecPath);
            Assert.Contains("sv_airaccelerate 1000", mapExec);
            Assert.EndsWith("exec dkay-combat.cfg\nexec dkay-live.cfg\n", mapExec.Replace("\r\n", "\n", StringComparison.Ordinal));
            Assert.Equal(1, mapExec.Split("exec dkay-combat.cfg", StringSplitOptions.None).Length - 1);
            Assert.Equal(1, mapExec.Split("exec dkay-live.cfg", StringSplitOptions.None).Length - 1);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Active_combat_mode_can_be_changed_and_persisted_while_the_server_is_running()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dkay-live-combat-{Guid.NewGuid():N}");
        var server = CreateServer(root);
        try
        {
            using var httpClient = new HttpClient();
            var manager = new Cs2ModeManager(httpClient);
            await manager.ApplyPresetAsync(
                server,
                new ApplyCs2ModePresetRequest(
                    "rpg-arena",
                    "fy_pool_day",
                    null,
                    0,
                    1,
                    false,
                    new Dictionary<string, string>(),
                    "team",
                    "standard",
                    "hidden"),
                CancellationToken.None);

            var profile = await manager.SetActiveCombatModeAsync(server, "ffa", CancellationToken.None);
            var reloaded = await manager.GetStateAsync(server, CancellationToken.None);

            Assert.Equal("ffa", profile.CombatMode);
            Assert.Equal("ffa", Assert.Single(reloaded.Profiles).CombatMode);
            var profileConfig = await File.ReadAllTextAsync(Path.Combine(root, "game", "csgo", "cfg", "dkay", "maps", "fy_pool_day.cfg"));
            var combatConfig = await File.ReadAllTextAsync(Path.Combine(root, "game", "csgo", "cfg", "dkay-combat.cfg"));
            Assert.Contains("mp_friendlyfire 1", profileConfig);
            Assert.Contains("mp_teammates_are_enemies 1", profileConfig);
            Assert.Contains("mp_damage_scale_t_body 1", combatConfig);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Respawn_and_hud_policies_can_be_changed_and_persisted_live()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dkay-live-policy-{Guid.NewGuid():N}");
        var server = CreateServer(root);
        try
        {
            using var httpClient = new HttpClient();
            var manager = new Cs2ModeManager(httpClient);
            await manager.ApplyPresetAsync(
                server,
                new ApplyCs2ModePresetRequest(
                    "rpg-arena",
                    "fy_pool_day",
                    null,
                    0,
                    1,
                    false,
                    new Dictionary<string, string>()),
                CancellationToken.None);

            await manager.SetActiveRespawnModeAsync(server, "round", CancellationToken.None);
            await manager.SetActiveHudModeAsync(server, "timer", CancellationToken.None);
            var profile = Assert.Single((await manager.GetStateAsync(server, CancellationToken.None)).Profiles);

            Assert.Equal("round", profile.RespawnMode);
            Assert.Equal("timer", profile.HudMode);
            var profileConfig = await File.ReadAllTextAsync(Path.Combine(root, "game", "csgo", "cfg", "dkay", "maps", "fy_pool_day.cfg"));
            Assert.Contains("mp_respawn_on_death_t 0", profileConfig);
            Assert.Contains("mp_ignore_round_win_conditions 0", profileConfig);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("surf")]
    [InlineData("kz")]
    [InlineData("bhop")]
    public void Movement_presets_prevent_player_blocking_and_round_interruptions(string presetId)
    {
        var preset = Cs2ModeCatalog.Presets.Single(item => item.Id == presetId);
        var result = Cs2ModeCatalog.BuildConVars(
            preset,
            new ApplyCs2ModePresetRequest(preset.Id, $"{preset.Id}_test", null, 0, 1, false, new Dictionary<string, string>()));

        Assert.Equal("0", result["mp_solid_teammates"]);
        Assert.Equal("0", result["mp_friendlyfire"]);
        Assert.Equal("1", result["mp_respawn_on_death_t"]);
        Assert.Equal("1", result["mp_respawn_on_death_ct"]);
        Assert.Equal("1", result["mp_ignore_round_win_conditions"]);
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

    [Fact]
    public async Task InstallPackage_DeploysFlatCs2TagsArchiveIntoCounterStrikeSharpPluginDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dkay-cs2-tags-tests-{Guid.NewGuid():N}");
        var csgoRoot = Path.Combine(root, "game", "csgo");
        var markerRoot = Path.Combine(csgoRoot, "addons", ".dkay");
        Directory.CreateDirectory(markerRoot);
        await File.WriteAllTextAsync(
            Path.Combine(markerRoot, "counterstrikesharp.json"),
            "{\"installed\":true,\"version\":\"test\",\"installedAt\":\"2026-01-01T00:00:00Z\"}");
        var server = GameServerInstance.Create(
            Guid.NewGuid(),
            "CS2 test",
            "counter-strike-2",
            root,
            "latest",
            27015,
            null,
            null,
            4096,
            "{}",
            DateTimeOffset.UtcNow);

        try
        {
            using var httpClient = new HttpClient(new Cs2TagsReleaseHandler(CreateCs2TagsArchive()));
            var manager = new Cs2ModeManager(httpClient);

            await manager.InstallPackageAsync(
                server,
                "cs2-tags",
                (_, _) => Task.CompletedTask,
                CancellationToken.None);

            Assert.True(File.Exists(Path.Combine(
                csgoRoot,
                "addons",
                "counterstrikesharp",
                "plugins",
                "CS2-Tags",
                "CS2-Tags.dll")));
            var state = await manager.GetStateAsync(server, CancellationToken.None);
            Assert.True(state.Packages.Single(package => package.Id == "cs2-tags").Installed);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Workshop_search_returns_only_usable_cs2_map_items()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dkay-workshop-search-{Guid.NewGuid():N}");
        var server = CreateServer(root);
        try
        {
            var runtime = new Cs2RuntimeProvisioner(new DockOptions());
            runtime.SaveWorkshopApiKey(server, "0123456789abcdef0123456789abcdef");
            using var httpClient = new HttpClient(new WorkshopHandler(false));
            var manager = new Cs2ModeManager(httpClient, runtime);

            var result = await manager.SearchWorkshopMapsAsync(server, "surf beginner", 18, CancellationToken.None);

            Assert.Equal(2, result.Total);
            var map = Assert.Single(result.Items);
            Assert.Equal("3141592653", map.PublishedFileId);
            Assert.Equal("surf_beginner_cs2", map.MapName);
            Assert.Equal("Surf", Assert.Single(map.Tags));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Workshop_profile_rejects_removed_or_incompatible_item_before_writing_config()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dkay-workshop-invalid-{Guid.NewGuid():N}");
        var server = CreateServer(root);
        try
        {
            var runtime = new Cs2RuntimeProvisioner(new DockOptions());
            runtime.SaveWorkshopApiKey(server, "0123456789abcdef0123456789abcdef");
            using var httpClient = new HttpClient(new WorkshopHandler(true));
            var manager = new Cs2ModeManager(httpClient, runtime);
            var request = new ApplyCs2ModePresetRequest(
                "surf",
                "surf_beginner",
                "607186931",
                0,
                1,
                false,
                new Dictionary<string, string>());

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                manager.ApplyPresetAsync(server, request, CancellationToken.None));

            Assert.Contains("removed, private, a collection, or incompatible", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(File.Exists(Path.Combine(root, "game", "csgo", "cfg", "dkay-mode.cfg")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("game/csgo/maps/workshop/{id}/surf_test.vpk")]
    [InlineData("game/csgo/maps/workshop/{id}.vpk")]
    [InlineData("steamapps/workshop/content/730/{id}/surf_test.vpk")]
    public async Task Workshop_profile_detects_supported_cs2_cache_layouts(string relativePayloadPath)
    {
        var root = Path.Combine(Path.GetTempPath(), $"dkay-workshop-state-{Guid.NewGuid():N}");
        var server = CreateServer(root);
        try
        {
            var runtime = new Cs2RuntimeProvisioner(new DockOptions());
            runtime.SaveWorkshopApiKey(server, "0123456789abcdef0123456789abcdef");
            using var httpClient = new HttpClient(new WorkshopHandler(false));
            var manager = new Cs2ModeManager(httpClient, runtime);
            var request = new ApplyCs2ModePresetRequest(
                "surf",
                "surf_beginner_cs2",
                "3141592653",
                0,
                1,
                false,
                new Dictionary<string, string>());

            var pending = await manager.ApplyPresetAsync(server, request, CancellationToken.None);
            Assert.Equal("pending", Assert.Single(pending.Profiles).WorkshopInstallState);

            var payloadPath = Path.Combine(root, relativePayloadPath.Replace("{id}", "3141592653", StringComparison.Ordinal));
            Directory.CreateDirectory(Path.GetDirectoryName(payloadPath)!);
            await File.WriteAllTextAsync(payloadPath, "workshop-map");

            var installed = await manager.GetStateAsync(server, CancellationToken.None);
            Assert.Equal("installed", Assert.Single(installed.Profiles).WorkshopInstallState);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static GameServerInstance CreateServer(string root)
    {
        Directory.CreateDirectory(Path.Combine(root, "game", "csgo", "cfg"));
        return GameServerInstance.Create(
            Guid.NewGuid(),
            "CS2 Workshop test",
            "counter-strike-2",
            root,
            "latest",
            27015,
            null,
            null,
            4096,
            "{}",
            DateTimeOffset.UtcNow);
    }

    private static byte[] CreateCs2TagsArchive()
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("CS2-Tags/CS2-Tags.dll");
            using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
            writer.Write("test-plugin");
        }

        return output.ToArray();
    }

    private sealed class Cs2TagsReleaseHandler(byte[] archive) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            HttpResponseMessage response;
            if (request.RequestUri?.Host == "api.github.com")
            {
                response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "{\"tag_name\":\"build-test\",\"assets\":[{\"name\":\"CS2-Tags.zip\",\"browser_download_url\":\"https://github.com/daffyyyy/CS2-Tags/releases/download/build-test/CS2-Tags.zip\"}]}",
                        Encoding.UTF8,
                        "application/json")
                };
            }
            else
            {
                response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(archive)
                };
            }

            response.RequestMessage = request;
            return Task.FromResult(response);
        }
    }

    private sealed class WorkshopHandler(bool removedDetails) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            const string usableMap = "{\"publishedfileid\":\"3141592653\",\"result\":1,\"consumer_appid\":730,\"file_type\":0,\"title\":\"surf_beginner_cs2\",\"file_size\":\"104857600\",\"subscriptions\":\"42000\",\"time_updated\":1770000000,\"preview_url\":\"https://images.steamusercontent.com/example.jpg\",\"tags\":[{\"tag\":\"Surf\",\"display_name\":\"Surf\"}]}";
            var isSearch = request.RequestUri?.AbsolutePath.Contains("QueryFiles", StringComparison.Ordinal) == true;
            var body = removedDetails
                ? "{\"response\":{\"result\":1,\"resultcount\":1,\"publishedfiledetails\":[{\"publishedfileid\":\"607186931\",\"result\":9}]}}"
                : isSearch
                    ? $"{{\"response\":{{\"total\":2,\"publishedfiledetails\":[{usableMap},{{\"publishedfileid\":\"607186931\",\"result\":9}}]}}}}"
                    : $"{{\"response\":{{\"result\":1,\"resultcount\":1,\"publishedfiledetails\":[{usableMap}]}}}}";
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
                RequestMessage = request
            };
            return Task.FromResult(response);
        }
    }
}
