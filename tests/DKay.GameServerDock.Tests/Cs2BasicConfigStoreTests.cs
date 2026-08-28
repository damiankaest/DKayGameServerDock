using DKay.GameServerDock.Application.Models;
using DKay.GameServerDock.Domain;
using DKay.GameServerDock.Infrastructure.Games;

namespace DKay.GameServerDock.Tests;

public sealed class Cs2BasicConfigStoreTests
{
    [Fact]
    public void Saved_basic_configuration_is_persistent_and_generates_one_authoritative_cfg()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dkay-basic-{Guid.NewGuid():N}");
        var server = CreateServer(root);
        try
        {
            var store = new Cs2BasicConfigStore();
            var configuration = new Cs2BasicConfiguration(true, 650, 4);

            store.Save(server, configuration);

            Assert.Equal(configuration, store.Read(server));
            var cfg = File.ReadAllText(Path.Combine(root, "game", "csgo", "cfg", "dkay-basic.cfg"));
            Assert.Contains("sv_enablebunnyhopping \"1\"", cfg, StringComparison.Ordinal);
            Assert.Contains("sv_autobunnyhopping \"1\"", cfg, StringComparison.Ordinal);
            Assert.Contains("sv_gravity \"650\"", cfg, StringComparison.Ordinal);
            Assert.Contains("bot_quota \"4\"", cfg, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Prepare_creates_a_safe_default_configuration_for_an_existing_server()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dkay-basic-{Guid.NewGuid():N}");
        var server = CreateServer(root);
        try
        {
            var store = new Cs2BasicConfigStore();

            store.Prepare(server);

            Assert.Equal(new Cs2BasicConfiguration(false, 800, 0), store.Read(server));
            Assert.True(File.Exists(Path.Combine(root, ".dkay", "basic-config.json")));
            Assert.True(File.Exists(Path.Combine(root, "game", "csgo", "cfg", "dkay-basic.cfg")));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Prepare_migrates_the_three_owned_values_from_existing_live_settings()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dkay-basic-{Guid.NewGuid():N}");
        var server = CreateServer(root);
        try
        {
            Directory.CreateDirectory(Path.Combine(root, ".dkay"));
            File.WriteAllText(
                Path.Combine(root, ".dkay", "live-settings.json"),
                """
                {
                  "sv_enablebunnyhopping": "1",
                  "sv_autobunnyhopping": "1",
                  "sv_gravity": "600",
                  "bot_quota": "3"
                }
                """);
            var store = new Cs2BasicConfigStore();

            store.Prepare(server);

            Assert.Equal(new Cs2BasicConfiguration(true, 600, 3), store.Read(server));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static GameServerInstance CreateServer(string root) => GameServerInstance.Create(
        Guid.NewGuid(),
        "Basic CS2",
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
