using System.Threading.Channels;
using DKay.GameServerDock.Application.Abstractions;

namespace DKay.GameServerDock.Infrastructure;

public sealed class ServerWorkQueue : IServerWorkQueue
{
    private readonly Channel<ServerWorkItem> _queue = Channel.CreateBounded<ServerWorkItem>(
        new BoundedChannelOptions(100)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });

    public ValueTask EnqueueAsync(ServerWorkItem item, CancellationToken cancellationToken) =>
        _queue.Writer.WriteAsync(item, cancellationToken);

    public ValueTask<ServerWorkItem> DequeueAsync(CancellationToken cancellationToken) =>
        _queue.Reader.ReadAsync(cancellationToken);
}

