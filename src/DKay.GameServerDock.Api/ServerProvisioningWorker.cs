using DKay.GameServerDock.Application.Abstractions;
using DKay.GameServerDock.Application.Services;

namespace DKay.GameServerDock.Api;

public sealed class ServerProvisioningWorker(
    IServerWorkQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<ServerProvisioningWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            ServerWorkItem item;
            try
            {
                item = await queue.DequeueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }

            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var orchestrator = scope.ServiceProvider.GetRequiredService<ServerOrchestrator>();
                if (item.Kind == ServerWorkKind.Install)
                {
                    await orchestrator.InstallAsync(item.ServerId, stoppingToken);
                }
                else if (item.Kind == ServerWorkKind.Update)
                {
                    await orchestrator.UpdateAsync(item.ServerId, stoppingToken);
                }
                else
                {
                    var modes = scope.ServiceProvider.GetRequiredService<Cs2ModeService>();
                    var packageIds = (item.Argument ?? throw new InvalidOperationException("The CS2 package id is missing."))
                        .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    if (packageIds.Length == 0)
                    {
                        throw new InvalidOperationException("The CS2 package stack is empty.");
                    }

                    foreach (var packageId in packageIds)
                    {
                        await modes.InstallPackageAsync(item.ServerId, packageId, stoppingToken);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Background work for server {ServerId} failed.", item.ServerId);
            }
        }
    }
}
