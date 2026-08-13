using System.Globalization;
using System.Text.RegularExpressions;
using DKay.GameServerDock.Application.Models;

namespace DKay.GameServerDock.Infrastructure.Games;

public static partial class Cs2ModeCatalog
{
    public static IReadOnlyList<Cs2ModePresetDescriptor> Presets { get; } =
    [
        new(
            "classic",
            "Classic / Practice",
            "Core",
            "5V5",
            "A clean competitive baseline with editable warmup, round and team rules.",
            ["de_", "cs_"],
            [],
            [
                Integer("mp_freezetime", "Freeze time", "15", 0, 60, "Seconds before a round starts."),
                Decimal("mp_roundtime", "Round time", "1.92", 0.5m, 60, "Round length in minutes."),
                Integer("mp_warmuptime", "Warmup time", "60", 0, 600, "Warmup length in seconds."),
                Boolean("mp_autoteambalance", "Auto team balance", "1", "Keep teams balanced."),
                Integer("mp_limitteams", "Team difference limit", "2", 0, 10, "Maximum team size difference."),
                Fixed("mp_friendlyfire", "0", "Prevent accidental damage to teammates."),
                Fixed("mp_teammates_are_enemies", "0", "Use normal team targeting."),
                Fixed("mp_solid_teammates", "1", "Use normal teammate collision."),
                Fixed("mp_respawn_on_death_t", "0", "Use normal round-based Terrorist spawns."),
                Fixed("mp_respawn_on_death_ct", "0", "Use normal round-based Counter-Terrorist spawns."),
                Fixed("mp_ignore_round_win_conditions", "0", "Use normal bomb, hostage and elimination win conditions."),
                Fixed("sv_airaccelerate", "12", "Valve-style air acceleration."),
                Fixed("sv_enablebunnyhopping", "0", "Automatic bunny hopping disabled.")
            ]),
        new(
            "surf",
            "Surf",
            "Movement",
            "SURF",
            "High air control, long rounds and a managed timer stack for staged surf maps.",
            ["surf_"],
            ["metamod-source", "counterstrikesharp", "cs2-tags", "movement-unlocker", "rampbug-fix", "sharp-timer"],
            [
                Integer("sv_airaccelerate", "Air acceleration", "150", 10, 2000, "Higher values provide stronger mid-air steering."),
                Integer("sv_accelerate", "Ground acceleration", "10", 1, 100, "Ground acceleration before entering a ramp."),
                Integer("sv_gravity", "Gravity", "800", 100, 1200, "World gravity."),
                Integer("sv_maxvelocity", "Maximum velocity", "10000", 1000, 20000, "Maximum movement velocity."),
                Integer("mp_roundtime", "Round time", "60", 1, 60, "Long rounds avoid interrupting runs."),
                Fixed("mp_freezetime", "0", "No freeze time."),
                Fixed("mp_autoteambalance", "0", "Movement servers do not auto-balance."),
                Fixed("mp_limitteams", "0", "No team-size restriction."),
                Fixed("mp_friendlyfire", "0", "Runs cannot be interrupted by teammate damage."),
                Fixed("mp_teammates_are_enemies", "0", "Movement players remain non-hostile teammates."),
                Fixed("mp_solid_teammates", "0", "Players cannot body-block a surf line."),
                Fixed("mp_respawn_on_death_t", "1", "Respawn Terrorists after a failed run."),
                Fixed("mp_respawn_on_death_ct", "1", "Respawn Counter-Terrorists after a failed run."),
                Fixed("mp_respawn_immunitytime", "0", "Do not retain combat immunity on movement maps."),
                Fixed("mp_ignore_round_win_conditions", "1", "Do not end active surf runs on normal win conditions."),
                Fixed("sv_enablebunnyhopping", "1", "Bunny hopping enabled."),
                Fixed("sv_autobunnyhopping", "0", "Jump timing remains manual.")
            ]),
        new(
            "kz",
            "KZ / Climb",
            "Movement",
            "KZ",
            "Climb-oriented movement values with the official CS2KZ Metamod package stack.",
            ["kz_", "xc_"],
            ["metamod-source", "cs2kz"],
            [
                Integer("sv_airaccelerate", "Air acceleration", "100", 10, 2000, "Air control for jumps and strafes."),
                Integer("sv_accelerate", "Ground acceleration", "6", 1, 100, "Ground acceleration."),
                Integer("sv_gravity", "Gravity", "800", 100, 1200, "World gravity."),
                Integer("sv_maxvelocity", "Maximum velocity", "10000", 1000, 20000, "Maximum movement velocity."),
                Integer("mp_roundtime", "Round time", "60", 1, 60, "Long rounds avoid interrupting climbs."),
                Fixed("mp_freezetime", "0", "No freeze time."),
                Fixed("mp_autoteambalance", "0", "No automatic team changes."),
                Fixed("mp_limitteams", "0", "No team-size restriction."),
                Fixed("mp_friendlyfire", "0", "Climbers cannot damage one another."),
                Fixed("mp_teammates_are_enemies", "0", "Climbers remain non-hostile teammates."),
                Fixed("mp_solid_teammates", "0", "Players cannot body-block a climb."),
                Fixed("mp_respawn_on_death_t", "1", "Respawn Terrorists after a failed climb."),
                Fixed("mp_respawn_on_death_ct", "1", "Respawn Counter-Terrorists after a failed climb."),
                Fixed("mp_respawn_immunitytime", "0", "Do not retain combat immunity on climb maps."),
                Fixed("mp_ignore_round_win_conditions", "1", "Do not end active climbs on normal win conditions."),
                Fixed("sv_enablebunnyhopping", "1", "Bunny hopping enabled."),
                Fixed("sv_autobunnyhopping", "0", "Jump timing remains manual.")
            ]),
        new(
            "bhop",
            "Bunny Hop",
            "Movement",
            "BHOP",
            "Automatic hopping, strong air control and long rounds with timer support.",
            ["bhop_"],
            ["metamod-source", "counterstrikesharp", "cs2-tags", "movement-unlocker", "sharp-timer"],
            [
                Integer("sv_airaccelerate", "Air acceleration", "1000", 10, 2000, "Air steering strength."),
                Integer("sv_accelerate", "Ground acceleration", "10", 1, 100, "Ground acceleration."),
                Integer("sv_gravity", "Gravity", "800", 100, 1200, "World gravity."),
                Integer("sv_maxvelocity", "Maximum velocity", "10000", 1000, 20000, "Maximum movement velocity."),
                Integer("mp_roundtime", "Round time", "60", 1, 60, "Long rounds avoid interrupting runs."),
                Boolean("sv_autobunnyhopping", "Automatic hopping", "1", "Hold jump to keep hopping."),
                Fixed("sv_enablebunnyhopping", "1", "Bunny hopping enabled."),
                Fixed("mp_freezetime", "0", "No freeze time."),
                Fixed("mp_autoteambalance", "0", "No automatic team changes."),
                Fixed("mp_limitteams", "0", "No team-size restriction."),
                Fixed("mp_friendlyfire", "0", "Runners cannot damage one another."),
                Fixed("mp_teammates_are_enemies", "0", "Runners remain non-hostile teammates."),
                Fixed("mp_solid_teammates", "0", "Players cannot body-block a bhop line."),
                Fixed("mp_respawn_on_death_t", "1", "Respawn Terrorists after a failed run."),
                Fixed("mp_respawn_on_death_ct", "1", "Respawn Counter-Terrorists after a failed run."),
                Fixed("mp_respawn_immunitytime", "0", "Do not retain combat immunity on movement maps."),
                Fixed("mp_ignore_round_win_conditions", "1", "Do not end active runs on normal win conditions.")
            ]),
        new(
            "scoutzknivez",
            "ScoutzKnivez",
            "Combat",
            "S+K",
            "Low-gravity SSG 08 and knife combat without an untrusted third-party plugin.",
            ["scoutzknivez", "sk_"],
            [],
            [
                Integer("sv_gravity", "Gravity", "220", 100, 800, "Classic low-gravity feel."),
                Integer("sv_airaccelerate", "Air acceleration", "100", 10, 2000, "Mid-air control."),
                Boolean("mp_respawn_on_death_t", "T respawn", "1", "Respawn Terrorists after death."),
                Boolean("mp_respawn_on_death_ct", "CT respawn", "1", "Respawn Counter-Terrorists after death."),
                Fixed("mp_buytime", "0", "Buying disabled."),
                Fixed("mp_buy_anywhere", "0", "Buying disabled everywhere."),
                Fixed("mp_maxmoney", "0", "No economy."),
                Fixed("mp_free_armor", "2", "Free armor and helmet."),
                Fixed("mp_t_default_primary", "weapon_ssg08", "Terrorists spawn with an SSG 08."),
                Fixed("mp_ct_default_primary", "weapon_ssg08", "Counter-Terrorists spawn with an SSG 08."),
                Fixed("mp_t_default_secondary", "", "No default pistol."),
                Fixed("mp_ct_default_secondary", "", "No default pistol."),
                Fixed("mp_friendlyfire", "0", "Use normal team-vs-team damage rules."),
                Fixed("mp_teammates_are_enemies", "0", "Keep ScoutzKnivez as team combat."),
                Fixed("mp_solid_teammates", "0", "Teammates cannot block low-gravity jumps."),
                Fixed("mp_respawn_immunitytime", "0", "Players can fight immediately after respawning."),
                Fixed("mp_ignore_round_win_conditions", "1", "Keep the respawn arena running."),
                Fixed("mp_autoteambalance", "1", "Keep combat teams balanced."),
                Fixed("mp_limitteams", "1", "Prevent heavily stacked combat teams.")
            ]),
        new(
            "rpg-arena",
            "RPG Arena",
            "Progression",
            "RPG",
            "Fast respawns and progression-ready rules. The Warcraft/RPG plugin is deliberately a manual, experimental package until a maintained release is available.",
            ["dm_", "aim_", "fy_"],
            ["metamod-source", "counterstrikesharp", "warcraft-rpg"],
            [
                Integer("mp_roundtime", "Round time", "30", 1, 60, "Long arena rounds."),
                Boolean("mp_respawn_on_death_t", "T respawn", "1", "Respawn Terrorists after death."),
                Boolean("mp_respawn_on_death_ct", "CT respawn", "1", "Respawn Counter-Terrorists after death."),
                Integer("mp_respawnwavetime_t", "T respawn delay", "2", 0, 30, "Respawn delay in seconds."),
                Integer("mp_respawnwavetime_ct", "CT respawn delay", "2", 0, 30, "Respawn delay in seconds."),
                Fixed("mp_freezetime", "0", "No freeze time."),
                Fixed("mp_buy_anywhere", "1", "Buy anywhere."),
                Fixed("mp_buytime", "9999", "Buying remains available."),
                Fixed("mp_friendlyfire", "0", "Protect teammates while still allowing normal damage against the opposing team."),
                Fixed("mp_teammates_are_enemies", "0", "Keep RPG Arena as team-versus-team combat."),
                Fixed("mp_solid_teammates", "0", "Players do not block one another in tight arena corridors."),
                Fixed("mp_respawn_immunitytime", "0", "No damage immunity remains after an arena respawn."),
                Fixed("mp_damage_scale_ct_head", "1", "Counter-Terrorists receive normal head damage."),
                Fixed("mp_damage_scale_ct_body", "1", "Counter-Terrorists receive normal body damage."),
                Fixed("mp_damage_scale_t_head", "1", "Terrorists receive normal head damage."),
                Fixed("mp_damage_scale_t_body", "1", "Terrorists receive normal body damage."),
                Fixed("mp_damage_headshot_only", "0", "Both head and body hits deal damage."),
                Fixed("bot_join_team", "any", "Distribute bots across both combat teams."),
                Fixed("mp_ignore_round_win_conditions", "1", "Keep the arena running while players respawn."),
                Fixed("mp_autoteambalance", "1", "Keep both RPG teams balanced."),
                Fixed("mp_limitteams", "1", "Prevent one RPG team from becoming heavily stacked.")
            ])
    ];

    public static IReadOnlyList<Cs2ManagedPackageDescriptor> Packages { get; } =
    [
        new("metamod-source", "Metamod:Source", "Framework", "Native Source 2 plugin loader.", "AlliedModders", "https://github.com/alliedmodders/metamod-source", true, false, []),
        new("counterstrikesharp", "CounterStrikeSharp", "Framework", "Managed C# plugin framework for CS2.", "roflmuffin", "https://github.com/roflmuffin/CounterStrikeSharp", true, false, ["metamod-source"]),
        new("cs2-tags", "CS2-Tags", "Dependency", "Required player-tag provider for the managed SharpTimer fork.", "daffyyyy", "https://github.com/daffyyyy/CS2-Tags", true, false, ["counterstrikesharp"]),
        new("movement-unlocker", "Movement Unlocker", "Movement", "Removes ground speed limits for Source-style movement modes.", "Source2ZE", "https://github.com/Source2ZE/MovementUnlocker", true, false, ["metamod-source"]),
        new("rampbug-fix", "RampBugFix", "Movement", "Mitigates common CS2 surf ramp bugs; upstream notes that no fix is perfect.", "Interesting-exe", "https://github.com/Interesting-exe/CS2Fixes-RampbugFix", true, true, ["metamod-source"]),
        new("sharp-timer", "SharpTimer", "Movement", "Timer, checkpoints and rankings for Surf and Bhop.", "Letaryat community fork", "https://github.com/Letaryat/poor-sharptimer", true, true, ["counterstrikesharp", "cs2-tags"]),
        new("cs2kz", "CS2KZ", "Movement", "Official KZGlobalTeam Metamod plugin for CS2 KZ.", "KZGlobalTeam", "https://github.com/KZGlobalTeam/cs2kz-metamod", true, false, ["metamod-source"]),
        new("warcraft-rpg", "Warcraft RPG", "Progression", "Classes, XP, skills and ultimates. Kept manual because the known upstream changed maintainers and has no trusted stable channel.", "Community", "https://github.com/Wngui/CS2WarcraftMod", false, true, ["counterstrikesharp"])
    ];

    public static IReadOnlyDictionary<string, string> BuildConVars(
        Cs2ModePresetDescriptor preset,
        ApplyCs2ModePresetRequest request)
    {
        ValidateMap(request.MapName, request.WorkshopId);
        if (request.BotQuota is < 0 or > 32)
        {
            throw new ArgumentException("Bot quota must be between 0 and 32.");
        }

        if (request.BotDifficulty is < 0 or > 5)
        {
            throw new ArgumentException("Bot difficulty must be between 0 and 5.");
        }

        var definitions = preset.Settings.ToDictionary(setting => setting.Key, StringComparer.Ordinal);
        var overrides = request.Overrides ?? new Dictionary<string, string>();
        foreach (var key in overrides.Keys)
        {
            if (!definitions.TryGetValue(key, out var definition) || !definition.Editable)
            {
                throw new ArgumentException($"ConVar override '{key}' is not allowed by the selected preset.");
            }
        }

        var result = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["bot_quota"] = request.BotQuota.ToString(CultureInfo.InvariantCulture),
            ["bot_difficulty"] = request.BotDifficulty.ToString(CultureInfo.InvariantCulture),
            ["bot_quota_mode"] = "fill",
            ["bot_join_after_player"] = "0",
            ["sv_cheats"] = "0"
        };

        foreach (var definition in preset.Settings)
        {
            var value = overrides.TryGetValue(definition.Key, out var candidate)
                ? candidate
                : definition.DefaultValue;
            result[definition.Key] = Normalize(definition, value);
        }

        return result;
    }

    public static void ValidateMap(string mapName, string? workshopId)
    {
        if (string.IsNullOrWhiteSpace(mapName) || !MapNamePattern().IsMatch(mapName.Trim()))
        {
            throw new ArgumentException("Map name may only contain letters, digits, underscores and hyphens (maximum 64 characters).");
        }

        if (!string.IsNullOrWhiteSpace(workshopId) && !WorkshopIdPattern().IsMatch(workshopId.Trim()))
        {
            throw new ArgumentException("Workshop id must be a positive numeric Steam Workshop id.");
        }
    }

    private static string Normalize(Cs2ConVarDescriptor definition, string value)
    {
        value = value.Trim();
        if (definition.Type == "boolean")
        {
            return value.ToLowerInvariant() switch
            {
                "1" or "true" => "1",
                "0" or "false" => "0",
                _ => throw new ArgumentException($"'{definition.Label}' must be enabled or disabled.")
            };
        }

        if (definition.Type is "integer" or "decimal")
        {
            if (!decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var number) ||
                definition.Type == "integer" && number != decimal.Truncate(number) ||
                definition.Minimum is { } minimum && number < minimum ||
                definition.Maximum is { } maximum && number > maximum)
            {
                throw new ArgumentException($"'{definition.Label}' is outside its allowed range.");
            }

            return number.ToString(CultureInfo.InvariantCulture);
        }

        if (definition.Options is { Count: > 0 } options && !options.Contains(value, StringComparer.Ordinal))
        {
            throw new ArgumentException($"'{definition.Label}' contains an unsupported value.");
        }

        return value;
    }

    private static Cs2ConVarDescriptor Fixed(string key, string value, string description) =>
        new(key, key, "text", value, false, description);

    private static Cs2ConVarDescriptor Integer(string key, string label, string value, decimal min, decimal max, string description) =>
        new(key, label, "integer", value, true, description, min, max);

    private static Cs2ConVarDescriptor Decimal(string key, string label, string value, decimal min, decimal max, string description) =>
        new(key, label, "decimal", value, true, description, min, max);

    private static Cs2ConVarDescriptor Boolean(string key, string label, string value, string description) =>
        new(key, label, "boolean", value, true, description, Options: ["0", "1"]);

    [GeneratedRegex("^[A-Za-z0-9_-]{1,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex MapNamePattern();

    [GeneratedRegex("^[1-9][0-9]{0,19}$", RegexOptions.CultureInvariant)]
    private static partial Regex WorkshopIdPattern();
}
