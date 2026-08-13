using System.Security.Cryptography;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using DKay.GameServerDock.Application.Abstractions;
using DKay.GameServerDock.Application.Models;
using DKay.GameServerDock.Domain;

namespace DKay.GameServerDock.Infrastructure.Games;

public sealed class Cs2RuntimeProvisioner(DockOptions options) : ICs2RuntimeControlStore
{
    private static readonly string[] WindowsSteamRuntimeFiles =
    [
        "steamclient64.dll",
        "tier0_s64.dll",
        "vstdlib_s64.dll"
    ];

    private static readonly JsonSerializerOptions SecretJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private static readonly IReadOnlyList<Cs2LiveSettingDescriptor> LiveSettingDefinitions =
    [
        new("mp_warmuptime", "Warmup time", "Round & match", "integer", "60", "Warmup duration in seconds.", 0, 3600, 5),
        new("mp_freezetime", "Freeze time", "Round & match", "integer", "15", "Freeze time before every round in seconds.", 0, 60, 1),
        new("mp_roundtime", "Round time", "Round & match", "decimal", "1.92", "Competitive round duration in minutes.", 0.1m, 60, 0.1m),
        new("mp_maxrounds", "Maximum rounds", "Round & match", "integer", "24", "Rounds played before the match ends. Zero disables the limit.", 0, 100, 1),
        new("mp_timelimit", "Map time limit", "Round & match", "integer", "0", "Minutes before the map ends. Zero disables the limit.", 0, 1440, 1),
        new("mp_buytime", "Buy time", "Round & match", "integer", "20", "Seconds in which players may buy equipment.", 0, 9999, 1),

        new("mp_autoteambalance", "Auto team balance", "Teams & bots", "boolean", "0", "Move players automatically to keep teams balanced.", Options: ["0", "1"]),
        new("mp_limitteams", "Team difference limit", "Teams & bots", "integer", "0", "Maximum team-size difference. Zero allows intentionally stacked practice teams.", 0, 32, 1),
        new("mp_friendlyfire", "Friendly fire", "Teams & bots", "boolean", "0", "Allow teammates to damage one another.", Options: ["0", "1"]),
        new("bot_quota", "Bot quota", "Teams & bots", "integer", "0", "Number of bots maintained by the server.", 0, 32, 1),
        new("bot_difficulty", "Bot difficulty", "Teams & bots", "integer", "1", "Bot skill from 0 (easy) to 5 (maximum).", 0, 5, 1),
        new("bot_quota_mode", "Bot quota mode", "Teams & bots", "select", "normal", "Normal keeps manually added bots stable; fill and match manage bot counts automatically.", Options: ["normal", "fill", "match"]),

        new("sv_gravity", "Gravity", "Movement & physics", "integer", "800", "World gravity used for jumps, Surf and ScoutzKnivez.", 100, 2000, 10),
        new("sv_airaccelerate", "Air acceleration", "Movement & physics", "integer", "12", "Mid-air steering strength used by Surf, KZ and Bhop.", 0, 5000, 1),
        new("sv_accelerate", "Ground acceleration", "Movement & physics", "decimal", "5.5", "How quickly players gain speed while touching the ground.", 0, 100, 0.1m),
        new("sv_maxvelocity", "Maximum velocity", "Movement & physics", "integer", "3500", "Maximum player/entity velocity. Movement modes commonly use 10000.", 100, 20000, 100),
        new("sv_enablebunnyhopping", "Remove bhop speed cap", "Movement & physics", "boolean", "0", "Permit bunny-hop speed beyond the normal weapon cap.", Options: ["0", "1"]),
        new("sv_autobunnyhopping", "Automatic bunnyhop", "Movement & physics", "boolean", "0", "Holding jump automatically performs consecutive jumps.", Options: ["0", "1"]),
        new("sv_staminamax", "Maximum stamina", "Movement & physics", "decimal", "80", "Maximum movement stamina penalty.", 0, 100, 1),
        new("sv_staminajumpcost", "Jump stamina cost", "Movement & physics", "decimal", "0.08", "Stamina consumed by jumping.", 0, 1, 0.01m),
        new("sv_staminalandcost", "Landing stamina cost", "Movement & physics", "decimal", "0.05", "Stamina consumed by landing.", 0, 1, 0.01m),

        new("sv_cheats", "Private-server cheats", "Admin playground", "boolean", "1", "Global CS2 switch required by noclip, bot_kill and training commands. It affects every connected player.", Options: ["0", "1"]),
        new("sv_infinite_ammo", "Infinite ammunition", "Admin playground", "select", "0", "0 disables it, 1 keeps magazines full, 2 provides infinite reserve ammunition.", Options: ["0", "1", "2"]),
        new("mp_buy_anywhere", "Buy anywhere", "Admin playground", "boolean", "0", "Allow buying outside normal buy zones.", Options: ["0", "1"]),
        new("mp_ignore_round_win_conditions", "Ignore win conditions", "Admin playground", "boolean", "0", "Keep practice rounds running after normal win conditions occur.", Options: ["0", "1"]),
        new("mp_respawn_on_death_ct", "Respawn CT players", "Admin playground", "boolean", "0", "Immediately respawn Counter-Terrorists after death.", Options: ["0", "1"]),
        new("mp_respawn_on_death_t", "Respawn T players", "Admin playground", "boolean", "0", "Immediately respawn Terrorists after death.", Options: ["0", "1"])
    ];

    public IReadOnlyList<Cs2LiveSettingDescriptor> SettingDefinitions => LiveSettingDefinitions;

    public void ProtectPersistentState(GameServerInstance server)
    {
        ArgumentNullException.ThrowIfNull(server);
        // Capture the manually created legacy cfg before SteamCMD has any opportunity to replace
        // files under game/csgo. Subsequent repairs always use the private .dkay copy.
        MigrateLegacyGslt(server);
        MigrateLegacyWorkshopApiKey(server);
    }

    public void Prepare(GameServerInstance server)
    {
        ArgumentNullException.ThrowIfNull(server);
        Directory.CreateDirectory(server.InstallDirectory);

        if (OperatingSystem.IsWindows())
        {
            CopyWindowsSteamRuntime(options.SteamCmdPath, server.InstallDirectory);
        }

        File.WriteAllText(Path.Combine(server.InstallDirectory, "steam_appid.txt"), "730\n");
        WriteRconConfiguration(server, GetOrCreateRconPassword(server));
        MigrateLegacyGslt(server);
        MigrateLegacyWorkshopApiKey(server);
        WriteGsltConfiguration(server);
        WriteWorkshopApiKey(server);
        WriteLiveConfiguration(server, ReadPersistedLiveSettings(server));
        WriteBootstrapConfiguration(server);
        EnsureRconAutoexec(server);
    }

    public IReadOnlyDictionary<string, string> ReadLiveSettings(GameServerInstance server)
    {
        ArgumentNullException.ThrowIfNull(server);
        var persisted = ReadPersistedLiveSettings(server);
        return LiveSettingDefinitions.ToDictionary(
            definition => definition.Key,
            definition => persisted.TryGetValue(definition.Key, out var value)
                ? value
                : definition.DefaultValue,
            StringComparer.Ordinal);
    }

    public IReadOnlyDictionary<string, string> SaveLiveSettings(
        GameServerInstance server,
        IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(values);
        var definitions = LiveSettingDefinitions.ToDictionary(setting => setting.Key, StringComparer.Ordinal);
        var unknown = values.Keys.FirstOrDefault(key => !definitions.ContainsKey(key));
        if (unknown is not null)
        {
            throw new InvalidOperationException($"Live setting '{unknown}' is not supported.");
        }

        var normalized = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var definition in LiveSettingDefinitions)
        {
            var candidate = values.TryGetValue(definition.Key, out var value)
                ? value
                : definition.DefaultValue;
            normalized[definition.Key] = NormalizeLiveSetting(definition, candidate);
        }

        var path = GetLiveSettingsPath(server);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        WriteAtomic(path, JsonSerializer.Serialize(normalized, SecretJsonOptions));
        WriteLiveConfiguration(server, normalized);
        return normalized;
    }

    public Cs2GsltState GetGsltState(GameServerInstance server)
    {
        ArgumentNullException.ThrowIfNull(server);
        MigrateLegacyGslt(server);
        var token = ReadGsltToken(server);
        if (token is null)
        {
            return new Cs2GsltState(
                false,
                null,
                false,
                "No Steam game-server login token is stored yet.");
        }

        return new Cs2GsltState(
            true,
            $"••••••••{token[^4..]}",
            true,
            "Stored in the private .dkay directory and regenerated after game or Hub updates.");
    }

    public Cs2GsltState SaveGsltToken(GameServerInstance server, string token)
    {
        ArgumentNullException.ThrowIfNull(server);
        token = token?.Trim() ?? string.Empty;
        if (!Regex.IsMatch(token, "^[A-Za-z0-9]{20,128}$", RegexOptions.CultureInvariant))
        {
            throw new InvalidOperationException("The GSLT must be a single Steam token containing only letters and numbers.");
        }

        var path = GetGsltSecretPath(server);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        WriteAtomic(path, token + Environment.NewLine);
        WriteGsltConfiguration(server);
        WriteBootstrapConfiguration(server);
        return GetGsltState(server);
    }

    public Cs2WorkshopAccessState GetWorkshopAccessState(GameServerInstance server)
    {
        ArgumentNullException.ThrowIfNull(server);
        MigrateLegacyWorkshopApiKey(server);
        var key = ReadWorkshopApiKey(server);
        if (key is null)
        {
            return new Cs2WorkshopAccessState(
                false,
                null,
                false,
                "A Steam Web API key is required to browse and download Workshop maps.");
        }

        return new Cs2WorkshopAccessState(
            true,
            $"••••••••{key[^4..]}",
            true,
            "Workshop access is protected in .dkay and restored after Steam or Hub updates.");
    }

    public Cs2WorkshopAccessState SaveWorkshopApiKey(GameServerInstance server, string key)
    {
        ArgumentNullException.ThrowIfNull(server);
        key = key?.Trim() ?? string.Empty;
        if (!Regex.IsMatch(key, "^[A-Fa-f0-9]{32}$", RegexOptions.CultureInvariant))
        {
            throw new InvalidOperationException("The Steam Web API key must contain exactly 32 hexadecimal characters.");
        }

        var path = GetWorkshopApiKeySecretPath(server);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        WriteAtomic(path, key + Environment.NewLine);
        WriteWorkshopApiKey(server);
        return GetWorkshopAccessState(server);
    }

    public string GetWorkshopApiKey(GameServerInstance server) =>
        ReadWorkshopApiKey(server)
        ?? throw new InvalidOperationException(
            "Configure a Steam Web API key in the Workshop map browser before searching for or loading Workshop maps.");

    public void WriteWorkshopLaunchConfiguration(GameServerInstance server, string publishedFileId)
    {
        ArgumentNullException.ThrowIfNull(server);
        publishedFileId = publishedFileId?.Trim() ?? string.Empty;
        if (!Regex.IsMatch(publishedFileId, "^[1-9][0-9]{5,19}$", RegexOptions.CultureInvariant))
        {
            throw new InvalidOperationException("The active Workshop map has an invalid published-file id.");
        }

        _ = GetWorkshopApiKey(server);
        var configDirectory = GetConfigDirectory(server);
        Directory.CreateDirectory(configDirectory);
        WriteAtomic(
            Path.Combine(configDirectory, "dkay-workshop-start.cfg"),
            $"// Generated by DKay Game Server Dock on every Workshop start.{Environment.NewLine}" +
            "// The Web API key stays in game/csgo/webapi_authkey.txt and is never added to the process command line." + Environment.NewLine +
            "sv_debug_ugc_downloads 1" + Environment.NewLine +
            $"echo \"DKAY_WORKSHOP_REQUEST {publishedFileId}\"{Environment.NewLine}" +
            $"host_workshop_map {publishedFileId}{Environment.NewLine}" +
            "exec dkay-server.cfg" + Environment.NewLine +
            "exec dkay-live.cfg" + Environment.NewLine);
    }

    public string GetRconPassword(GameServerInstance server)
    {
        var secretPath = GetRconSecretPath(server);
        if (!File.Exists(secretPath))
        {
            throw new InvalidOperationException(
                "The CS2 command-channel secret is missing. Stop the server, run Update server once and start it again.");
        }

        var password = File.ReadAllText(secretPath).Trim();
        if (string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException("The CS2 command-channel secret is empty.");
        }

        return password;
    }

    public static void CopyWindowsSteamRuntime(string steamCmdPath, string serverInstallDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(steamCmdPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(serverInstallDirectory);

        var steamCmdDirectory = Path.GetDirectoryName(Path.GetFullPath(steamCmdPath));
        if (string.IsNullOrWhiteSpace(steamCmdDirectory))
        {
            throw new InvalidOperationException($"SteamCMD has no parent directory: '{steamCmdPath}'.");
        }

        var destination = Path.Combine(serverInstallDirectory, "game", "bin", "win64");
        if (!Directory.Exists(destination))
        {
            throw new InvalidOperationException(
                $"The CS2 Windows runtime directory is missing at '{destination}'. Run Update server to repair the installation.");
        }

        var missingFiles = WindowsSteamRuntimeFiles
            .Where(file => !File.Exists(Path.Combine(steamCmdDirectory, file)))
            .ToArray();
        if (missingFiles.Length > 0)
        {
            throw new InvalidOperationException(
                $"SteamCMD is missing its Windows server runtime ({string.Join(", ", missingFiles)}). Run '{steamCmdPath} +quit' once and retry.");
        }

        foreach (var file in WindowsSteamRuntimeFiles)
        {
            File.Copy(Path.Combine(steamCmdDirectory, file), Path.Combine(destination, file), overwrite: true);
        }
    }

    private static string GetOrCreateRconPassword(GameServerInstance server)
    {
        var secretPath = GetRconSecretPath(server);
        if (File.Exists(secretPath))
        {
            var existing = File.ReadAllText(secretPath).Trim();
            if (!string.IsNullOrWhiteSpace(existing))
            {
                return existing;
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(secretPath)!);
        var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        File.WriteAllText(secretPath, password + Environment.NewLine);
        return password;
    }

    private static void WriteRconConfiguration(GameServerInstance server, string password)
    {
        var configDirectory = Path.Combine(server.InstallDirectory, "game", "csgo", "cfg");
        Directory.CreateDirectory(configDirectory);
        File.WriteAllText(
            Path.Combine(configDirectory, "dkay-rcon.cfg"),
            $"// Managed by DKay Game Server Dock. Do not share this file.{Environment.NewLine}rcon_password \"{password}\"{Environment.NewLine}");
    }

    private static void EnsureRconAutoexec(GameServerInstance server)
    {
        const string directive = "exec dkay-rcon.cfg";
        var configDirectory = Path.Combine(server.InstallDirectory, "game", "csgo", "cfg");
        Directory.CreateDirectory(configDirectory);
        var autoexecPath = Path.Combine(configDirectory, "autoexec.cfg");
        if (File.Exists(autoexecPath) && File.ReadLines(autoexecPath).Any(line =>
                line.Trim().Equals(directive, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var separator = File.Exists(autoexecPath) && new FileInfo(autoexecPath).Length > 0
            ? Environment.NewLine
            : string.Empty;
        File.AppendAllText(
            autoexecPath,
            $"{separator}// Load the private local administrator command channel before the first map.{Environment.NewLine}{directive}{Environment.NewLine}");
    }

    private static void WriteBootstrapConfiguration(GameServerInstance server)
    {
        var configDirectory = GetConfigDirectory(server);
        Directory.CreateDirectory(configDirectory);
        var lines = new List<string>
        {
            "// Generated by DKay Game Server Dock on every start.",
            "// Canonical secrets live in .dkay and survive Steam/game updates.",
            "exec dkay-rcon.cfg"
        };
        if (ReadGsltToken(server) is not null)
        {
            lines.Add("exec dkay-gslt.cfg");
        }

        File.WriteAllLines(Path.Combine(configDirectory, "dkay-bootstrap.cfg"), lines);
    }

    private static void WriteGsltConfiguration(GameServerInstance server)
    {
        var token = ReadGsltToken(server);
        if (token is null)
        {
            return;
        }

        var configDirectory = GetConfigDirectory(server);
        Directory.CreateDirectory(configDirectory);
        File.WriteAllText(
            Path.Combine(configDirectory, "dkay-gslt.cfg"),
            $"// Generated from the protected .dkay token store. Do not share this file.{Environment.NewLine}" +
            $"sv_setsteamaccount \"{token}\"{Environment.NewLine}");
    }

    private static void WriteWorkshopApiKey(GameServerInstance server)
    {
        var key = ReadWorkshopApiKey(server);
        if (key is null)
        {
            return;
        }

        var csgoDirectory = Path.Combine(server.InstallDirectory, "game", "csgo");
        Directory.CreateDirectory(csgoDirectory);
        WriteAtomic(Path.Combine(csgoDirectory, "webapi_authkey.txt"), key + Environment.NewLine);
    }

    private static void WriteLiveConfiguration(
        GameServerInstance server,
        IReadOnlyDictionary<string, string> values)
    {
        var configDirectory = GetConfigDirectory(server);
        Directory.CreateDirectory(configDirectory);
        var lines = new List<string>
        {
            "// Generated from .dkay/live-settings.json. Use the Hub Live Control page to edit."
        };
        lines.AddRange(LiveSettingDefinitions
            .Where(definition => values.ContainsKey(definition.Key))
            .Select(definition => $"{definition.Key} {values[definition.Key]}"));
        File.WriteAllLines(Path.Combine(configDirectory, "dkay-live.cfg"), lines);
    }

    private static IReadOnlyDictionary<string, string> ReadPersistedLiveSettings(GameServerInstance server)
    {
        var path = GetLiveSettingsPath(server);
        if (!File.Exists(path))
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        try
        {
            var document = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ?? [];
            var definitions = LiveSettingDefinitions.ToDictionary(definition => definition.Key, StringComparer.Ordinal);
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (key, value) in document)
            {
                if (definitions.TryGetValue(key, out var definition))
                {
                    result[key] = NormalizeLiveSetting(definition, value);
                }
            }

            return result;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }

    private static string NormalizeLiveSetting(Cs2LiveSettingDescriptor definition, string value)
    {
        value = value?.Trim() ?? string.Empty;
        if (definition.Type == "boolean")
        {
            return value.ToLowerInvariant() switch
            {
                "1" or "true" => "1",
                "0" or "false" => "0",
                _ => throw new InvalidOperationException($"'{definition.Label}' must be enabled or disabled.")
            };
        }

        if (definition.Options is { Count: > 0 } options)
        {
            if (!options.Contains(value, StringComparer.Ordinal))
            {
                throw new InvalidOperationException($"'{definition.Label}' contains an unsupported value.");
            }

            return value;
        }

        if (definition.Type is "integer" or "decimal")
        {
            if (!decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var number) ||
                definition.Type == "integer" && number != decimal.Truncate(number) ||
                definition.Minimum is { } minimum && number < minimum ||
                definition.Maximum is { } maximum && number > maximum)
            {
                throw new InvalidOperationException($"'{definition.Label}' is outside its allowed range.");
            }

            return number.ToString(CultureInfo.InvariantCulture);
        }

        throw new InvalidOperationException($"'{definition.Label}' has an unsupported setting type.");
    }

    private static void MigrateLegacyGslt(GameServerInstance server)
    {
        if (ReadGsltToken(server) is not null)
        {
            return;
        }

        var legacyPath = Path.Combine(GetConfigDirectory(server), "dkay-gslt.cfg");
        if (!File.Exists(legacyPath))
        {
            return;
        }

        var match = Regex.Match(
            File.ReadAllText(legacyPath),
            "(?:^|\\n)\\s*sv_setsteamaccount\\s+\\\"?(?<token>[A-Za-z0-9]{20,128})\\\"?",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            return;
        }

        var path = GetGsltSecretPath(server);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        WriteAtomic(path, match.Groups["token"].Value + Environment.NewLine);
    }

    private static void MigrateLegacyWorkshopApiKey(GameServerInstance server)
    {
        if (ReadWorkshopApiKey(server) is not null)
        {
            return;
        }

        var legacyPath = Path.Combine(server.InstallDirectory, "game", "csgo", "webapi_authkey.txt");
        if (!File.Exists(legacyPath))
        {
            return;
        }

        var key = File.ReadAllText(legacyPath).Trim();
        if (!Regex.IsMatch(key, "^[A-Fa-f0-9]{32}$", RegexOptions.CultureInvariant))
        {
            return;
        }

        var path = GetWorkshopApiKeySecretPath(server);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        WriteAtomic(path, key + Environment.NewLine);
    }

    private static string? ReadGsltToken(GameServerInstance server)
    {
        var path = GetGsltSecretPath(server);
        if (!File.Exists(path))
        {
            return null;
        }

        var token = File.ReadAllText(path).Trim();
        return Regex.IsMatch(token, "^[A-Za-z0-9]{20,128}$", RegexOptions.CultureInvariant)
            ? token
            : null;
    }

    private static string? ReadWorkshopApiKey(GameServerInstance server)
    {
        var path = GetWorkshopApiKeySecretPath(server);
        if (!File.Exists(path))
        {
            return null;
        }

        var key = File.ReadAllText(path).Trim();
        return Regex.IsMatch(key, "^[A-Fa-f0-9]{32}$", RegexOptions.CultureInvariant)
            ? key
            : null;
    }

    private static void WriteAtomic(string path, string content)
    {
        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporaryPath, content);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static string GetConfigDirectory(GameServerInstance server) =>
        Path.Combine(server.InstallDirectory, "game", "csgo", "cfg");

    private static string GetGsltSecretPath(GameServerInstance server) =>
        Path.Combine(server.InstallDirectory, ".dkay", "gslt-token");

    private static string GetLiveSettingsPath(GameServerInstance server) =>
        Path.Combine(server.InstallDirectory, ".dkay", "live-settings.json");

    private static string GetWorkshopApiKeySecretPath(GameServerInstance server) =>
        Path.Combine(server.InstallDirectory, ".dkay", "steam-web-api-key");

    private static string GetRconSecretPath(GameServerInstance server) =>
        Path.Combine(server.InstallDirectory, ".dkay", "rcon-password");
}
