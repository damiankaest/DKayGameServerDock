using System.Text.Json;
using DKay.GameServerDock.Application.Abstractions;
using DKay.GameServerDock.Application.Models;
using DKay.GameServerDock.Domain;

namespace DKay.GameServerDock.Infrastructure.Games;

public sealed class Cs2BasicConfigStore : ICs2BasicConfigStore
{
    private static readonly Cs2BasicConfiguration DefaultConfiguration = new(false, 800, 0);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public Cs2BasicConfiguration Read(GameServerInstance server)
    {
        ArgumentNullException.ThrowIfNull(server);
        var path = GetStatePath(server);
        if (!File.Exists(path))
        {
            return ReadExistingLiveConfiguration(server);
        }

        try
        {
            return JsonSerializer.Deserialize<Cs2BasicConfiguration>(File.ReadAllText(path), JsonOptions)
                ?? DefaultConfiguration;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"Die Basic-Konfiguration ist beschädigt: '{path}'.", exception);
        }
    }

    public Cs2BasicConfiguration Save(GameServerInstance server, Cs2BasicConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(configuration);
        Directory.CreateDirectory(Path.GetDirectoryName(GetStatePath(server))!);
        WriteAtomic(GetStatePath(server), JsonSerializer.Serialize(configuration, JsonOptions));
        WriteCfg(server, configuration);
        return configuration;
    }

    public void Prepare(GameServerInstance server)
    {
        ArgumentNullException.ThrowIfNull(server);
        var configuration = Read(server);
        if (!File.Exists(GetStatePath(server)))
        {
            Save(server, configuration);
            return;
        }

        WriteCfg(server, configuration);
    }

    private static void WriteCfg(GameServerInstance server, Cs2BasicConfiguration configuration)
    {
        var cfgDirectory = Path.Combine(server.InstallDirectory, "game", "csgo", "cfg");
        Directory.CreateDirectory(cfgDirectory);
        var enabled = configuration.AutoBhop ? "1" : "0";
        var contents = string.Join(Environment.NewLine,
            "// Basic configuration managed by DKay Game Server Dock.",
            "// This file is generated from .dkay/basic-config.json and loaded last.",
            $"sv_enablebunnyhopping \"{enabled}\"",
            $"sv_autobunnyhopping \"{enabled}\"",
            $"sv_gravity \"{configuration.Gravity}\"",
            $"bot_quota \"{configuration.BotQuota}\"",
            string.Empty);
        WriteAtomic(Path.Combine(cfgDirectory, "dkay-basic.cfg"), contents);
    }

    private static void WriteAtomic(string path, string contents)
    {
        var temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath, contents);
        File.Move(temporaryPath, path, overwrite: true);
    }

    private static string GetStatePath(GameServerInstance server) =>
        Path.Combine(server.InstallDirectory, ".dkay", "basic-config.json");

    private static Cs2BasicConfiguration ReadExistingLiveConfiguration(GameServerInstance server)
    {
        var liveSettingsPath = Path.Combine(server.InstallDirectory, ".dkay", "live-settings.json");
        if (!File.Exists(liveSettingsPath))
        {
            return DefaultConfiguration;
        }

        try
        {
            var values = JsonSerializer.Deserialize<Dictionary<string, string>>(
                File.ReadAllText(liveSettingsPath),
                JsonOptions) ?? [];
            var autoBhop = values.TryGetValue("sv_enablebunnyhopping", out var enabled) && enabled == "1" &&
                           values.TryGetValue("sv_autobunnyhopping", out var automatic) && automatic == "1";
            var gravity = values.TryGetValue("sv_gravity", out var gravityValue) &&
                          int.TryParse(gravityValue, out var parsedGravity) && parsedGravity is >= 100 and <= 2000
                ? parsedGravity
                : DefaultConfiguration.Gravity;
            var botQuota = values.TryGetValue("bot_quota", out var botQuotaValue) &&
                           int.TryParse(botQuotaValue, out var parsedBotQuota) && parsedBotQuota is >= 0 and <= 32
                ? parsedBotQuota
                : DefaultConfiguration.BotQuota;
            return new Cs2BasicConfiguration(autoBhop, gravity, botQuota);
        }
        catch (JsonException)
        {
            return DefaultConfiguration;
        }
    }
}
