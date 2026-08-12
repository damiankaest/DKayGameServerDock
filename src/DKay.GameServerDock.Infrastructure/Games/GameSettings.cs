using System.Text.Json;
using DKay.GameServerDock.Domain;

namespace DKay.GameServerDock.Infrastructure.Games;

internal static class GameSettings
{
    public static IReadOnlyDictionary<string, string> Read(GameServerInstance server) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(server.SettingsJson)
        ?? new Dictionary<string, string>();

    public static string Get(this IReadOnlyDictionary<string, string> settings, string key, string fallback = "") =>
        settings.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : fallback;

    public static string SafeConfigValue(this IReadOnlyDictionary<string, string> settings, string key, string fallback = "") =>
        settings.Get(key, fallback).Replace("\r", string.Empty, StringComparison.Ordinal).Replace("\n", string.Empty, StringComparison.Ordinal);
}

