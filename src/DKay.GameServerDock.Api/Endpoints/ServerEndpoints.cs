using System.Text.Json;
using DKay.GameServerDock.Application.Abstractions;
using DKay.GameServerDock.Application.Models;
using DKay.GameServerDock.Application.Services;
using DKay.GameServerDock.Domain;

namespace DKay.GameServerDock.Api.Endpoints;

public static class ServerEndpoints
{
    public static IEndpointRouteBuilder MapServerEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/host", (IHostMetricsProvider host, CancellationToken token) => host.GetSnapshotAsync(token));
        endpoints.MapGet("/api/game-templates", (IGameModuleRegistry modules) => modules.GetTemplates());
        endpoints.MapGet("/api/activity", async (IServerRepository servers, int? take, CancellationToken token) =>
            Results.Ok(await servers.GetEventsAsync(null, take ?? 100, token)));

        var group = endpoints.MapGroup("/api/servers");
        group.MapGet("/", ListAsync);
        group.MapGet("/{id:guid}", GetAsync);
        group.MapPost("/", CreateAsync);
        group.MapPut("/{id:guid}", UpdateAsync);
        group.MapPost("/{id:guid}/start", StartAsync);
        group.MapPost("/{id:guid}/stop", (Guid id, ServerOrchestrator orchestrator, CancellationToken token) =>
            orchestrator.StopAsync(id, false, token));
        group.MapPost("/{id:guid}/kill", (Guid id, ServerOrchestrator orchestrator, CancellationToken token) =>
            orchestrator.StopAsync(id, true, token));
        group.MapPost("/{id:guid}/restart", (Guid id, ServerOrchestrator orchestrator, CancellationToken token) =>
            orchestrator.RestartAsync(id, token));
        group.MapPost("/{id:guid}/update", QueueUpdateAsync);
        group.MapPost("/{id:guid}/command", SendCommandAsync);
        group.MapGet("/{id:guid}/players", async (Guid id, ServerOrchestrator orchestrator, CancellationToken token) =>
            Results.Ok((await orchestrator.GetRuntimeStatusAsync(id, token)).Players));
        group.MapGet("/{id:guid}/logs", async (Guid id, IServerRepository servers, int? take, CancellationToken token) =>
            Results.Ok(await servers.GetEventsAsync(id, take ?? 300, token)));

        return endpoints;
    }

    private static async Task<IResult> ListAsync(
        IServerRepository servers,
        IProcessSupervisor processes,
        IGameModuleRegistry modules,
        CancellationToken cancellationToken)
    {
        var items = await servers.ListAsync(cancellationToken);
        return Results.Ok(items.Select(server => ToResponse(server, processes.GetSnapshot(server.Id), modules)));
    }

    private static async Task<IResult> GetAsync(
        Guid id,
        ServerOrchestrator orchestrator,
        IGameModuleRegistry modules,
        CancellationToken cancellationToken)
    {
        var runtime = await orchestrator.GetRuntimeStatusAsync(id, cancellationToken);
        return Results.Ok(ToResponse(runtime.Server, runtime.Process, modules, runtime.Players, runtime.CurrentMap));
    }

    private static async Task<IResult> CreateAsync(
        CreateServerRequest request,
        ServerOrchestrator orchestrator,
        IServerWorkQueue queue,
        IGameModuleRegistry modules,
        CancellationToken cancellationToken)
    {
        var server = await orchestrator.CreateAsync(request, cancellationToken);
        await queue.EnqueueAsync(new ServerWorkItem(server.Id, ServerWorkKind.Install), cancellationToken);
        return Results.Accepted($"/api/servers/{server.Id}", ToResponse(server, new ProcessSnapshot(false, null, null, null, null, 0, 0), modules));
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        UpdateServerRequest request,
        ServerOrchestrator orchestrator,
        CancellationToken cancellationToken)
    {
        await orchestrator.UpdateSettingsAsync(id, request, cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> StartAsync(
        Guid id,
        ServerOrchestrator orchestrator,
        CancellationToken cancellationToken) => Results.Ok(await orchestrator.StartAsync(id, cancellationToken));

    private static async Task<IResult> QueueUpdateAsync(
        Guid id,
        IServerWorkQueue queue,
        CancellationToken cancellationToken)
    {
        await queue.EnqueueAsync(new ServerWorkItem(id, ServerWorkKind.Update), cancellationToken);
        return Results.Accepted();
    }

    private static async Task<IResult> SendCommandAsync(
        Guid id,
        ConsoleCommandRequest request,
        ServerOrchestrator orchestrator,
        CancellationToken cancellationToken)
    {
        await orchestrator.SendCommandAsync(id, request.Command, cancellationToken);
        return Results.NoContent();
    }

    private static object ToResponse(
        GameServerInstance server,
        ProcessSnapshot process,
        IGameModuleRegistry modules,
        IReadOnlyList<PlayerInfo>? players = null,
        string? currentMap = null)
    {
        var module = modules.GetRequired(server.TemplateId);
        var secretKeys = module.Descriptor.Settings.Where(setting => setting.Secret).Select(setting => setting.Key).ToHashSet();
        var settings = JsonSerializer.Deserialize<Dictionary<string, string>>(server.SettingsJson) ?? [];
        foreach (var key in secretKeys)
        {
            settings.Remove(key);
        }

        return new
        {
            server.Id,
            server.Name,
            server.TemplateId,
            TemplateName = module.Descriptor.Name,
            TemplateIcon = module.Descriptor.Icon,
            server.Version,
            server.Port,
            server.QueryPort,
            server.RconPort,
            server.RamLimitMb,
            server.Autostart,
            server.AutoRestart,
            Status = server.Status.ToString(),
            server.ProcessId,
            server.ExitCode,
            server.LastError,
            server.CreatedAt,
            server.UpdatedAt,
            server.StartedAt,
            Settings = settings,
            Process = process,
            Players = players ?? [],
            CurrentMap = currentMap,
            Capabilities = module.Descriptor.Capabilities.ToString()
        };
    }

    private sealed record ConsoleCommandRequest(string Command);
}

