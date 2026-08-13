using DKay.GameServerDock.Infrastructure.Games;

namespace DKay.GameServerDock.Tests;

public sealed class Cs2GameServerAdapterTests
{
    [Fact]
    public void ParseStatus_ReturnsWorkshopMapAndConnectedPlayers()
    {
        const string output = """
            hostname: DKay CS2 Server
            map     : workshop/3076153623/surf_kitsune at: 0 x, 0 y, 0 z
            players : 2 humans, 1 bots (16 max)
            # userid name steamid connected ping loss state rate adr
            # 2 1 "Alice" 76561198000000001 01:42 31 0 active 786432 192.0.2.1:27005
            # 3 2 "Trapper" BOT 00:18 0 0 active 0 loopback
            """;

        var status = Cs2GameServerAdapter.ParseStatus(output);

        Assert.Equal("surf_kitsune", status.Map);
        Assert.Collection(
            status.Players,
            player =>
            {
                Assert.Equal("Alice", player.Name);
                Assert.Equal("76561198000000001", player.Id);
                Assert.Equal(31, player.Ping);
                Assert.Equal(TimeSpan.FromSeconds(102), player.ConnectionTime);
            },
            player =>
            {
                Assert.Equal("Trapper", player.Name);
                Assert.Equal("BOT:3 2", player.Id);
                Assert.Equal(0, player.Ping);
            });
    }

    [Fact]
    public void ParseStatus_ReturnsEmptySnapshotForMissingOutput()
    {
        var status = Cs2GameServerAdapter.ParseStatus(null);

        Assert.Null(status.Map);
        Assert.Empty(status.Players);
    }
}
