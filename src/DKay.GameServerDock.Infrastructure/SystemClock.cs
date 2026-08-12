using DKay.GameServerDock.Application.Abstractions;

namespace DKay.GameServerDock.Infrastructure;

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

