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
            var body = removedDetails
                ? "{\"response\":{\"result\":1,\"resultcount\":1,\"publishedfiledetails\":[{\"publishedfileid\":\"607186931\",\"result\":9}]}}"
                : "{\"response\":{\"total\":2,\"publishedfiledetails\":[{\"publishedfileid\":\"3141592653\",\"result\":1,\"consumer_appid\":730,\"file_type\":0,\"title\":\"surf_beginner_cs2\",\"file_size\":\"104857600\",\"subscriptions\":\"42000\",\"time_updated\":1770000000,\"preview_url\":\"https://images.steamusercontent.com/example.jpg\",\"tags\":[{\"tag\":\"Surf\",\"display_name\":\"Surf\"}]},{\"publishedfileid\":\"607186931\",\"result\":9}]}}";
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
                RequestMessage = request
            };
            return Task.FromResult(response);
        }
    }
}
