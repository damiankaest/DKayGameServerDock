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
                else
                {
                    await orchestrator.UpdateAsync(item.ServerId, stoppingToken);
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
