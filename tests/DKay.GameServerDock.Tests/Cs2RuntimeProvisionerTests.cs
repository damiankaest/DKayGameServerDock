using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using DKay.GameServerDock.Domain;
using DKay.GameServerDock.Infrastructure;
using DKay.GameServerDock.Infrastructure.Games;
using DKay.GameServerDock.Infrastructure.Processes;

namespace DKay.GameServerDock.Tests;

public sealed class Cs2RuntimeProvisionerTests
{
    private static readonly string[] RuntimeFiles =
    [
        "steamclient64.dll",
        "tier0_s64.dll",
        "vstdlib_s64.dll"
    ];

    [Fact]
    public void Copies_required_steam_runtime_next_to_cs2()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var steamCmdDirectory = Path.Combine(root, "steamcmd");
            var serverDirectory = Path.Combine(root, "server");
            var destination = Path.Combine(serverDirectory, "game", "bin", "win64");
            Directory.CreateDirectory(steamCmdDirectory);
            Directory.CreateDirectory(destination);
            foreach (var file in RuntimeFiles)
            {
                File.WriteAllText(Path.Combine(steamCmdDirectory, file), $"test-{file}");
            }

            Cs2RuntimeProvisioner.CopyWindowsSteamRuntime(
                Path.Combine(steamCmdDirectory, "steamcmd.exe"),
                serverDirectory);

            foreach (var file in RuntimeFiles)
            {
                Assert.Equal($"test-{file}", File.ReadAllText(Path.Combine(destination, file)));
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Rejects_incomplete_steam_runtime()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var steamCmdDirectory = Path.Combine(root, "steamcmd");
            var serverDirectory = Path.Combine(root, "server");
            Directory.CreateDirectory(steamCmdDirectory);
            Directory.CreateDirectory(Path.Combine(serverDirectory, "game", "bin", "win64"));
            File.WriteAllText(Path.Combine(steamCmdDirectory, "steamclient64.dll"), "test");

            var exception = Assert.Throws<InvalidOperationException>(() =>
                Cs2RuntimeProvisioner.CopyWindowsSteamRuntime(
                    Path.Combine(steamCmdDirectory, "steamcmd.exe"),
                    serverDirectory));

            Assert.Contains("tier0_s64.dll", exception.Message, StringComparison.Ordinal);
            Assert.Contains("vstdlib_s64.dll", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Suppresses_cs2_console_input_polling_noise_only()
    {
        Assert.False(ConsoleOutputPolicy.ShouldRecord("CTextConsoleWin::GetLine: !GetNumberOfConsoleInputEvents"));
        Assert.True(ConsoleOutputPolicy.ShouldRecord("Connection to Steam servers successful."));
    }

    [Fact]
    public async Task Authenticates_and_executes_a_local_rcon_command()
    {
        var root = CreateTemporaryDirectory();
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var server = GameServerInstance.Create(
            Guid.NewGuid(),
            "CS2 test",
            "counter-strike-2",
            root,
            "latest",
            port,
            null,
            null,
            4096,
            "{}",
            DateTimeOffset.UtcNow);
        Directory.CreateDirectory(Path.Combine(root, ".dkay"));
        File.WriteAllText(Path.Combine(root, ".dkay", "rcon-password"), "test-password");

        try
        {
            var fakeServer = RunFakeRconServerAsync(listener);
            var client = new Cs2RconClient(new Cs2RuntimeProvisioner(new DockOptions()));
            var output = await client.ExecuteAsync(server, "echo DKAY_PROBE", CancellationToken.None);

            Assert.Contains("DKAY_PROBE", output, StringComparison.Ordinal);
            await fakeServer;
        }
        finally
        {
            listener.Stop();
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task RunFakeRconServerAsync(TcpListener listener)
    {
        using var connection = await listener.AcceptTcpClientAsync();
        await using var stream = connection.GetStream();
        var authentication = await ReadPacketAsync(stream);
        Assert.Equal(3, authentication.Type);
        Assert.Equal("test-password", authentication.Body);
        await WritePacketAsync(stream, authentication.Id, 2, string.Empty);

        var command = await ReadPacketAsync(stream);
        Assert.Equal(2, command.Type);
        Assert.Equal("echo DKAY_PROBE", command.Body);
        await WritePacketAsync(stream, command.Id, 0, "DKAY_PROBE");
    }

    private static async Task<TestPacket> ReadPacketAsync(NetworkStream stream)
    {
        var sizeBytes = new byte[4];
        await stream.ReadExactlyAsync(sizeBytes);
        var size = BinaryPrimitives.ReadInt32LittleEndian(sizeBytes);
        var payload = new byte[size];
        await stream.ReadExactlyAsync(payload);
        var bodyLength = Array.IndexOf(payload, (byte)0, 8) - 8;
        return new TestPacket(
            BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(0, 4)),
            BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(4, 4)),
            Encoding.UTF8.GetString(payload, 8, bodyLength));
    }

    private static async Task WritePacketAsync(NetworkStream stream, int id, int type, string body)
    {
        var bodyBytes = Encoding.UTF8.GetBytes(body);
        var size = bodyBytes.Length + 10;
        var packet = new byte[size + 4];
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(0, 4), size);
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(4, 4), id);
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(8, 4), type);
        bodyBytes.CopyTo(packet.AsSpan(12));
        await stream.WriteAsync(packet);
        await stream.FlushAsync();
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dkay-cs2-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed record TestPacket(int Id, int Type, string Body);
}
