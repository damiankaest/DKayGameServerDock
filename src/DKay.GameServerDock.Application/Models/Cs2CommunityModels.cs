namespace DKay.GameServerDock.Application.Models;

public sealed record Cs2CommunityRecord(
    int Rank,
    string PlayerName,
    int TimerTicks,
    string FormattedTime,
    int Completions,
    DateTimeOffset? AchievedAt);

public sealed record Cs2CommunityMapStats(
    string ProfileId,
    string MapName,
    string Title,
    string? WorkshopId,
    string? PreviewUrl,
    string PresetName,
    string WorkshopInstallState,
    bool Active,
    int PlayCount,
    DateTimeOffset? LastPlayedAt,
    int UniqueRunners,
    int TotalCompletions,
    IReadOnlyList<Cs2CommunityRecord> Records);

public sealed record Cs2CommunityStats(
    IReadOnlyList<Cs2CommunityMapStats> Maps,
    bool RecordsAvailable,
    string RecordsMessage);
