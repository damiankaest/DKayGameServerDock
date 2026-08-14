using System.Globalization;
using System.Text.Json;
using DKay.GameServerDock.Application.Abstractions;
using DKay.GameServerDock.Application.Models;
using DKay.GameServerDock.Domain;
using Microsoft.Data.Sqlite;

namespace DKay.GameServerDock.Infrastructure.Games;

public sealed class Cs2CommunityStatsProvider : ICs2CommunityStatsProvider
{
    private const int MaximumRecordRows = 10_000;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<Cs2CommunityStats> GetAsync(
        GameServerInstance server,
        Cs2ModeState modeState,
        IReadOnlyList<ServerEvent> events,
        string? currentMap,
        CancellationToken cancellationToken)
    {
        var recordRead = await ReadRecordsAsync(server, cancellationToken);
        var recordsByMap = recordRead.Records
            .Where(record => record.TimerTicks > 0 && !string.IsNullOrWhiteSpace(record.MapName))
            .GroupBy(record => record.MapName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderBy(record => record.TimerTicks)
                    .ThenBy(record => record.PlayerName, StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                StringComparer.OrdinalIgnoreCase);
        var history = ReadMapHistory(events);

        var maps = modeState.Profiles
            .Select(profile =>
            {
                recordsByMap.TryGetValue(profile.MapName, out var mapRecords);
                mapRecords ??= [];
                history.TryGetValue(profile.MapName, out var plays);
                plays ??= [];
                var ranked = mapRecords.Take(10).Select((record, index) => new Cs2CommunityRecord(
                    index + 1,
                    record.PlayerName,
                    record.TimerTicks,
                    string.IsNullOrWhiteSpace(record.FormattedTime) ? FormatTime(record.TimerTicks) : record.FormattedTime,
                    Math.Max(1, record.Completions),
                    record.AchievedAt)).ToArray();

                return new Cs2CommunityMapStats(
                    profile.Id,
                    profile.MapName,
                    string.IsNullOrWhiteSpace(profile.WorkshopTitle) ? profile.MapName : profile.WorkshopTitle,
                    profile.WorkshopId,
                    profile.WorkshopPreviewUrl,
                    profile.PresetName,
                    profile.WorkshopInstallState,
                    string.Equals(profile.Id, modeState.ActiveProfileId, StringComparison.Ordinal) ||
                    string.Equals(profile.MapName, currentMap, StringComparison.OrdinalIgnoreCase),
                    plays.Count,
                    plays.Count == 0 ? null : plays.Max(),
                    mapRecords.Select(record => record.PlayerKey).Distinct(StringComparer.Ordinal).Count(),
                    mapRecords.Sum(record => Math.Max(1, record.Completions)),
                    ranked);
            })
            .OrderByDescending(map => map.Active)
            .ThenByDescending(map => map.LastPlayedAt)
            .ThenBy(map => map.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new Cs2CommunityStats(maps, recordRead.Available, recordRead.Message);
    }

    private static Dictionary<string, List<DateTimeOffset>> ReadMapHistory(IReadOnlyList<ServerEvent> events)
    {
        var history = new Dictionary<string, List<DateTimeOffset>>(StringComparer.OrdinalIgnoreCase);
        foreach (var serverEvent in events.Where(item => item.Type == ServerEventType.MapChanged))
        {
            var mapName = ReadMapName(serverEvent.DataJson);
            if (string.IsNullOrWhiteSpace(mapName))
            {
                continue;
            }

            if (!history.TryGetValue(mapName, out var timestamps))
            {
                timestamps = [];
                history[mapName] = timestamps;
            }

            timestamps.Add(serverEvent.OccurredAt);
        }

        return history;
    }

    private static string? ReadMapName(string? dataJson)
    {
        if (string.IsNullOrWhiteSpace(dataJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(dataJson);
            return document.RootElement.TryGetProperty("mapName", out var mapName) && mapName.ValueKind == JsonValueKind.String
                ? mapName.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async Task<RecordReadResult> ReadRecordsAsync(
        GameServerInstance server,
        CancellationToken cancellationToken)
    {
        var sharpTimerRoot = Path.Combine(server.InstallDirectory, "game", "csgo", "cfg", "SharpTimer");
        var databasePath = Path.Combine(sharpTimerRoot, "database.db");
        Exception? databaseFailure = null;
        var databaseRead = false;
        if (File.Exists(databasePath))
        {
            try
            {
                var databaseRecords = await ReadDatabaseRecordsAsync(databasePath, cancellationToken);
                databaseRead = true;
                if (databaseRecords.Count > 0)
                {
                    return new RecordReadResult(databaseRecords, true, "Rankings are read from SharpTimer's local database.");
                }
            }
            catch (Exception exception) when (exception is SqliteException or IOException or InvalidDataException or UnauthorizedAccessException)
            {
                databaseFailure = exception;
            }
        }

        var recordsRoot = Path.Combine(sharpTimerRoot, "PlayerRecords");
        if (Directory.Exists(recordsRoot))
        {
            var jsonRecords = await ReadJsonRecordsAsync(recordsRoot, cancellationToken);
            var message = jsonRecords.Count > 0
                ? "Rankings are read from SharpTimer's local player records."
                : "SharpTimer is ready; completed runs will appear here automatically.";
            if (databaseFailure is not null)
            {
                message += " The database was temporarily unavailable, so the JSON fallback is shown.";
            }

            return new RecordReadResult(jsonRecords, true, message);
        }

        if (databaseRead)
        {
            return new RecordReadResult([], true, "SharpTimer's local database is ready; completed runs will appear here automatically.");
        }

        return new RecordReadResult(
            [],
            false,
            databaseFailure is null
                ? "SharpTimer has not created its player-record storage yet."
                : "SharpTimer's record database is currently unavailable.");
    }

    private static async Task<IReadOnlyList<RawRecord>> ReadDatabaseRecordsAsync(
        string databasePath,
        CancellationToken cancellationToken)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Shared,
            DefaultTimeout = 2
        }.ToString();
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using (var schema = connection.CreateCommand())
        {
            schema.CommandText = "PRAGMA table_info(PlayerRecords);";
            await using var reader = await schema.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                columns.Add(reader.GetString(1));
            }
        }

        string[] required = ["MapName", "PlayerName", "TimerTicks"];
        if (required.Any(column => !columns.Contains(column)))
        {
            throw new InvalidDataException("SharpTimer's PlayerRecords table is missing required columns.");
        }

        var formatted = columns.Contains("FormattedTime") ? "FormattedTime" : "'' AS FormattedTime";
        var completions = columns.Contains("TimesFinished") ? "TimesFinished" : "1 AS TimesFinished";
        var timestamp = columns.Contains("UnixStamp") ? "UnixStamp" : columns.Contains("LastFinished") ? "LastFinished AS UnixStamp" : "0 AS UnixStamp";
        var playerKey = columns.Contains("SteamID") ? "SteamID" : "PlayerName AS SteamID";
        var filters = new List<string> { "TimerTicks > 0" };
        if (columns.Contains("Style")) filters.Add("COALESCE(Style, 0) = 0");
        if (columns.Contains("Mode")) filters.Add("COALESCE(Mode, '') IN ('', 'standard')");

        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT MapName, {playerKey}, PlayerName, TimerTicks, {formatted}, {completions}, {timestamp}
            FROM PlayerRecords
            WHERE {string.Join(" AND ", filters)}
            ORDER BY MapName, TimerTicks
            LIMIT {MaximumRecordRows};
            """;
        var records = new List<RawRecord>();
        await using var rows = await command.ExecuteReaderAsync(cancellationToken);
        while (await rows.ReadAsync(cancellationToken))
        {
            var timerTicks = Convert.ToInt32(rows.GetValue(3), CultureInfo.InvariantCulture);
            var playerName = rows.IsDBNull(2) ? "Unknown player" : rows.GetString(2);
            records.Add(new RawRecord(
                rows.IsDBNull(0) ? string.Empty : rows.GetString(0),
                rows.IsDBNull(1) ? $"name:{playerName}" : rows.GetString(1),
                playerName,
                timerTicks,
                rows.IsDBNull(4) ? FormatTime(timerTicks) : rows.GetString(4),
                rows.IsDBNull(5) ? 1 : Convert.ToInt32(rows.GetValue(5), CultureInfo.InvariantCulture),
                rows.IsDBNull(6) ? null : FromUnixTime(rows.GetValue(6))));
        }

        return records;
    }

    private static async Task<IReadOnlyList<RawRecord>> ReadJsonRecordsAsync(
        string recordsRoot,
        CancellationToken cancellationToken)
    {
        var records = new List<RawRecord>();
        foreach (var path in Directory.EnumerateFiles(recordsRoot, "*.json", SearchOption.TopDirectoryOnly)
                     .Where(path => !Path.GetFileNameWithoutExtension(path).Contains("_bonus", StringComparison.OrdinalIgnoreCase))
                     .Take(500))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var info = new FileInfo(path);
                if (info.Length > 8L * 1024 * 1024)
                {
                    continue;
                }

                await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 81920, true);
                var mapRecords = await JsonSerializer.DeserializeAsync<Dictionary<string, SharpTimerJsonRecord>>(
                    stream,
                    JsonOptions,
                    cancellationToken) ?? [];
                var fileMapName = Path.GetFileNameWithoutExtension(path);
                foreach (var (playerKey, record) in mapRecords)
                {
                    if (record.TimerTicks <= 0)
                    {
                        continue;
                    }

                    records.Add(new RawRecord(
                        string.IsNullOrWhiteSpace(record.MapName) ? fileMapName : record.MapName,
                        string.IsNullOrWhiteSpace(record.SteamId) ? playerKey : record.SteamId,
                        string.IsNullOrWhiteSpace(record.PlayerName) ? "Unknown player" : record.PlayerName,
                        record.TimerTicks,
                        FormatTime(record.TimerTicks),
                        Math.Max(1, record.Completions),
                        new DateTimeOffset(info.LastWriteTimeUtc)));
                }
            }
            catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
            {
                // A record can be replaced by SharpTimer while the guest page refreshes. Skip
                // that one file and keep every other map and join address available.
            }
        }

        return records;
    }

    private static DateTimeOffset? FromUnixTime(object value)
    {
        try
        {
            var seconds = Convert.ToInt64(value, CultureInfo.InvariantCulture);
            return seconds is > 0 and < 253402300799 ? DateTimeOffset.FromUnixTimeSeconds(seconds) : null;
        }
        catch (Exception exception) when (exception is FormatException or InvalidCastException or OverflowException)
        {
            return null;
        }
    }

    private static string FormatTime(int ticks)
    {
        var totalMilliseconds = (long)Math.Round(ticks * (1000d / 64d));
        var time = TimeSpan.FromMilliseconds(totalMilliseconds);
        return time.TotalHours >= 1
            ? $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}.{time.Milliseconds:000}"
            : $"{(int)time.TotalMinutes}:{time.Seconds:00}.{time.Milliseconds:000}";
    }

    private sealed record RecordReadResult(IReadOnlyList<RawRecord> Records, bool Available, string Message);
    private sealed record RawRecord(
        string MapName,
        string PlayerKey,
        string PlayerName,
        int TimerTicks,
        string FormattedTime,
        int Completions,
        DateTimeOffset? AchievedAt);

    private sealed class SharpTimerJsonRecord
    {
        public string? PlayerName { get; init; }
        public string? SteamId { get; init; }
        public string? MapName { get; init; }
        public int TimerTicks { get; init; }
        public int Completions { get; init; }
    }
}
