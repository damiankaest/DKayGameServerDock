using System.Text.Json;
using System.Net;
using System.Net.Sockets;
using DKay.GameServerDock.Application.Abstractions;
using DKay.GameServerDock.Application.Models;
using DKay.GameServerDock.Application.Services;
using DKay.GameServerDock.Domain;
using DKay.GameServerDock.Infrastructure;
using Microsoft.AspNetCore.RateLimiting;

namespace DKay.GameServerDock.Api.Endpoints;

public static class ServerEndpoints
{
    public static IEndpointRouteBuilder MapServerEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/host", (IHostMetricsProvider host, CancellationToken token) => host.GetSnapshotAsync(token));
        endpoints.MapGet("/api/host/readiness", (IHostReadinessProvider readiness) => readiness.GetSnapshot());
        endpoints.MapGet("/api/game-templates", (IGameModuleRegistry modules) => modules.GetTemplates());
        endpoints.MapGet("/api/cs2/mode-presets", (Cs2ModeService modes) => Results.Ok(new
        {
            Presets = modes.Presets,
            Packages = modes.Packages
        }));
        endpoints.MapGet("/api/public/servers", PublicServersAsync)
            .AllowAnonymous()
            .RequireRateLimiting("public-guest");
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
        group.MapDelete("/{id:guid}", DeleteAsync);
        group.MapPut("/{id:guid}/publication", UpdatePublicationAsync);
        group.MapGet("/{id:guid}/cs2-mode", (Guid id, Cs2ModeService modes, CancellationToken token) =>
            modes.GetStateAsync(id, token));
        group.MapPut("/{id:guid}/cs2-mode", ApplyCs2ModeAsync);
        group.MapPost("/{id:guid}/cs2-packages/{packageId}/install", QueueCs2PackageAsync);
        group.MapGet("/{id:guid}/cs2-control", async (Guid id, Cs2LiveControlService controls, CancellationToken token) =>
            Results.Ok(await controls.GetStateAsync(id, token)));
        group.MapPut("/{id:guid}/cs2-control", async (
                Guid id,
                ApplyCs2LiveConfigurationRequest request,
                Cs2LiveControlService controls,
                CancellationToken token) =>
            Results.Ok(await controls.ApplyAsync(id, request, token)));
        group.MapPost("/{id:guid}/cs2-control/actions", async (
                Guid id,
                RunCs2QuickActionRequest request,
                Cs2LiveControlService controls,
                CancellationToken token) =>
            Results.Ok(await controls.RunActionAsync(id, request, token)));
        group.MapPut("/{id:guid}/cs2-control/gslt", async (
                Guid id,
                ConfigureCs2GsltRequest request,
                Cs2LiveControlService controls,
                CancellationToken token) =>
            Results.Ok(await controls.ConfigureGsltAsync(id, request, token)));
        group.MapPost("/{id:guid}/command", SendCommandAsync);
        group.MapPost("/{id:guid}/self-test", async (Guid id, ServerOrchestrator orchestrator, CancellationToken token) =>
            Results.Ok(await orchestrator.TestCommandChannelAsync(id, token)));
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
        DockOptions dockOptions,
        CancellationToken cancellationToken)
    {
        var items = await servers.ListAsync(cancellationToken);
        return Results.Ok(items.Select(server => ToResponse(server, processes.GetSnapshot(server.Id), modules, dockOptions)));
    }

    private static async Task<IResult> GetAsync(
        Guid id,
        ServerOrchestrator orchestrator,
        IGameModuleRegistry modules,
        DockOptions dockOptions,
        CancellationToken cancellationToken)
    {
        var runtime = await orchestrator.GetRuntimeStatusAsync(id, cancellationToken);
        return Results.Ok(ToResponse(runtime.Server, runtime.Process, modules, dockOptions, runtime.Players, runtime.CurrentMap));
    }

    private static async Task<IResult> CreateAsync(
        CreateServerRequest request,
        ServerOrchestrator orchestrator,
        IServerWorkQueue queue,
        IGameModuleRegistry modules,
        DockOptions dockOptions,
        CancellationToken cancellationToken)
    {
        var server = await orchestrator.CreateAsync(request, cancellationToken);
        await queue.EnqueueAsync(new ServerWorkItem(server.Id, ServerWorkKind.Install), cancellationToken);
        return Results.Accepted(
            $"/api/servers/{server.Id}",
            ToResponse(server, new ProcessSnapshot(false, null, null, null, null, 0, 0), modules, dockOptions));
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
        IServerRepository servers,
        IServerWorkQueue queue,
        CancellationToken cancellationToken)
    {
        var server = await servers.FindAsync(id, cancellationToken)
            ?? throw new KeyNotFoundException($"Server '{id}' was not found.");
        if (server.Status is not (ServerStatus.Stopped or ServerStatus.Crashed or ServerStatus.Error))
        {
            throw new InvalidOperationException($"Stop the server before updating it (current state: {server.Status}).");
        }

        await queue.EnqueueAsync(new ServerWorkItem(id, ServerWorkKind.Update), cancellationToken);
        return Results.Accepted();
    }

    private static async Task<IResult> DeleteAsync(
        Guid id,
        bool? deleteFiles,
        ServerOrchestrator orchestrator,
        CancellationToken cancellationToken)
    {
        await orchestrator.DeleteAsync(id, deleteFiles ?? true, cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> UpdatePublicationAsync(
        Guid id,
        UpdateServerPublicationRequest request,
        ServerOrchestrator orchestrator,
        DockOptions dockOptions,
        CancellationToken cancellationToken)
    {
        if (request.Published)
        {
            if (!dockOptions.PublicPortalEnabled)
            {
                throw new InvalidOperationException("Enable the public guest portal before publishing a server.");
            }

            if (!IsValidPublicHost(dockOptions.PublicHost))
            {
                throw new InvalidOperationException("Configure DGS_PUBLIC_HOST with a public DNS name or IP address first.");
            }
        }

        var publication = await orchestrator.UpdatePublicationAsync(id, request, cancellationToken);
        return Results.Ok(ToPublicationResponse(publication, dockOptions));
    }

    private static async Task<IResult> ApplyCs2ModeAsync(
        Guid id,
        ApplyCs2ModePresetRequest request,
        Cs2ModeService modes,
        IServerWorkQueue queue,
        CancellationToken cancellationToken)
    {
        var result = await modes.ApplyPresetAsync(id, request, cancellationToken);
        if (result.QueuedPackageIds.Count > 0)
        {
            await queue.EnqueueAsync(
                new ServerWorkItem(id, ServerWorkKind.InstallCs2Package, string.Join("\n", result.QueuedPackageIds)),
                cancellationToken);
        }

        return Results.Ok(result);
    }

    private static async Task<IResult> QueueCs2PackageAsync(
        Guid id,
        string packageId,
        Cs2ModeService modes,
        IServerWorkQueue queue,
        CancellationToken cancellationToken)
    {
        var packageStack = modes.ResolveAutomaticInstallOrder([packageId]);
        await queue.EnqueueAsync(
            new ServerWorkItem(id, ServerWorkKind.InstallCs2Package, string.Join("\n", packageStack)),
            cancellationToken);

        return Results.Accepted();
    }

    private static async Task<IResult> SendCommandAsync(
        Guid id,
        ConsoleCommandRequest request,
        ServerOrchestrator orchestrator,
        CancellationToken cancellationToken)
    {
        return Results.Ok(await orchestrator.SendCommandAsync(id, request.Command, cancellationToken));
    }

    private static object ToResponse(
        GameServerInstance server,
        ProcessSnapshot process,
        IGameModuleRegistry modules,
        DockOptions dockOptions,
        IReadOnlyList<PlayerInfo>? players = null,
        string? currentMap = null)
    {
        var module = modules.GetRequired(server.TemplateId);
        var secretKeys = module.Descriptor.Settings.Where(setting => setting.Secret).Select(setting => setting.Key).ToHashSet();
        var settings = JsonSerializer.Deserialize<Dictionary<string, string>>(server.SettingsJson) ?? [];
        ServerPublicationSettings.RemoveMetadata(settings);
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
            Capabilities = module.Descriptor.Capabilities.ToString(),
            NetworkProtocols = module.Descriptor.NetworkProtocols,
            Publication = ToPublicationResponse(ServerPublicationSettings.Read(server), dockOptions)
        };
    }

    private static async Task<IResult> PublicServersAsync(
        IServerRepository servers,
        IGameModuleRegistry modules,
        ICs2ModeManager cs2Modes,
        DockOptions dockOptions,
        CancellationToken cancellationToken)
    {
        if (!dockOptions.PublicPortalEnabled)
        {
            return Results.NotFound();
        }

        if (!IsValidPublicHost(dockOptions.PublicHost))
        {
            return Results.Problem(
                "The guest portal is enabled, but no valid public host is configured.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        var items = await servers.ListAsync(cancellationToken);
        var publishedServers = items
            .Select(server => new { Server = server, Publication = ServerPublicationSettings.Read(server) })
            .Where(item => item.Publication.Published)
            .Select(item =>
            {
                var descriptor = modules.GetRequired(item.Server.TemplateId).Descriptor;
                var activeMode = item.Server.TemplateId == "counter-strike-2"
                    ? cs2Modes.GetActiveProfile(item.Server)
                    : null;
                var settings = JsonSerializer.Deserialize<Dictionary<string, string>>(item.Server.SettingsJson) ?? [];
                var passwordProtected = descriptor.Settings
                    .Where(setting => setting.Secret)
                    .Any(setting => settings.TryGetValue(setting.Key, out var value) && !string.IsNullOrWhiteSpace(value));
                var maxPlayers = settings.TryGetValue("maxPlayers", out var maxPlayersValue) &&
                                 int.TryParse(maxPlayersValue, out var parsedMaxPlayers)
                    ? parsedMaxPlayers
                    : (int?)null;

                return new
                {
                    item.Server.Name,
                    TemplateName = descriptor.Name,
                    TemplateIcon = descriptor.Icon,
                    Status = item.Server.Status.ToString(),
                    JoinAddress = FormatHostPort(dockOptions.PublicHost, item.Publication.PublicPort),
                    PublicPort = item.Publication.PublicPort,
                    Protocols = descriptor.NetworkProtocols,
                    PasswordProtected = passwordProtected,
                    MaxPlayers = maxPlayers,
                    Mode = activeMode?.PresetName,
                    Map = activeMode?.MapName,
                    item.Server.UpdatedAt
                };
            })
            .OrderBy(server => server.Name)
            .ToArray();

        return Results.Ok(new
        {
            Name = dockOptions.PublicPortalName,
            Servers = publishedServers,
            GeneratedAt = DateTimeOffset.UtcNow
        });
    }

    private static object ToPublicationResponse(ServerPublicationState publication, DockOptions dockOptions) => new
    {
        publication.Published,
        publication.PublicPort,
        PortalEnabled = dockOptions.PublicPortalEnabled,
        Address = IsValidPublicHost(dockOptions.PublicHost)
            ? FormatHostPort(dockOptions.PublicHost, publication.PublicPort)
            : null,
        PortalUrl = IsValidPublicHost(dockOptions.PublicHost)
            ? $"http://{FormatHostPort(dockOptions.PublicHost, dockOptions.PublicPortalPort)}/join"
            : null
    };

    private static bool IsValidPublicHost(string host)
    {
        var normalized = host.Trim().Trim('[', ']');
        return !string.IsNullOrWhiteSpace(normalized) && Uri.CheckHostName(normalized) != UriHostNameType.Unknown;
    }

    private static string FormatHostPort(string host, int port)
    {
        var normalized = host.Trim().Trim('[', ']');
        return IPAddress.TryParse(normalized, out var address) && address.AddressFamily == AddressFamily.InterNetworkV6
            ? $"[{normalized}]:{port}"
            : $"{normalized}:{port}";
    }

    private sealed record ConsoleCommandRequest(string Command);
}
