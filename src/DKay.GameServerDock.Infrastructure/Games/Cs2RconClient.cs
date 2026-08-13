using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using DKay.GameServerDock.Domain;

namespace DKay.GameServerDock.Infrastructure.Games;

public sealed class Cs2RconClient(Cs2RuntimeProvisioner runtime)
{
    private const int AuthPacketType = 3;
    private const int AuthResponsePacketType = 2;
    private const int ExecutePacketType = 2;
    private const int MaximumPacketSize = 1024 * 1024;

    public async Task<string> ExecuteAsync(
        GameServerInstance server,
        string command,
        CancellationToken cancellationToken,
        TimeSpan? listenerWait = null)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentException.ThrowIfNullOrWhiteSpace(command);

        var wait = listenerWait ?? TimeSpan.Zero;
        if (wait < TimeSpan.Zero || wait > TimeSpan.FromSeconds(30))
        {
            throw new ArgumentOutOfRangeException(nameof(listenerWait));
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(wait + TimeSpan.FromSeconds(8));
        var elapsed = Stopwatch.StartNew();
        SocketException? connectionFailure = null;
        try
        {
            while (true)
            {
                try
                {
                    return await ExecuteOnceAsync(
                        server,
                        command,
                        runtime.GetRconPassword(server),
                        timeout.Token);
                }
                catch (SocketException exception) when (
                    IsListenerUnavailable(exception) && elapsed.Elapsed < wait)
                {
                    connectionFailure = exception;
                    await Task.Delay(TimeSpan.FromMilliseconds(500), timeout.Token);
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            if (connectionFailure is not null)
            {
                throw CreateConnectionException(server, wait, connectionFailure);
            }

            throw new TimeoutException(
                $"CS2 did not answer the local RCON command on port {server.Port} within {(wait + TimeSpan.FromSeconds(8)).TotalSeconds:0} seconds.");
        }
        catch (SocketException exception)
        {
            throw CreateConnectionException(server, wait, exception);
        }
    }

    private static async Task<string> ExecuteOnceAsync(
        GameServerInstance server,
        string command,
        string password,
        CancellationToken cancellationToken)
    {
        using var client = new TcpClient { NoDelay = true };
        await client.ConnectAsync(IPAddress.Loopback, server.Port, cancellationToken);
        await using var stream = client.GetStream();
        await AuthenticateAsync(stream, password, cancellationToken);

        var requestId = RandomNumberGenerator.GetInt32(1, int.MaxValue);
        await WritePacketAsync(stream, requestId, ExecutePacketType, command, cancellationToken);
        var response = await ReadPacketAsync(stream, cancellationToken);
        if (response.Id != requestId)
        {
            throw new InvalidOperationException("CS2 returned an unexpected RCON response identifier.");
        }

        return response.Body;
    }

    private static bool IsListenerUnavailable(SocketException exception) => exception.SocketErrorCode is
        SocketError.ConnectionRefused or SocketError.TimedOut or SocketError.HostUnreachable;

    private static InvalidOperationException CreateConnectionException(
        GameServerInstance server,
        TimeSpan listenerWait,
        SocketException exception)
    {
        var timing = listenerWait > TimeSpan.Zero
            ? $" after waiting {listenerWait.TotalSeconds:0} seconds"
            : string.Empty;
        return new InvalidOperationException(
            $"CS2 is running, but its local RCON listener did not open on 127.0.0.1:{server.Port}{timing}. " +
            "Restart the server once so the managed autoexec configuration is loaded. " +
            $"Socket error: {exception.Message}",
            exception);
    }

    private static async Task AuthenticateAsync(NetworkStream stream, string password, CancellationToken cancellationToken)
    {
        var requestId = RandomNumberGenerator.GetInt32(1, int.MaxValue);
        await WritePacketAsync(stream, requestId, AuthPacketType, password, cancellationToken);

        for (var responseNumber = 0; responseNumber < 3; responseNumber++)
        {
            var response = await ReadPacketAsync(stream, cancellationToken);
            if (response.Type != AuthResponsePacketType)
            {
                continue;
            }

            if (response.Id == -1)
            {
                throw new InvalidOperationException("CS2 rejected the managed RCON password.");
            }

            if (response.Id != requestId)
            {
                throw new InvalidOperationException("CS2 returned an unexpected RCON authentication response.");
            }

            return;
        }

        throw new InvalidOperationException("CS2 did not return an RCON authentication response.");
    }

    private static async Task WritePacketAsync(
        NetworkStream stream,
        int id,
        int type,
        string body,
        CancellationToken cancellationToken)
    {
        var bodyBytes = Encoding.UTF8.GetBytes(body);
        var payloadSize = 4 + 4 + bodyBytes.Length + 1 + 1;
        var packet = new byte[4 + payloadSize];
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(0, 4), payloadSize);
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(4, 4), id);
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(8, 4), type);
        bodyBytes.CopyTo(packet.AsSpan(12));
        await stream.WriteAsync(packet, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private static async Task<RconPacket> ReadPacketAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var sizeBytes = new byte[4];
        await stream.ReadExactlyAsync(sizeBytes, cancellationToken);
        var size = BinaryPrimitives.ReadInt32LittleEndian(sizeBytes);
        if (size is < 10 or > MaximumPacketSize)
        {
            throw new InvalidOperationException($"CS2 returned an invalid RCON packet size: {size}.");
        }

        var payload = new byte[size];
        await stream.ReadExactlyAsync(payload, cancellationToken);
        var id = BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(0, 4));
        var type = BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(4, 4));
        var bodyLength = Array.IndexOf(payload, (byte)0, 8) - 8;
        if (bodyLength < 0)
        {
            throw new InvalidOperationException("CS2 returned an unterminated RCON response.");
        }

        return new RconPacket(id, type, Encoding.UTF8.GetString(payload, 8, bodyLength));
    }

    private sealed record RconPacket(int Id, int Type, string Body);
}
