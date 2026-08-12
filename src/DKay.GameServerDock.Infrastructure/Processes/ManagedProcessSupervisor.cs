using System.Collections.Concurrent;
using System.Diagnostics;
using DKay.GameServerDock.Application.Abstractions;
using DKay.GameServerDock.Application.Models;
using DKay.GameServerDock.Domain;

namespace DKay.GameServerDock.Infrastructure.Processes;

public sealed class ManagedProcessSupervisor(
    IServerEventSink events,
    IServerRuntimeStateStore runtimeState,
    IClock clock) : IProcessSupervisor, IDisposable
{
    private readonly ConcurrentDictionary<Guid, ManagedProcess> _processes = new();

    public Task<ProcessSnapshot> StartAsync(
        GameServerInstance server,
        ServerLaunchSpec launchSpec,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_processes.TryGetValue(server.Id, out var existing) && !existing.Process.HasExited)
        {
            throw new InvalidOperationException("The server process is already running.");
        }

        if (Path.IsPathRooted(launchSpec.FileName) && !File.Exists(launchSpec.FileName))
        {
            throw new FileNotFoundException("The server executable was not found.", launchSpec.FileName);
        }

        Directory.CreateDirectory(launchSpec.WorkingDirectory);
        var startInfo = new ProcessStartInfo
        {
            FileName = launchSpec.FileName,
            WorkingDirectory = launchSpec.WorkingDirectory,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in launchSpec.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        foreach (var variable in launchSpec.Environment)
        {
            startInfo.Environment[variable.Key] = variable.Value;
        }

        var process = new Process { StartInfo = startInfo };
        process.OutputDataReceived += (_, args) => QueueOutput(server.Id, "stdout", args.Data);
        process.ErrorDataReceived += (_, args) => QueueOutput(server.Id, "stderr", args.Data);

        if (!process.Start())
        {
            process.Dispose();
            throw new InvalidOperationException("The operating system refused to start the server process.");
        }

        var managed = new ManagedProcess(process, clock.UtcNow);
        if (!_processes.TryAdd(server.Id, managed))
        {
            process.Kill(true);
            process.Dispose();
            throw new InvalidOperationException("A process is already registered for this server.");
        }

        process.Exited += (_, _) => _ = HandleExitAsync(server.Id, process);
        process.EnableRaisingEvents = true;
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return Task.FromResult(CreateSnapshot(managed));
    }

    public async Task<ProcessSnapshot> StopAsync(
        GameServerInstance server,
        string gracefulCommand,
        bool force,
        CancellationToken cancellationToken)
    {
        if (!_processes.TryGetValue(server.Id, out var managed) || managed.Process.HasExited)
        {
            return EmptySnapshot(managed?.Process.ExitCode);
        }

        if (force)
        {
            managed.Process.Kill(true);
        }
        else
        {
            await managed.Process.StandardInput.WriteLineAsync(gracefulCommand.AsMemory(), cancellationToken);
            await managed.Process.StandardInput.FlushAsync(cancellationToken);
            try
            {
                await managed.Process.WaitForExitAsync(cancellationToken).WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
            }
            catch (TimeoutException)
            {
                managed.Process.Kill(true);
            }
        }

        await managed.Process.WaitForExitAsync(cancellationToken);
        return new ProcessSnapshot(
            false,
            null,
            managed.Process.ExitCode,
            managed.StartedAt,
            clock.UtcNow - managed.StartedAt,
            0,
            0);
    }

    public async Task SendCommandAsync(Guid serverId, string command, CancellationToken cancellationToken)
    {
        if (!_processes.TryGetValue(serverId, out var managed) || managed.Process.HasExited)
        {
            throw new InvalidOperationException("The server process is not running.");
        }

        await managed.Process.StandardInput.WriteLineAsync(command.AsMemory(), cancellationToken);
        await managed.Process.StandardInput.FlushAsync(cancellationToken);
    }

    public ProcessSnapshot GetSnapshot(Guid serverId) =>
        _processes.TryGetValue(serverId, out var managed) && !managed.Process.HasExited
            ? CreateSnapshot(managed)
            : EmptySnapshot(managed?.Process.ExitCode);

    private ProcessSnapshot CreateSnapshot(ManagedProcess managed)
    {
        lock (managed.SyncRoot)
        {
            var now = clock.UtcNow;
            var totalProcessorTime = managed.Process.TotalProcessorTime;
            var elapsed = now - managed.LastSampleAt;
            var processorDelta = totalProcessorTime - managed.LastProcessorTime;
            var cpu = elapsed.TotalMilliseconds <= 0
                ? 0
                : processorDelta.TotalMilliseconds / elapsed.TotalMilliseconds / Environment.ProcessorCount * 100;
            managed.LastSampleAt = now;
            managed.LastProcessorTime = totalProcessorTime;

            return new ProcessSnapshot(
                true,
                managed.Process.Id,
                null,
                managed.StartedAt,
                now - managed.StartedAt,
                Math.Clamp(cpu, 0, 100),
                managed.Process.WorkingSet64);
        }
    }

    private static ProcessSnapshot EmptySnapshot(int? exitCode = null) =>
        new(false, null, exitCode, null, null, 0, 0);

    private void QueueOutput(Guid serverId, string stream, string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        _ = RecordOutputAsync(serverId, stream, line);
    }

    private Task RecordOutputAsync(Guid serverId, string stream, string line) =>
        events.RecordAsync(
            ServerEvent.Create(
                serverId,
                ServerEventType.ConsoleOutput,
                line.Length <= 4000 ? line : line[..4000],
                clock.UtcNow,
                $"{{\"stream\":\"{stream}\"}}"),
            CancellationToken.None);

    private async Task HandleExitAsync(Guid serverId, Process process)
    {
        try
        {
            await runtimeState.MarkExitedAsync(serverId, process.ExitCode, CancellationToken.None);
        }
        finally
        {
            if (_processes.TryRemove(serverId, out var managed))
            {
                managed.Process.Dispose();
            }
        }
    }

    public void Dispose()
    {
        foreach (var managed in _processes.Values)
        {
            managed.Process.Dispose();
        }

        _processes.Clear();
    }

    private sealed class ManagedProcess(Process process, DateTimeOffset startedAt)
    {
        public Process Process { get; } = process;
        public DateTimeOffset StartedAt { get; } = startedAt;
        public object SyncRoot { get; } = new();
        public DateTimeOffset LastSampleAt { get; set; } = startedAt;
        public TimeSpan LastProcessorTime { get; set; }
    }
}
