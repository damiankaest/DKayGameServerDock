using System.IO.Compression;
using System.Net;
using System.Text;
using DKay.GameServerDock.Application.Models;
using DKay.GameServerDock.Domain;
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
}
