using DKay.GameServerDock.Domain;

namespace DKay.GameServerDock.Tests;

public sealed class InstallationStatusTests
{
    [Fact]
    public void New_instance_starts_in_installing_state()
    {
        var server = GameServerInstance.Create(
            Guid.NewGuid(),
            "Friends Survival",
            "minecraft-paper",
            Path.Combine(Path.GetTempPath(), "friends"),
            "latest",
            25565,
            null,
            null,
            4096,
            "{}",
            DateTimeOffset.UtcNow);

        Assert.Equal(ServerStatus.Installing, server.Status);
    }

    [Fact]
    public void Rejects_invalid_ports_and_tiny_memory_limits()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => GameServerInstance.Create(
            Guid.NewGuid(), "Invalid", "minecraft-paper", "server", "latest", 70000, null, null, 256, "{}", DateTimeOffset.UtcNow));
    }
}

