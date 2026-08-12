using System.Net.Http.Json;
using System.Text.Json;
using DKay.GameServerDock.Application.Abstractions;
using DKay.GameServerDock.Application.Models;
using DKay.GameServerDock.Domain;
using DKay.GameServerDock.Infrastructure.Games;

namespace DKay.GameServerDock.Infrastructure.Installation;

public sealed class PaperInstaller(HttpClient httpClient) : IGameInstaller
{
    private const string ProjectUrl = "https://fill.papermc.io/v3/projects/paper";

    public async Task InstallAsync(
        GameServerInstance server,
        Func<InstallationProgress, CancellationToken, Task> reportProgress,
        CancellationToken cancellationToken)
    {
        var settings = GameSettings.Read(server);
        if (!bool.TryParse(settings.Get("acceptEula"), out var accepted) || !accepted)
        {
            throw new InvalidOperationException("The Minecraft EULA must be accepted before Paper can be installed.");
        }

        Directory.CreateDirectory(server.InstallDirectory);
        await reportProgress(new InstallationProgress(5, "metadata", "Resolving a stable Paper build."), cancellationToken);
        var version = server.Version.Equals("latest", StringComparison.OrdinalIgnoreCase)
            ? await ResolveLatestVersionAsync(cancellationToken)
            : server.Version;
        var downloadUrl = await ResolveStableDownloadAsync(version, cancellationToken);

        await reportProgress(new InstallationProgress(20, "download", $"Downloading Paper {version}."), cancellationToken);
        using var response = await SendAsync(downloadUrl, cancellationToken);
        response.EnsureSuccessStatusCode();
        var temporaryPath = Path.Combine(server.InstallDirectory, "paper.jar.download");
        var destinationPath = Path.Combine(server.InstallDirectory, "paper.jar");
        await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
        await using (var destination = File.Create(temporaryPath))
        {
            await source.CopyToAsync(destination, cancellationToken);
        }

        File.Move(temporaryPath, destinationPath, true);
        await reportProgress(new InstallationProgress(80, "configure", "Writing the base server configuration."), cancellationToken);
        await WriteConfigurationAsync(server, settings, cancellationToken);
        await reportProgress(new InstallationProgress(100, "complete", $"Paper {version} is ready."), cancellationToken);
    }

    public Task UpdateAsync(
        GameServerInstance server,
        Func<InstallationProgress, CancellationToken, Task> reportProgress,
        CancellationToken cancellationToken) => InstallAsync(server, reportProgress, cancellationToken);

    private async Task<string> ResolveLatestVersionAsync(CancellationToken cancellationToken)
    {
        using var response = await SendAsync(ProjectUrl, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
        var versions = json.RootElement.GetProperty("versions");
        foreach (var group in versions.EnumerateObject())
        {
            var version = group.Value.EnumerateArray().FirstOrDefault();
            if (version.ValueKind == JsonValueKind.String)
            {
                return version.GetString()!;
            }
        }

        throw new InvalidOperationException("PaperMC did not return an available version.");
    }

    private async Task<string> ResolveStableDownloadAsync(string version, CancellationToken cancellationToken)
    {
        using var response = await SendAsync($"{ProjectUrl}/versions/{Uri.EscapeDataString(version)}/builds", cancellationToken);
        response.EnsureSuccessStatusCode();
        var builds = await response.Content.ReadFromJsonAsync<PaperBuild[]>(cancellationToken: cancellationToken) ?? [];
        var stable = builds.FirstOrDefault(build => build.Channel.Equals("STABLE", StringComparison.OrdinalIgnoreCase));
        if (stable is null || !stable.Downloads.TryGetValue("server:default", out var download))
        {
            throw new InvalidOperationException($"No stable Paper build is available for Minecraft {version}.");
        }

        return download.Url;
    }

    private async Task<HttpResponseMessage> SendAsync(string url, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("DKayGameServerDock/0.1 (https://github.com/damiankaest/DKayGameServerDock)");
        return await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }

    private static async Task WriteConfigurationAsync(
        GameServerInstance server,
        IReadOnlyDictionary<string, string> settings,
        CancellationToken cancellationToken)
    {
        await File.WriteAllTextAsync(Path.Combine(server.InstallDirectory, "eula.txt"), "eula=true\n", cancellationToken);
        var properties = string.Join('\n', new[]
        {
            $"server-port={server.Port}",
            $"motd={settings.SafeConfigValue("motd", server.Name)}",
            $"max-players={settings.SafeConfigValue("maxPlayers", "10")}",
            $"gamemode={settings.SafeConfigValue("gamemode", "survival")}",
            $"difficulty={settings.SafeConfigValue("difficulty", "normal")}",
            $"pvp={settings.SafeConfigValue("pvp", "true")}",
            "enable-query=true"
        });
        await File.WriteAllTextAsync(Path.Combine(server.InstallDirectory, "server.properties"), properties + "\n", cancellationToken);
    }

    private sealed record PaperBuild(string Channel, Dictionary<string, PaperDownload> Downloads);
    private sealed record PaperDownload(string Url);
}

