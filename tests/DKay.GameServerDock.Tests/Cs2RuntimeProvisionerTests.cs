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
    public void Suppresses_known_cs2_console_noise_without_hiding_native_plugin_failures()
    {
        Assert.False(ConsoleOutputPolicy.ShouldRecord("CTextConsoleWin::GetLine: !GetNumberOfConsoleInputEvents"));
        Assert.False(ConsoleOutputPolicy.ShouldRecord(
            @"Could not PreloadLibrary E:\server\game\csgo\addons\counterstrikesharp\api\System.Runtime.dll - Access violation at 00007FFA."));
        Assert.True(ConsoleOutputPolicy.ShouldRecord("Connection to Steam servers successful."));
        Assert.True(ConsoleOutputPolicy.ShouldRecord(
            @"[META] Failed to load plugin addons\cs2fixes-rampbugfix\bin\win64\cs2fixes-rampbugfix: procedure not found."));
    }

    [Fact]
    public void Prepare_loads_managed_bootstrap_before_first_map_without_overwriting_autoexec()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var root = CreateTemporaryDirectory();
        var cfgRoot = Path.Combine(root, "game", "csgo", "cfg");
        Directory.CreateDirectory(cfgRoot);
        File.WriteAllText(Path.Combine(cfgRoot, "autoexec.cfg"), "hostname \"existing\"\n");
        var server = CreateServer(root, 27015);

        try
        {
            var provisioner = new Cs2RuntimeProvisioner(new DockOptions());
            provisioner.Prepare(server);
            provisioner.Prepare(server);

            var autoexec = File.ReadAllLines(Path.Combine(cfgRoot, "autoexec.cfg"));
            Assert.Contains("hostname \"existing\"", autoexec);
            Assert.Single(autoexec, line => line.Trim().Equals("exec dkay-rcon.cfg", StringComparison.OrdinalIgnoreCase));
            var bootstrap = File.ReadAllLines(Path.Combine(cfgRoot, "dkay-bootstrap.cfg"));
            Assert.Contains("exec dkay-rcon.cfg", bootstrap);
            Assert.True(File.Exists(Path.Combine(cfgRoot, "dkay-rcon.cfg")));
            Assert.True(File.Exists(Path.Combine(root, ".dkay", "rcon-password")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Prepare_migrates_legacy_gslt_and_repairs_generated_files_after_updates()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        const string token = "0123456789abcdef0123456789abcdef";
        var root = CreateTemporaryDirectory();
        var cfgRoot = Path.Combine(root, "game", "csgo", "cfg");
        Directory.CreateDirectory(cfgRoot);
        File.WriteAllText(Path.Combine(cfgRoot, "dkay-gslt.cfg"), $"sv_setsteamaccount \"{token}\"\n");
        var server = CreateServer(root, 27015);

        try
        {
            var provisioner = new Cs2RuntimeProvisioner(new DockOptions());
            provisioner.Prepare(server);

            var secretPath = Path.Combine(root, ".dkay", "gslt-token");
            Assert.Equal(token, File.ReadAllText(secretPath).Trim());
            Assert.True(provisioner.GetGsltState(server).ProtectedFromGameUpdates);

            // Simulate SteamCMD or a Hub update replacing generated game cfg files.
            File.Delete(Path.Combine(cfgRoot, "dkay-gslt.cfg"));
            File.WriteAllText(Path.Combine(cfgRoot, "dkay-bootstrap.cfg"), "overwritten\n");
            provisioner.Prepare(server);

            Assert.Contains(token, File.ReadAllText(Path.Combine(cfgRoot, "dkay-gslt.cfg")), StringComparison.Ordinal);
            Assert.Contains("exec dkay-gslt.cfg", File.ReadAllText(Path.Combine(cfgRoot, "dkay-bootstrap.cfg")), StringComparison.Ordinal);
            Assert.Equal(token, File.ReadAllText(secretPath).Trim());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Live_control_settings_survive_generated_cfg_replacement()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var root = CreateTemporaryDirectory();
        Directory.CreateDirectory(Path.Combine(root, "game", "csgo", "cfg"));
        var server = CreateServer(root, 27015);

        try
        {
            var provisioner = new Cs2RuntimeProvisioner(new DockOptions());
            var values = new Dictionary<string, string>
            {
                ["sv_cheats"] = "1",
                ["sv_maxvelocity"] = "10000",
                ["mp_warmuptime"] = "180",
                ["bot_quota_mode"] = "Fill"
            };
            var saved = provisioner.SaveLiveSettings(server, values);
            Assert.Equal("1", saved["sv_cheats"]);
            Assert.Equal("10000", saved["sv_maxvelocity"]);
            Assert.Equal("fill", saved["bot_quota_mode"]);

            var liveConfigPath = Path.Combine(root, "game", "csgo", "cfg", "dkay-live.cfg");
            File.WriteAllText(liveConfigPath, "overwritten\n");
            provisioner.Prepare(server);

            var liveConfig = File.ReadAllText(liveConfigPath);
            Assert.Contains("sv_cheats 1", liveConfig, StringComparison.Ordinal);
            Assert.Contains("sv_maxvelocity 10000", liveConfig, StringComparison.Ordinal);
            Assert.Contains("mp_warmuptime 180", liveConfig, StringComparison.Ordinal);
            Assert.Contains("bot_quota_mode fill", liveConfig, StringComparison.Ordinal);
            Assert.True(File.Exists(Path.Combine(root, ".dkay", "live-settings.json")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Applying_a_preset_aligns_only_overlapping_persisted_live_settings()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var root = CreateTemporaryDirectory();
        Directory.CreateDirectory(Path.Combine(root, "game", "csgo", "cfg"));
        var server = CreateServer(root, 27015);

        try
        {
            var provisioner = new Cs2RuntimeProvisioner(new DockOptions());
            provisioner.SaveLiveSettings(server, new Dictionary<string, string>
            {
                ["sv_cheats"] = "1",
                ["mp_friendlyfire"] = "0",
                ["mp_respawn_on_death_ct"] = "0",
                ["mp_respawn_on_death_t"] = "0"
            });

            provisioner.AlignPersistedLiveSettingsWithPreset(server, new Dictionary<string, string>
            {
                ["mp_friendlyfire"] = "1",
                ["mp_respawn_on_death_ct"] = "1",
                ["mp_respawn_on_death_t"] = "1",
                ["unsupported_preset_value"] = "1"
            });

            var settings = provisioner.ReadLiveSettings(server);
            Assert.Equal("1", settings["mp_friendlyfire"]);
            Assert.Equal("1", settings["mp_respawn_on_death_ct"]);
            Assert.Equal("1", settings["mp_respawn_on_death_t"]);
            Assert.Equal("1", settings["sv_cheats"]);
            Assert.DoesNotContain("unsupported_preset_value", settings.Keys);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Steam_workshop_key_is_masked_and_survives_generated_file_replacement()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        const string key = "0123456789abcdef0123456789abcdef";
        var root = CreateTemporaryDirectory();
        Directory.CreateDirectory(Path.Combine(root, "game", "csgo", "cfg"));
        var server = CreateServer(root, 27015);

        try
        {
            var provisioner = new Cs2RuntimeProvisioner(new DockOptions());
            var state = provisioner.SaveWorkshopApiKey(server, key);
            Assert.True(state.Configured);
            Assert.NotNull(state.MaskedKey);
            Assert.DoesNotContain(key, state.MaskedKey!, StringComparison.Ordinal);
            Assert.True(state.ProtectedFromGameUpdates);

            var generatedPath = Path.Combine(root, "game", "csgo", "webapi_authkey.txt");
            Assert.Equal(key, File.ReadAllText(generatedPath).Trim());
            File.Delete(generatedPath);

            provisioner.Prepare(server);

            Assert.Equal(key, File.ReadAllText(generatedPath).Trim());
            Assert.Equal(key, File.ReadAllText(Path.Combine(root, ".dkay", "steam-web-api-key")).Trim());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Steam_workshop_key_rejects_non_api_key_values()
    {
        var root = CreateTemporaryDirectory();
        var server = CreateServer(root, 27015);
        try
        {
            var provisioner = new Cs2RuntimeProvisioner(new DockOptions());
            Assert.Throws<InvalidOperationException>(() => provisioner.SaveWorkshopApiKey(server, "+quit"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Workshop_launch_cfg_runs_after_source2_init_without_exposing_the_api_key()
    {
        const string key = "0123456789abcdef0123456789abcdef";
        const string publishedFileId = "3076153623";
        var root = CreateTemporaryDirectory();
        Directory.CreateDirectory(Path.Combine(root, "game", "csgo", "cfg"));
        var server = CreateServer(root, 27015);

        try
        {
            var provisioner = new Cs2RuntimeProvisioner(new DockOptions());
            provisioner.SaveWorkshopApiKey(server, key);
            provisioner.WriteWorkshopLaunchConfiguration(server, publishedFileId);

            var launchConfig = File.ReadAllText(Path.Combine(root, "game", "csgo", "cfg", "dkay-workshop-start.cfg"));
            Assert.Contains($"DKAY_WORKSHOP_REQUEST {publishedFileId}", launchConfig, StringComparison.Ordinal);
            Assert.Contains($"host_workshop_map {publishedFileId}", launchConfig, StringComparison.Ordinal);
            Assert.Contains("sv_debug_ugc_downloads 1", launchConfig, StringComparison.Ordinal);
            Assert.Contains("exec dkay-server.cfg", launchConfig, StringComparison.Ordinal);
            Assert.Contains("exec dkay-live.cfg", launchConfig, StringComparison.Ordinal);
            Assert.DoesNotContain(key, launchConfig, StringComparison.Ordinal);
            Assert.Throws<InvalidOperationException>(() =>
                provisioner.WriteWorkshopLaunchConfiguration(server, "3076153623;quit"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Authenticates_and_executes_a_local_rcon_command()
    {
        var root = CreateTemporaryDirectory();
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var server = CreateServer(root, port);
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

    [Fact]
    public async Task Waits_for_a_starting_rcon_listener()
    {
        var root = CreateTemporaryDirectory();
        var port = ReserveTcpPort();
        using var listener = new TcpListener(IPAddress.Loopback, port);
        var server = CreateServer(root, port);
        Directory.CreateDirectory(Path.Combine(root, ".dkay"));
        File.WriteAllText(Path.Combine(root, ".dkay", "rcon-password"), "test-password");

        try
        {
            var fakeServer = Task.Run(async () =>
            {
                await Task.Delay(250);
                listener.Start();
                await RunFakeRconServerAsync(listener);
            });
            var client = new Cs2RconClient(new Cs2RuntimeProvisioner(new DockOptions()));

            var output = await client.ExecuteAsync(
                server,
                "echo DKAY_PROBE",
                CancellationToken.None,
                TimeSpan.FromSeconds(3));

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

    private static GameServerInstance CreateServer(string root, int port) => GameServerInstance.Create(
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

    private static int ReserveTcpPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private sealed record TestPacket(int Id, int Type, string Body);
}
