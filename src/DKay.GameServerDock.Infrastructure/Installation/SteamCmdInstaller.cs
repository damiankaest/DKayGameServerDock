using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using DKay.GameServerDock.Application.Abstractions;
using DKay.GameServerDock.Application.Models;
using DKay.GameServerDock.Application.Services;
using DKay.GameServerDock.Domain;

namespace DKay.GameServerDock.Infrastructure.Installation;

public sealed class SteamCmdInstaller(DockOptions options, int appId) : IGameInstaller
{
    public Task InstallAsync(
        GameServerInstance server,
        Func<InstallationProgress, CancellationToken, Task> reportProgress,
        CancellationToken cancellationToken) => RunSteamCmdAsync(server, reportProgress, true, cancellationToken);

    public Task UpdateAsync(
        GameServerInstance server,
        Func<InstallationProgress, CancellationToken, Task> reportProgress,
        CancellationToken cancellationToken) => RunSteamCmdAsync(server, reportProgress, false, cancellationToken);

    private async Task RunSteamCmdAsync(
        GameServerInstance server,
        Func<InstallationProgress, CancellationToken, Task> reportProgress,
        bool validate,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.SteamCmdPath) || !File.Exists(options.SteamCmdPath))
        {
            throw new InvalidOperationException(
                "SteamCMD was not found. Configure Dock:SteamCmdPath or DGS_STEAMCMD_PATH before installing CS2.");
        }

        Directory.CreateDirectory(server.InstallDirectory);
        await reportProgress(
            new InstallationProgress(5, "runtime", $"SteamCMD found at '{options.SteamCmdPath}'."),
            cancellationToken);

        var arguments = new CommandArgumentBuilder()
            .AddPair("+force_install_dir", server.InstallDirectory)
            .AddPair("+login", "anonymous")
            .AddPair("+app_update", appId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (validate)
        {
            arguments.Add("validate");
        }

        arguments.Add("+quit");

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = options.SteamCmdPath,
                WorkingDirectory = Path.GetDirectoryName(options.SteamCmdPath)!,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };
        foreach (var argument in arguments.Build())
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        if (!process.Start())
        {
            throw new InvalidOperationException("Windows refused to start SteamCMD.");
        }

        var recentOutput = new ConcurrentQueue<string>();
        await reportProgress(
            new InstallationProgress(10, "launch", $"SteamCMD process {process.Id} started for app {appId}."),
            cancellationToken);
        var outputTask = PumpAsync(process.StandardOutput, "stdout", recentOutput, reportProgress, cancellationToken);
        var errorTask = PumpAsync(process.StandardError, "stderr", recentOutput, reportProgress, cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken)
                .WaitAsync(TimeSpan.FromMinutes(45), cancellationToken);
        }
        catch (TimeoutException)
        {
            await reportProgress(
                new InstallationProgress(90, "timeout", "SteamCMD did not exit within 45 minutes; stopping it."),
                CancellationToken.None);
            await TerminateAsync(process);
            await DrainAsync(outputTask, errorTask);
            throw new TimeoutException($"SteamCMD timed out after 45 minutes.{FormatRecentOutput(recentOutput)}");
        }
        catch (OperationCanceledException)
        {
            await TerminateAsync(process);
            await DrainAsync(outputTask, errorTask);
            throw;
        }

        await Task.WhenAll(outputTask, errorTask);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"SteamCMD exited with code {process.ExitCode}.{FormatRecentOutput(recentOutput)}");
        }

        await reportProgress(new InstallationProgress(100, "complete", "Steam installation completed."), cancellationToken);
    }

    private static async Task PumpAsync(
        StreamReader reader,
        string stream,
        ConcurrentQueue<string> recentOutput,
        Func<InstallationProgress, CancellationToken, Task> reportProgress,
        CancellationToken cancellationToken)
    {
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var normalized = line.Length <= 2000 ? line.Trim() : line[..2000].Trim();
            recentOutput.Enqueue($"[{stream}] {normalized}");
            while (recentOutput.Count > 30 && recentOutput.TryDequeue(out _))
            {
            }

            await reportProgress(
                new InstallationProgress(GetProgress(normalized), stream == "stderr" ? "steamcmd-error" : "download", $"SteamCMD {stream}: {normalized}"),
                cancellationToken);
        }
    }

    private static int GetProgress(string line)
    {
        if (line.Contains("Success!", StringComparison.OrdinalIgnoreCase))
        {
            return 95;
        }

        var match = Regex.Match(line, @"progress:\s*(\d+(?:\.\d+)?)", RegexOptions.IgnoreCase);
        return match.Success && double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var progress)
            ? Math.Clamp(15 + (int)Math.Round(progress * 0.75), 15, 90)
            : 15;
    }

    private static string FormatRecentOutput(ConcurrentQueue<string> output)
    {
        var tail = output.ToArray().TakeLast(8).ToArray();
        return tail.Length == 0
            ? string.Empty
            : $" Recent output: {string.Join(" | ", tail)}";
    }

    private static async Task TerminateAsync(Process process)
    {
        if (process.HasExited)
        {
            return;
        }

        try
        {
            await process.StandardInput.WriteLineAsync("quit");
            await process.StandardInput.FlushAsync();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
        }
    }

    private static async Task DrainAsync(params Task[] pumps)
    {
        try
        {
            await Task.WhenAll(pumps);
        }
        catch (OperationCanceledException)
        {
        }
    }
}
