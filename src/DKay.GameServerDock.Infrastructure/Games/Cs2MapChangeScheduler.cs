using System.Collections.Concurrent;
using System.Text.Json;
using DKay.GameServerDock.Application.Abstractions;
using DKay.GameServerDock.Application.Models;
using DKay.GameServerDock.Domain;

namespace DKay.GameServerDock.Infrastructure.Games;

public sealed class Cs2MapChangeScheduler(
    Cs2RconClient rcon,
    ICs2ModeManager modes,
    IProcessSupervisor processes,
    IServerEventSink events,
    IClock clock) : ICs2MapChangeScheduler, IDisposable
{
    private static readonly int[] CountdownMilestones = [120, 60, 30, 10, 5, 4, 3, 2, 1];
    private readonly ConcurrentDictionary<Guid, ScheduledChange> _changes = [];

    public Cs2MapChangeState GetState(Guid serverId) =>
        _changes.TryGetValue(serverId, out var change)
            ? ToState(change)
            : IdleState();

    public async Task<Cs2MapChangeState> ScheduleAsync(
        GameServerInstance server,
        Cs2ModeProfile profile,
        TimeSpan delay,
        CancellationToken cancellationToken)
    {
        if (!processes.GetSnapshot(server.Id).IsRunning)
        {
            throw new InvalidOperationException("Start the CS2 server before scheduling a map change.");
        }

        if (delay < TimeSpan.Zero || delay > TimeSpan.FromMinutes(10))
        {
            throw new ArgumentOutOfRangeException(nameof(delay), "The map-change delay must be between 0 and 10 minutes.");
        }

        if (_changes.TryRemove(server.Id, out var previous))
        {
            previous.Cancellation.Cancel();
        }

        var executeAt = clock.UtcNow.Add(delay);
        var change = new ScheduledChange(server, profile, executeAt, new CancellationTokenSource());
        _changes[server.Id] = change;
        try
        {
            await events.RecordAsync(
                ServerEvent.Create(
                    server.Id,
                    ServerEventType.ConfigurationChanged,
                    delay == TimeSpan.Zero
                        ? $"Live map change to {profile.MapName} requested."
                        : $"Map change to {profile.MapName} scheduled in {delay.TotalSeconds:0} seconds.",
                    clock.UtcNow),
                cancellationToken);
        }
        catch
        {
            _changes.TryRemove(server.Id, out _);
            change.Cancellation.Cancel();
            throw;
        }

        _ = RunAsync(change);
        return ToState(change);
    }

    public async Task<Cs2MapChangeState> CancelAsync(
        GameServerInstance server,
        CancellationToken cancellationToken)
    {
        if (!_changes.TryRemove(server.Id, out var change))
        {
            return IdleState("There is no pending map change to cancel.");
        }

        change.Cancellation.Cancel();
        if (processes.GetSnapshot(server.Id).IsRunning)
        {
            try
            {
                await rcon.ExecuteAsync(server, "say [DKay] Scheduled map change cancelled.", cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Cancellation itself succeeded even if the optional in-game announcement did not.
            }
        }

        await events.RecordAsync(
            ServerEvent.Create(server.Id, ServerEventType.ConfigurationChanged, "Scheduled CS2 map change cancelled.", clock.UtcNow),
            cancellationToken);
        return IdleState("Scheduled map change cancelled.");
    }

    public void Dispose()
    {
        foreach (var change in _changes.Values)
        {
            change.Cancellation.Cancel();
        }

        _changes.Clear();
    }

    private async Task RunAsync(ScheduledChange change)
    {
        var token = change.Cancellation.Token;
        try
        {
            var initialSeconds = RemainingSeconds(change);
            if (initialSeconds > 0)
            {
                await AnnounceAsync(change, initialSeconds, token);
            }

            foreach (var milestone in CountdownMilestones.Where(value => value < initialSeconds))
            {
                await DelayUntilAsync(change.ExecuteAt.AddSeconds(-milestone), token);
                await AnnounceAsync(change, milestone, token);
            }

            await DelayUntilAsync(change.ExecuteAt, token);
            change.Status = "changing";
            change.Message = $"Changing to {change.Profile.MapName} and waiting for CS2 to activate it…";
            await modes.ActivateProfileAsync(change.Server, change.Profile.Id, token);

            var cfgProfileId = change.Profile.WorkshopId is null
                ? change.Profile.MapName.ToLowerInvariant()
                : $"workshop-{change.Profile.WorkshopId}";
            var mapCommand = change.Profile.WorkshopId is null
                ? $"changelevel {change.Profile.MapName}"
                : $"host_workshop_map {change.Profile.WorkshopId}";
            await rcon.ExecuteAsync(
                change.Server,
                $"say [DKay] Changing map to {change.Profile.MapName} now.; exec dkay/maps/{cfgProfileId}.cfg; {mapCommand}",
                token,
                TimeSpan.FromSeconds(2));

            if (!await WaitForTargetMapAsync(change, cfgProfileId, token))
            {
                change.Status = "failed";
                change.Message = $"CS2 accepted the command, but {change.Profile.MapName} was not confirmed within ten minutes.";
                await RecordResultAsync(change, change.Message, token);
                return;
            }

            change.Status = "completed";
            change.Message = $"{change.Profile.MapName} is live. Its saved preset and live settings were applied.";
            await events.RecordAsync(
                ServerEvent.Create(
                    change.Server.Id,
                    ServerEventType.MapChanged,
                    change.Message,
                    clock.UtcNow,
                    JsonSerializer.Serialize(new
                    {
                        profileId = change.Profile.Id,
                        mapName = change.Profile.MapName,
                        workshopId = change.Profile.WorkshopId
                    })),
                token);
            await Task.Delay(TimeSpan.FromSeconds(15), token);
            RemoveIfCurrent(change);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Replaced or explicitly cancelled by the administrator.
        }
        catch (Exception exception)
        {
            change.Status = "failed";
            change.Message = $"Map change failed: {exception.Message}";
            try
            {
                await RecordResultAsync(change, change.Message, CancellationToken.None);
            }
            catch
            {
                // Keep the failure visible through GetState even when activity persistence is unavailable.
            }
        }
    }

    private async Task<bool> WaitForTargetMapAsync(
        ScheduledChange change,
        string cfgProfileId,
        CancellationToken cancellationToken)
    {
        var timeoutAt = clock.UtcNow.AddMinutes(10);
        while (clock.UtcNow < timeoutAt)
        {
            await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
            try
            {
                var status = Cs2GameServerAdapter.ParseStatus(await rcon.ExecuteAsync(
                    change.Server,
                    "status",
                    cancellationToken,
                    TimeSpan.FromSeconds(2)));
                if (!string.Equals(status.Map, change.Profile.MapName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                await rcon.ExecuteAsync(
                    change.Server,
                    $"exec dkay/maps/{cfgProfileId}.cfg; exec dkay-live.cfg",
                    cancellationToken,
                    TimeSpan.FromSeconds(2));
                return true;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // CS2 can briefly close the command channel while a level is activated.
            }
        }

        return false;
    }

    private async Task AnnounceAsync(ScheduledChange change, int seconds, CancellationToken cancellationToken)
    {
        change.Message = $"{change.Profile.MapName} starts in {seconds} second{(seconds == 1 ? string.Empty : "s")}.";
        try
        {
            await rcon.ExecuteAsync(
                change.Server,
                $"say [DKay] Next map: {change.Profile.MapName} in {seconds} second{(seconds == 1 ? string.Empty : "s")}.",
                cancellationToken,
                TimeSpan.FromSeconds(2));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A missed chat announcement must not cancel the administrator's scheduled change.
        }
    }

    private async Task DelayUntilAsync(DateTimeOffset target, CancellationToken cancellationToken)
    {
        var delay = target - clock.UtcNow;
        if (delay > TimeSpan.Zero)
        {
            await Task.Delay(delay, cancellationToken);
        }
    }

    private async Task RecordResultAsync(ScheduledChange change, string message, CancellationToken cancellationToken) =>
        await events.RecordAsync(
            ServerEvent.Create(change.Server.Id, ServerEventType.ConfigurationChanged, message, clock.UtcNow),
            cancellationToken);

    private void RemoveIfCurrent(ScheduledChange change)
    {
        if (_changes.TryGetValue(change.Server.Id, out var current) && ReferenceEquals(current, change))
        {
            _changes.TryRemove(change.Server.Id, out _);
        }
    }

    private int RemainingSeconds(ScheduledChange change) =>
        Math.Max(0, (int)Math.Ceiling((change.ExecuteAt - clock.UtcNow).TotalSeconds));

    private Cs2MapChangeState ToState(ScheduledChange change) => new(
        change.Status,
        change.Profile.Id,
        change.Profile.MapName,
        change.Profile.WorkshopId,
        change.ExecuteAt,
        RemainingSeconds(change),
        change.Message);

    private static Cs2MapChangeState IdleState(string message = "No map change is scheduled.") =>
        new("idle", null, null, null, null, 0, message);

    private sealed class ScheduledChange(
        GameServerInstance server,
        Cs2ModeProfile profile,
        DateTimeOffset executeAt,
        CancellationTokenSource cancellation)
    {
        public GameServerInstance Server { get; } = server;
        public Cs2ModeProfile Profile { get; } = profile;
        public DateTimeOffset ExecuteAt { get; } = executeAt;
        public CancellationTokenSource Cancellation { get; } = cancellation;
        public string Status { get; set; } = "scheduled";
        public string Message { get; set; } = $"{profile.MapName} is queued as the next map.";
    }
}
