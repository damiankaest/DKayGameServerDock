using System.Text.Json;
using DKay.GameServerDock.Application.Models;
using DKay.GameServerDock.Domain;
using DKay.GameServerDock.Infrastructure.Games;

namespace DKay.GameServerDock.Tests;

public sealed class Cs2CommunityStatsProviderTests
{
    [Fact]
    public async Task GetAsync_reads_public_rankings_without_exposing_player_identifiers()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dkay-community-{Guid.NewGuid():N}");
        var recordsRoot = Path.Combine(root, "game", "csgo", "cfg", "SharpTimer", "PlayerRecords");
        Directory.CreateDirectory(recordsRoot);
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(recordsRoot, "surf_beginner.json"),
                JsonSerializer.Serialize(new Dictionary<string, object>
                {
                    ["76561198000000001"] = new { PlayerName = "Alice", SteamID = "76561198000000001", MapName = "surf_beginner", TimerTicks = 640, Completions = 3 },
                    ["76561198000000002"] = new { PlayerName = "Bob", SteamID = "76561198000000002", MapName = "surf_beginner", TimerTicks = 576, Completions = 1 }
                }));
            var server = GameServerInstance.Create(
                Guid.NewGuid(),
                "Surf server",
                "counter-strike-2",
                root,
                "latest",
                27015,
                null,
                null,
                4096,
                "{}",
                DateTimeOffset.UtcNow);
            var profile = new Cs2ModeProfile(
                "surf_beginner",
                "surf",
                "Surf",
                "surf_beginner",
                null,
                null,
                null,
                "local",
                0,
                1,
                new Dictionary<string, string>(),
                ["sharp-timer"],
                DateTimeOffset.UtcNow,
                "peaceful",
                "standard",
                "movement",
                "instant",
                "anywhere");
            var state = new Cs2ModeState(
                profile.Id,
                [profile],
                [],
                new Cs2WorkshopAccessState(false, null, true, "not configured"));
            var playedAt = DateTimeOffset.UtcNow.AddMinutes(-5);
            var history = ServerEvent.Create(
                server.Id,
                ServerEventType.MapChanged,
                "surf_beginner is live.",
                playedAt,
                "{\"mapName\":\"surf_beginner\"}");

            var result = await new Cs2CommunityStatsProvider().GetAsync(
                server,
                state,
                [history],
                "surf_beginner",
                CancellationToken.None);

            var map = Assert.Single(result.Maps);
            Assert.True(result.RecordsAvailable);
            Assert.True(map.Active);
            Assert.Equal(1, map.PlayCount);
            Assert.Equal(2, map.UniqueRunners);
            Assert.Equal(4, map.TotalCompletions);
            Assert.Collection(
                map.Records,
                record =>
                {
                    Assert.Equal("Bob", record.PlayerName);
                    Assert.Equal("0:09.000", record.FormattedTime);
                },
                record => Assert.Equal("Alice", record.PlayerName));
            Assert.DoesNotContain("765611980000000", JsonSerializer.Serialize(result), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
