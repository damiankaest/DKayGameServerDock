using System.Diagnostics;
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
        await reportProgress(new InstallationProgress(5, "runtime", "SteamCMD found."), cancellationToken);

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
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };
        foreach (var argument in arguments.Build())
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.Start();
        var outputTask = PumpAsync(process.StandardOutput, reportProgress, cancellationToken);
        var errorTask = PumpAsync(process.StandardError, reportProgress, cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        await Task.WhenAll(outputTask, errorTask);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"SteamCMD exited with code {process.ExitCode}.");
        }

        await reportProgress(new InstallationProgress(100, "complete", "Steam installation completed."), cancellationToken);
    }

    private static async Task PumpAsync(
        StreamReader reader,
        Func<InstallationProgress, CancellationToken, Task> reportProgress,
        CancellationToken cancellationToken)
    {
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            var percent = line.Contains("Success!", StringComparison.OrdinalIgnoreCase) ? 95 : 45;
            await reportProgress(new InstallationProgress(percent, "download", line), cancellationToken);
        }
    }
}

