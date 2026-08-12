using DKay.GameServerDock.Domain;

namespace DKay.GameServerDock.Application.Services;

public static class ServerStateMachine
{
    private static readonly IReadOnlyDictionary<ServerStatus, ServerStatus[]> AllowedTransitions =
        new Dictionary<ServerStatus, ServerStatus[]>
        {
            [ServerStatus.Installing] = [ServerStatus.Stopped, ServerStatus.Error],
            [ServerStatus.Stopped] = [ServerStatus.Starting, ServerStatus.Updating, ServerStatus.Installing],
            [ServerStatus.Starting] = [ServerStatus.Running, ServerStatus.Crashed, ServerStatus.Error, ServerStatus.Stopped],
            [ServerStatus.Running] = [ServerStatus.Stopping, ServerStatus.Crashed, ServerStatus.Error],
            [ServerStatus.Stopping] = [ServerStatus.Stopped, ServerStatus.Crashed, ServerStatus.Error],
            [ServerStatus.Updating] = [ServerStatus.Stopped, ServerStatus.Error],
            [ServerStatus.Crashed] = [ServerStatus.Starting, ServerStatus.Stopped, ServerStatus.Updating],
            [ServerStatus.Error] = [ServerStatus.Installing, ServerStatus.Stopped, ServerStatus.Updating]
        };

    public static bool CanTransition(ServerStatus from, ServerStatus to) =>
        from == to || AllowedTransitions.TryGetValue(from, out var allowed) && allowed.Contains(to);

    public static void EnsureCanTransition(ServerStatus from, ServerStatus to)
    {
        if (!CanTransition(from, to))
        {
            throw new InvalidOperationException($"Cannot transition a game server from {from} to {to}.");
        }
    }
}

