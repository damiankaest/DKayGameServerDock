using System.Text.Json;
using DKay.GameServerDock.Application.Models;
using DKay.GameServerDock.Application.Services;
using DKay.GameServerDock.Domain;

namespace DKay.GameServerDock.Tests;

public sealed class ServerPublicationSettingsTests
{
    [Fact]
    public void Defaults_to_private_and_the_game_port()
    {
        var server = CreateServer(port: 25565);

        var publication = ServerPublicationSettings.Read(server);

        Assert.False(publication.Published);
        Assert.Equal(25565, publication.PublicPort);
    }

    [Fact]
    public void Stores_publication_without_losing_game_settings()
    {
        var server = CreateServer(port: 27015);

        var json = ServerPublicationSettings.Apply(server, new UpdateServerPublicationRequest(true, 37015));
        server.UpdatePublication(json, DateTimeOffset.UtcNow);
        var publication = ServerPublicationSettings.Read(server);
        var settings = JsonSerializer.Deserialize<Dictionary<string, string>>(json)!;

        Assert.True(publication.Published);
        Assert.Equal(37015, publication.PublicPort);
        Assert.Equal("10", settings["maxPlayers"]);
    }

    [Fact]
    public void Preserves_publication_when_game_settings_change()
    {
        var server = CreateServer(port: 25565);
        server.UpdatePublication(
            ServerPublicationSettings.Apply(server, new UpdateServerPublicationRequest(true, 35565)),
            DateTimeOffset.UtcNow);

        var json = ServerPublicationSettings.MergeGameSettings(
            server,
            new Dictionary<string, string> { ["maxPlayers"] = "20" });
        server.UpdatePublication(json, DateTimeOffset.UtcNow);
        var publication = ServerPublicationSettings.Read(server);

        Assert.True(publication.Published);
        Assert.Equal(35565, publication.PublicPort);
    }

    [Fact]
    public void Preserves_external_installation_marker_when_game_settings_change()
    {
        var server = CreateServer(port: 27015);
        server.UpdatePublication(
            ServerPublicationSettings.MarkExternalInstallation(
                new Dictionary<string, string> { ["hostname"] = "Imported" }),
            DateTimeOffset.UtcNow);

        var json = ServerPublicationSettings.MergeGameSettings(
            server,
            new Dictionary<string, string> { ["hostname"] = "Renamed" });
        server.UpdatePublication(json, DateTimeOffset.UtcNow);

        Assert.True(ServerPublicationSettings.IsExternalInstallation(server));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    public void Rejects_invalid_public_ports(int port)
    {
        var server = CreateServer(port: 25565);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ServerPublicationSettings.Apply(server, new UpdateServerPublicationRequest(true, port)));
    }

    private static GameServerInstance CreateServer(int port) => GameServerInstance.Create(
        Guid.NewGuid(),
        "Friends server",
        "minecraft-paper",
        Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")),
        "latest",
        port,
        null,
        null,
        4096,
        "{\"maxPlayers\":\"10\"}",
        DateTimeOffset.UtcNow);
}
