using System.Globalization;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DKay.GameServerDock.Application.Abstractions;
using DKay.GameServerDock.Application.Models;
using DKay.GameServerDock.Domain;
using DKay.GameServerDock.Infrastructure.Installation;

namespace DKay.GameServerDock.Infrastructure.Games;

public sealed partial class Cs2ModeManager : ICs2ModeManager
{
    private const long MaximumDownloadBytes = 256L * 1024 * 1024;
    private static readonly JsonSerializerOptions FileJsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly IReadOnlyDictionary<string, PackageSource> PackageSources =
        new Dictionary<string, PackageSource>(StringComparer.Ordinal)
        {
            ["metamod-source"] = new(PackageSourceKind.MetamodSnapshot, null),
            ["counterstrikesharp"] = new(PackageSourceKind.GitHubRelease, "roflmuffin/CounterStrikeSharp"),
            ["cs2-tags"] = new(PackageSourceKind.GitHubRelease, "daffyyyy/CS2-Tags"),
            ["movement-unlocker"] = new(PackageSourceKind.GitHubRelease, "Source2ZE/MovementUnlocker"),
            ["rampbug-fix"] = new(PackageSourceKind.GitHubRelease, "Interesting-exe/CS2Fixes-RampbugFix"),
            ["sharp-timer"] = new(PackageSourceKind.GitHubRelease, "Letaryat/poor-sharptimer"),
            ["cs2kz"] = new(PackageSourceKind.GitHubRelease, "KZGlobalTeam/cs2kz-metamod")
        };
    private readonly HttpClient httpClient;
    private readonly Cs2RuntimeProvisioner runtime;

    public Cs2ModeManager(HttpClient httpClient)
        : this(httpClient, new Cs2RuntimeProvisioner(new DockOptions()))
    {
    }

    public Cs2ModeManager(HttpClient httpClient, Cs2RuntimeProvisioner runtime)
    {
        this.httpClient = httpClient;
        this.runtime = runtime;
    }

    public IReadOnlyList<Cs2ModePresetDescriptor> Presets => Cs2ModeCatalog.Presets;
    public IReadOnlyList<Cs2ManagedPackageDescriptor> Packages => Cs2ModeCatalog.Packages;

    public Cs2ModeProfile? GetActiveProfile(GameServerInstance server)
    {
        var document = ReadModeDocument(server);
        var profile = document.ActiveProfileId is null
            ? null
            : document.Profiles.FirstOrDefault(profile => profile.Id == document.ActiveProfileId);
        if (profile is null)
        {
            return null;
        }

        try
        {
            Cs2ModeCatalog.ValidateMap(profile.MapName, profile.WorkshopId);
            var preset = Presets.FirstOrDefault(item => string.Equals(item.Id, profile.PresetId, StringComparison.Ordinal));
            return preset is null ? null : NormalizeProfile(profile, preset);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    public Task<Cs2ModeState> GetStateAsync(GameServerInstance server, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(BuildState(server, ReadModeDocument(server)));
    }

    public async Task<Cs2ModeState> ApplyPresetAsync(
        GameServerInstance server,
        ApplyCs2ModePresetRequest request,
        CancellationToken cancellationToken)
    {
        var preset = Presets.FirstOrDefault(item => string.Equals(item.Id, request.PresetId, StringComparison.Ordinal))
            ?? throw new ArgumentException($"Unknown CS2 preset '{request.PresetId}'.");
        var combatMode = Cs2ModeCatalog.ResolveCombatMode(preset, request.CombatMode);
        var ammoMode = Cs2ModeCatalog.ResolveAmmoMode(preset, request.AmmoMode);
        var hudMode = Cs2ModeCatalog.ResolveHudMode(preset, request.HudMode);
        var respawnMode = Cs2ModeCatalog.ResolveRespawnMode(preset, request.RespawnMode);
        var convars = Cs2ModeCatalog.BuildConVars(preset, request);
        var workshopId = string.IsNullOrWhiteSpace(request.WorkshopId) ? null : request.WorkshopId.Trim();
        Cs2WorkshopMap? workshopMap = null;
        if (workshopId is not null)
        {
            _ = runtime.GetWorkshopApiKey(server);
            workshopMap = await GetWorkshopMapAsync(workshopId, cancellationToken);
        }

        var mapName = workshopMap?.MapName ?? request.MapName.Trim();
        Cs2ModeCatalog.ValidateMap(mapName, workshopId);
        var profileId = workshopId is null ? mapName.ToLowerInvariant() : $"workshop-{workshopId}";
        var normalizedOverrides = (request.Overrides ?? new Dictionary<string, string>())
            .Where(pair => preset.Settings.Any(setting => setting.Editable && setting.Key == pair.Key))
            .ToDictionary(pair => pair.Key, pair => convars[pair.Key], StringComparer.Ordinal);
        var profile = new Cs2ModeProfile(
            profileId,
            preset.Id,
            preset.Name,
            mapName,
            workshopId,
            workshopMap?.Title,
            workshopMap?.PreviewUrl,
            workshopId is null ? "local" : GetWorkshopInstallState(server, workshopId),
            request.BotQuota,
            request.BotDifficulty,
            normalizedOverrides,
            preset.RecommendedPackageIds,
            DateTimeOffset.UtcNow,
            combatMode,
            ammoMode,
            hudMode,
            respawnMode);

        var cfgRoot = GetCfgRoot(server);
        await WriteProfileConfigurationAsync(server, profile, preset, convars, cancellationToken);
        await WriteAllLinesAtomicallyAsync(
            Path.Combine(cfgRoot, "dkay-mode.cfg"),
            [
                "// Active map preset managed by DKay Game Server Dock.",
                $"exec dkay/maps/{profileId}.cfg"
            ],
            cancellationToken);
        await WriteActiveCombatConfigurationAsync(server, profile, preset, cancellationToken);
        AlignLiveSettingsWithPreset(server, convars);

        var document = ReadModeDocument(server);
        var profiles = document.Profiles.Where(item => item.Id != profile.Id).Append(profile).OrderBy(item => item.MapName).ToArray();
        document = new ModeDocument(profile.Id, profiles);
        await WriteJsonAtomicallyAsync(GetModeDocumentPath(server), document, cancellationToken);
        return BuildState(server, document);
    }

    public async Task<Cs2ModeProfile> ActivateProfileAsync(
        GameServerInstance server,
        string profileId,
        CancellationToken cancellationToken)
    {
        var document = ReadModeDocument(server);
        var profile = document.Profiles.FirstOrDefault(item => string.Equals(item.Id, profileId, StringComparison.Ordinal))
            ?? throw new KeyNotFoundException($"CS2 map profile '{profileId}' was not found.");
        Cs2ModeCatalog.ValidateMap(profile.MapName, profile.WorkshopId);
        var expectedProfileId = profile.WorkshopId is null
            ? profile.MapName.ToLowerInvariant()
            : $"workshop-{profile.WorkshopId}";
        if (!string.Equals(profile.Id, expectedProfileId, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The saved CS2 map profile identifier is invalid.");
        }

        var preset = Presets.FirstOrDefault(item => string.Equals(item.Id, profile.PresetId, StringComparison.Ordinal))
            ?? throw new InvalidDataException($"The saved CS2 preset '{profile.PresetId}' is no longer available.");
        profile = NormalizeProfile(profile, preset);
        var convars = BuildProfileConVars(profile, preset);
        await WriteProfileConfigurationAsync(server, profile, preset, convars, cancellationToken);

        await WriteAllLinesAtomicallyAsync(
            Path.Combine(GetCfgRoot(server), "dkay-mode.cfg"),
            [
                "// Active map preset managed by DKay Game Server Dock.",
                $"exec dkay/maps/{expectedProfileId}.cfg"
            ],
            cancellationToken);
        await WriteActiveCombatConfigurationAsync(server, profile, preset, cancellationToken);
        AlignLiveSettingsWithPreset(server, convars);
        var profiles = document.Profiles
            .Select(item => string.Equals(item.Id, profile.Id, StringComparison.Ordinal) ? profile : item)
            .ToArray();
        await WriteJsonAtomicallyAsync(
            GetModeDocumentPath(server),
            document with { ActiveProfileId = profile.Id, Profiles = profiles },
            cancellationToken);
        return profile;
    }

    public async Task<Cs2ModeProfile> SetActiveCombatModeAsync(
        GameServerInstance server,
        string combatMode,
        CancellationToken cancellationToken)
    {
        var document = ReadModeDocument(server);
        var profile = document.ActiveProfileId is null
            ? null
            : document.Profiles.FirstOrDefault(item => string.Equals(item.Id, document.ActiveProfileId, StringComparison.Ordinal));
        if (profile is null)
        {
            throw new InvalidOperationException("Select and activate a CS2 map profile before changing its live combat mode.");
        }

        var preset = Presets.FirstOrDefault(item => string.Equals(item.Id, profile.PresetId, StringComparison.Ordinal))
            ?? throw new InvalidDataException($"The saved CS2 preset '{profile.PresetId}' is no longer available.");
        profile = NormalizeProfile(profile, preset) with
        {
            CombatMode = Cs2ModeCatalog.ResolveCombatMode(preset, combatMode),
            UpdatedAt = DateTimeOffset.UtcNow
        };
        var convars = BuildProfileConVars(profile, preset);
        await WriteProfileConfigurationAsync(server, profile, preset, convars, cancellationToken);
        await WriteActiveCombatConfigurationAsync(server, profile, preset, cancellationToken);

        var profiles = document.Profiles
            .Select(item => string.Equals(item.Id, profile.Id, StringComparison.Ordinal) ? profile : item)
            .ToArray();
        await WriteJsonAtomicallyAsync(
            GetModeDocumentPath(server),
            document with { Profiles = profiles },
            cancellationToken);
        return profile;
    }

    public async Task<Cs2ModeProfile> SetActiveRespawnModeAsync(
        GameServerInstance server,
        string respawnMode,
        CancellationToken cancellationToken)
    {
        var document = ReadModeDocument(server);
        var profile = GetActiveProfileForPolicyChange(document);
        var preset = Presets.FirstOrDefault(item => string.Equals(item.Id, profile.PresetId, StringComparison.Ordinal))
            ?? throw new InvalidDataException($"The saved CS2 preset '{profile.PresetId}' is no longer available.");
        profile = NormalizeProfile(profile, preset) with
        {
            RespawnMode = Cs2ModeCatalog.ResolveRespawnMode(preset, respawnMode),
            UpdatedAt = DateTimeOffset.UtcNow
        };
        var convars = BuildProfileConVars(profile, preset);
        await WriteProfileConfigurationAsync(server, profile, preset, convars, cancellationToken);
        await WriteActiveCombatConfigurationAsync(server, profile, preset, cancellationToken);
        await PersistUpdatedProfileAsync(server, document, profile, cancellationToken);
        return profile;
    }

    public async Task<Cs2ModeProfile> SetActiveHudModeAsync(
        GameServerInstance server,
        string hudMode,
        CancellationToken cancellationToken)
    {
        var document = ReadModeDocument(server);
        var profile = GetActiveProfileForPolicyChange(document);
        var preset = Presets.FirstOrDefault(item => string.Equals(item.Id, profile.PresetId, StringComparison.Ordinal))
            ?? throw new InvalidDataException($"The saved CS2 preset '{profile.PresetId}' is no longer available.");
        profile = NormalizeProfile(profile, preset) with
        {
            HudMode = Cs2ModeCatalog.ResolveHudMode(preset, hudMode),
            UpdatedAt = DateTimeOffset.UtcNow
        };
        var convars = BuildProfileConVars(profile, preset);
        await WriteProfileConfigurationAsync(server, profile, preset, convars, cancellationToken);
        await WriteActiveCombatConfigurationAsync(server, profile, preset, cancellationToken);
        await PersistUpdatedProfileAsync(server, document, profile, cancellationToken);
        return profile;
    }

    public Cs2WorkshopAccessState GetWorkshopAccessState(GameServerInstance server) =>
        runtime.GetWorkshopAccessState(server);

    public Cs2WorkshopAccessState SaveWorkshopApiKey(GameServerInstance server, string key) =>
        runtime.SaveWorkshopApiKey(server, key);

    public async Task<Cs2WorkshopSearchResult> SearchWorkshopMapsAsync(
        GameServerInstance server,
        string query,
        int take,
        CancellationToken cancellationToken)
    {
        query = query?.Trim() ?? string.Empty;
        if (query.Length is < 2 or > 80)
        {
            throw new ArgumentException("Workshop search text must contain between 2 and 80 characters.");
        }

        var key = runtime.GetWorkshopApiKey(server);
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["key"] = key,
            ["query_type"] = "0",
            ["page"] = "1",
            ["numperpage"] = Math.Clamp(take, 1, 30).ToString(CultureInfo.InvariantCulture),
            ["creator_appid"] = "730",
            ["appid"] = "730",
            ["search_text"] = query,
            ["filetype"] = "0",
            ["return_vote_data"] = "true",
            ["return_tags"] = "true",
            ["return_previews"] = "true",
            ["return_short_description"] = "true"
        };
        var queryString = string.Join("&", parameters.Select(pair =>
            $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
        var url = new Uri($"https://api.steampowered.com/IPublishedFileService/QueryFiles/v1/?{queryString}");
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await SendSteamApiAsync(request, cancellationToken);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!json.RootElement.TryGetProperty("response", out var root))
        {
            throw new InvalidDataException("Steam Workshop returned an unexpected search response.");
        }

        var total = (int)Math.Clamp(ReadInt64(root, "total"), 0, int.MaxValue);
        var items = new List<Cs2WorkshopMap>();
        if (root.TryGetProperty("publishedfiledetails", out var details) && details.ValueKind == JsonValueKind.Array)
        {
            foreach (var detail in details.EnumerateArray())
            {
                try
                {
                    items.Add(ParseWorkshopMap(detail));
                }
                catch (InvalidDataException)
                {
                    // Removed, private, collection, or otherwise unusable entries never become selectable maps.
                }
            }
        }

        return new Cs2WorkshopSearchResult(query, total, items);
    }

    public IReadOnlyList<string> ResolveAutomaticInstallOrder(IEnumerable<string> packageIds)
    {
        var result = new List<string>();
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);

        foreach (var packageId in packageIds)
        {
            Visit(packageId);
        }

        return result;

        void Visit(string packageId)
        {
            if (visited.Contains(packageId))
            {
                return;
            }

            if (!visiting.Add(packageId))
            {
                throw new InvalidOperationException("The managed package catalog contains a dependency cycle.");
            }

            var package = Packages.FirstOrDefault(item => item.Id == packageId)
                ?? throw new ArgumentException($"Unknown managed package '{packageId}'.");
            foreach (var dependency in package.DependencyIds)
            {
                Visit(dependency);
            }

            visiting.Remove(packageId);
            visited.Add(packageId);
            if (package.AutomaticInstall)
            {
                result.Add(packageId);
            }
        }
    }

    public async Task RepairAfterGameUpdateAsync(GameServerInstance server, CancellationToken cancellationToken)
    {
        if (ReadPackageMarker(server, "metamod-source").Installed)
        {
            await PatchGameInfoAsync(server, cancellationToken);
        }

        var document = ReadModeDocument(server);
        foreach (var profile in document.Profiles)
        {
            var preset = Presets.FirstOrDefault(item => string.Equals(item.Id, profile.PresetId, StringComparison.Ordinal));
            if (preset is null)
            {
                continue;
            }

            var normalizedProfile = NormalizeProfile(profile, preset);
            var convars = BuildProfileConVars(normalizedProfile, preset);
            await WriteProfileConfigurationAsync(server, normalizedProfile, preset, convars, cancellationToken);
            if (string.Equals(profile.Id, document.ActiveProfileId, StringComparison.Ordinal))
            {
                await WriteActiveCombatConfigurationAsync(server, normalizedProfile, preset, cancellationToken);
                AlignLiveSettingsWithPreset(server, convars);
            }
        }
    }

    public async Task InstallPackageAsync(
        GameServerInstance server,
        string packageId,
        Func<InstallationProgress, CancellationToken, Task> reportProgress,
        CancellationToken cancellationToken)
    {
        var package = Packages.FirstOrDefault(item => item.Id == packageId)
            ?? throw new ArgumentException($"Unknown managed package '{packageId}'.");
        if (!package.AutomaticInstall || !PackageSources.TryGetValue(packageId, out var source))
        {
            throw new InvalidOperationException($"'{package.Name}' has no trusted automatic installation channel.");
        }

        foreach (var dependency in package.DependencyIds)
        {
            if (!ReadPackageMarker(server, dependency).Installed)
            {
                throw new InvalidOperationException($"Install dependency '{dependency}' before '{package.Name}'.");
            }
        }

        var csgoRoot = GetCsgoRoot(server);
        if (!Directory.Exists(csgoRoot))
        {
            throw new InvalidOperationException("The CS2 installation is incomplete; the game/csgo directory is missing.");
        }

        await reportProgress(new InstallationProgress(5, "resolve", $"Resolving trusted {package.Name} release…"), cancellationToken);
        var download = source.Kind == PackageSourceKind.MetamodSnapshot
            ? await ResolveMetamodSnapshotAsync(cancellationToken)
            : await ResolveGitHubReleaseAsync(packageId, source.Repository!, cancellationToken);
        var temporaryRoot = Path.Combine(Path.GetTempPath(), $"dkay-cs2-mod-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryRoot);

        try
        {
            var archivePath = Path.Combine(temporaryRoot, "package.zip");
            await DownloadAsync(download.Url, archivePath, cancellationToken);
            await reportProgress(new InstallationProgress(55, "verify", $"Validating {package.Name} archive paths…"), cancellationToken);
            var stagingRoot = Path.Combine(temporaryRoot, "staging");
            await SafeZipExtractor.ExtractAsync(archivePath, stagingRoot, cancellationToken);
            var deployment = ResolveDeployment(packageId, stagingRoot, csgoRoot);
            await reportProgress(new InstallationProgress(75, "deploy", $"Deploying {package.Name} into game/csgo…"), cancellationToken);
            await CopyPayloadAsync(deployment.SourceRoot, deployment.DestinationRoot, cancellationToken);

            if (packageId == "metamod-source")
            {
                await PatchGameInfoAsync(server, cancellationToken);
            }

            await WritePackageMarkerAsync(server, new PackageMarker(true, download.Version, DateTimeOffset.UtcNow), packageId, cancellationToken);
            if (packageId == "sharp-timer" && GetActiveProfile(server) is { } activeProfile)
            {
                var activePreset = Presets.Single(item => item.Id == activeProfile.PresetId);
                await WriteActiveCombatConfigurationAsync(server, activeProfile, activePreset, cancellationToken);
            }
            await reportProgress(new InstallationProgress(100, "complete", $"{package.Name} {download.Version} installed."), cancellationToken);
        }
        finally
        {
            if (Directory.Exists(temporaryRoot))
            {
                try
                {
                    Directory.Delete(temporaryRoot, true);
                }
                catch (IOException)
                {
                    // A temporary antivirus/file-indexing lock must not turn a completed install into a failure.
                }
                catch (UnauthorizedAccessException)
                {
                    // Best-effort cleanup; the OS temp cleaner can remove a locked directory later.
                }
            }
        }
    }

    private Cs2ModeState BuildState(GameServerInstance server, ModeDocument document)
    {
        var profiles = document.Profiles.Select(profile =>
        {
            var preset = Presets.FirstOrDefault(item => item.Id == profile.PresetId);
            var normalized = preset is null ? profile : NormalizeProfile(profile, preset);
            return normalized with
            {
                WorkshopInstallState = normalized.WorkshopId is null
                    ? "local"
                    : GetWorkshopInstallState(server, normalized.WorkshopId)
            };
        }).ToArray();
        var packageStates = Packages.Select(package =>
        {
            var marker = ReadPackageMarker(server, package.Id);
            return new Cs2ManagedPackageState(
                package.Id,
                package.Name,
                package.Kind,
                package.Description,
                package.Publisher,
                package.ProjectUrl,
                package.AutomaticInstall,
                package.Experimental,
                marker.Installed,
                marker.Version,
                marker.InstalledAt,
                package.DependencyIds);
        }).ToArray();
        return new Cs2ModeState(document.ActiveProfileId, profiles, packageStates, runtime.GetWorkshopAccessState(server));
    }

    private async Task<Cs2WorkshopMap> GetWorkshopMapAsync(
        string publishedFileId,
        CancellationToken cancellationToken)
    {
        if (!WorkshopIdPattern().IsMatch(publishedFileId))
        {
            throw new ArgumentException("Workshop id must be a positive numeric Steam Workshop id.");
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["itemcount"] = "1",
                ["publishedfileids[0]"] = publishedFileId
            })
        };
        using var response = await SendSteamApiAsync(request, cancellationToken);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!json.RootElement.TryGetProperty("response", out var root) ||
            !root.TryGetProperty("publishedfiledetails", out var details) ||
            details.ValueKind != JsonValueKind.Array ||
            details.GetArrayLength() != 1)
        {
            throw new InvalidDataException("Steam Workshop did not return details for this map.");
        }

        try
        {
            return ParseWorkshopMap(details[0]);
        }
        catch (InvalidDataException exception)
        {
            throw new InvalidOperationException(
                $"Workshop item {publishedFileId} cannot be used as a CS2 map. It may be removed, private, a collection, or incompatible with CS2. {exception.Message}",
                exception);
        }
    }

    private async Task<HttpResponseMessage> SendSteamApiAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("DKayGameServerDock", "1.0"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.RequestMessage?.RequestUri is not { } finalUri ||
            !finalUri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !finalUri.Host.Equals("api.steampowered.com", StringComparison.OrdinalIgnoreCase))
        {
            response.Dispose();
            throw new InvalidOperationException("Steam Workshop redirected to an untrusted API host.");
        }

        if (!response.IsSuccessStatusCode)
        {
            var status = response.StatusCode;
            response.Dispose();
            throw new InvalidOperationException($"Steam Workshop API returned HTTP {(int)status}. Check the protected Web API key and try again.");
        }

        return response;
    }

    private static Cs2WorkshopMap ParseWorkshopMap(JsonElement detail)
    {
        var result = ReadInt64(detail, "result");
        if (result != 1)
        {
            throw new InvalidDataException($"Steam result code {result} indicates that the item is unavailable.");
        }

        var consumerAppId = ReadInt64(detail, "consumer_app_id", "consumer_appid");
        if (consumerAppId != 730)
        {
            throw new InvalidDataException("The item does not belong to the Counter-Strike 2 Workshop.");
        }

        if (ReadBoolean(detail, "banned"))
        {
            throw new InvalidDataException("The Workshop item is banned.");
        }

        var publishedFileId = ReadString(detail, "publishedfileid");
        if (!WorkshopIdPattern().IsMatch(publishedFileId))
        {
            throw new InvalidDataException("Steam returned an invalid Workshop id.");
        }

        var fileType = ReadInt64(detail, "file_type");
        if (fileType != 0)
        {
            throw new InvalidDataException("The Workshop item is not an individual map file.");
        }

        var title = ReadString(detail, "title").Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new InvalidDataException("The Workshop map has no title.");
        }

        string? previewUrl = null;
        var previewCandidate = ReadString(detail, "preview_url");
        if (Uri.TryCreate(previewCandidate, UriKind.Absolute, out var preview) &&
            preview.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
            IsTrustedSteamImageHost(preview.Host))
        {
            previewUrl = preview.ToString();
        }

        var tags = detail.TryGetProperty("tags", out var tagsElement) && tagsElement.ValueKind == JsonValueKind.Array
            ? tagsElement.EnumerateArray()
                .Select(tag => ReadString(tag, "display_name", "tag"))
                .Where(tag => !string.IsNullOrWhiteSpace(tag))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(12)
                .ToArray()
            : [];
        return new Cs2WorkshopMap(
            publishedFileId,
            title,
            DeriveMapName(title, publishedFileId),
            previewUrl,
            $"https://steamcommunity.com/sharedfiles/filedetails/?id={publishedFileId}",
            Math.Max(0, ReadInt64(detail, "file_size")),
            Math.Max(0, ReadInt64(detail, "subscriptions")),
            ReadUnixTimestamp(detail, "time_updated"),
            tags);
    }

    private static string GetWorkshopInstallState(GameServerInstance server, string publishedFileId)
    {
        var mapsWorkshopRoot = Path.Combine(server.InstallDirectory, "game", "csgo", "maps", "workshop");
        var directPayloads = new[]
        {
            Path.Combine(mapsWorkshopRoot, $"{publishedFileId}.vpk"),
            Path.Combine(mapsWorkshopRoot, $"{publishedFileId}.bsp")
        };
        if (directPayloads.Any(File.Exists))
        {
            return "installed";
        }

        var workshopRoots = new[]
        {
            Path.Combine(mapsWorkshopRoot, publishedFileId),
            Path.Combine(server.InstallDirectory, "steamapps", "workshop", "content", "730", publishedFileId)
        };

        foreach (var workshopRoot in workshopRoots.Where(Directory.Exists))
        {
            try
            {
                if (Directory.EnumerateFiles(workshopRoot, "*", SearchOption.AllDirectories)
                    .Any(file => file.EndsWith(".vpk", StringComparison.OrdinalIgnoreCase) ||
                                 file.EndsWith(".bsp", StringComparison.OrdinalIgnoreCase)))
                {
                    return "installed";
                }
            }
            catch (IOException)
            {
                // CS2 may be replacing a payload while its state is polled. Retry on the next refresh.
            }
            catch (UnauthorizedAccessException)
            {
                // A transient read restriction must not turn a running download into an API failure.
            }
        }

        return "pending";
    }

    private static string DeriveMapName(string title, string publishedFileId)
    {
        var match = WorkshopMapNamePattern().Match(title);
        if (match.Success)
        {
            return match.Groups["map"].Value[..Math.Min(64, match.Groups["map"].Value.Length)];
        }

        var sanitized = Regex.Replace(title.ToLowerInvariant(), "[^a-z0-9_-]+", "_").Trim('_', '-');
        return string.IsNullOrWhiteSpace(sanitized)
            ? $"workshop_{publishedFileId}"
            : sanitized[..Math.Min(64, sanitized.Length)];
    }

    private static long ReadInt64(JsonElement element, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (!element.TryGetProperty(propertyName, out var property))
            {
                continue;
            }

            if (property.ValueKind == JsonValueKind.Number && property.TryGetInt64(out var number))
            {
                return number;
            }

            if (property.ValueKind == JsonValueKind.String &&
                long.TryParse(property.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
            {
                return number;
            }
        }

        return 0;
    }

    private static bool ReadBoolean(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) &&
        (property.ValueKind == JsonValueKind.True ||
         property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var value) && value != 0);

    private static string ReadString(JsonElement element, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (element.TryGetProperty(propertyName, out var property))
            {
                return property.ValueKind switch
                {
                    JsonValueKind.String => property.GetString() ?? string.Empty,
                    JsonValueKind.Number => property.GetRawText(),
                    _ => string.Empty
                };
            }
        }

        return string.Empty;
    }

    private static DateTimeOffset? ReadUnixTimestamp(JsonElement element, string propertyName)
    {
        var seconds = ReadInt64(element, propertyName);
        if (seconds <= 0)
        {
            return null;
        }

        try
        {
            return DateTimeOffset.FromUnixTimeSeconds(seconds);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static bool IsTrustedSteamImageHost(string host) =>
        host.EndsWith(".steamusercontent.com", StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith(".steamstatic.com", StringComparison.OrdinalIgnoreCase);

    private async Task<PackageDownload> ResolveMetamodSnapshotAsync(CancellationToken cancellationToken)
    {
        var platform = OperatingSystem.IsWindows() ? "windows" : "linux";
        var latestUrl = new Uri($"https://mms.alliedmods.net/mmsdrop/2.0/mmsource-latest-{platform}");
        using var response = await SendTrustedAsync(latestUrl, cancellationToken);
        var fileName = (await response.Content.ReadAsStringAsync(cancellationToken)).Trim();
        if (!MetamodFilePattern().IsMatch(fileName) || !fileName.EndsWith($"-{platform}.zip", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("AlliedModders returned an unexpected Metamod snapshot name.");
        }

        var suffix = $"-{platform}.zip";
        var version = fileName["mmsource-".Length..^suffix.Length];
        return new PackageDownload(new Uri($"https://mms.alliedmods.net/mmsdrop/2.0/{fileName}"), version);
    }

    private async Task<PackageDownload> ResolveGitHubReleaseAsync(string packageId, string repository, CancellationToken cancellationToken)
    {
        var apiUrl = new Uri($"https://api.github.com/repos/{repository}/releases/latest");
        using var response = await SendTrustedAsync(apiUrl, cancellationToken, "application/vnd.github+json");
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var version = json.RootElement.GetProperty("tag_name").GetString() ?? "latest";
        var assets = json.RootElement.GetProperty("assets").EnumerateArray()
            .Select(asset => new
            {
                Name = asset.GetProperty("name").GetString() ?? string.Empty,
                Url = asset.GetProperty("browser_download_url").GetString() ?? string.Empty
            })
            .Where(asset => asset.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var platform = OperatingSystem.IsWindows() ? "windows" : "linux";
        var selected = packageId switch
        {
            "counterstrikesharp" => assets.FirstOrDefault(asset => asset.Name.Contains("with-runtime", StringComparison.OrdinalIgnoreCase) && asset.Name.Contains(platform, StringComparison.OrdinalIgnoreCase)),
            "cs2kz" => assets.FirstOrDefault(asset => asset.Name.Contains(platform, StringComparison.OrdinalIgnoreCase)),
            _ => assets.FirstOrDefault(asset => asset.Name.Contains(platform, StringComparison.OrdinalIgnoreCase)) ?? assets.FirstOrDefault()
        };
        if (selected is null || !Uri.TryCreate(selected.Url, UriKind.Absolute, out var assetUrl) || assetUrl.Host != "github.com")
        {
            throw new InvalidOperationException($"The latest trusted release of '{packageId}' has no compatible ZIP asset for {platform}.");
        }

        return new PackageDownload(assetUrl, version);
    }

    private async Task DownloadAsync(Uri url, string destination, CancellationToken cancellationToken)
    {
        using var response = await SendTrustedAsync(url, cancellationToken, "application/octet-stream");
        if (response.Content.Headers.ContentLength is > MaximumDownloadBytes)
        {
            throw new InvalidDataException("The mod package download is larger than 256 MB.");
        }

        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
        {
            total += read;
            if (total > MaximumDownloadBytes)
            {
                throw new InvalidDataException("The mod package download exceeded 256 MB.");
            }

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        if (total < 4)
        {
            throw new InvalidDataException("The downloaded mod archive is empty.");
        }
    }

    private async Task<HttpResponseMessage> SendTrustedAsync(Uri url, CancellationToken cancellationToken, string? accept = null)
    {
        if (!IsTrustedDownloadHost(url.Host))
        {
            throw new InvalidOperationException("The managed package attempted to use an untrusted download host.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("DKayGameServerDock", "1.0"));
        if (accept is not null)
        {
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));
        }

        var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.RequestMessage?.RequestUri is not { } finalUri || !IsTrustedDownloadHost(finalUri.Host))
        {
            response.Dispose();
            throw new InvalidOperationException("The managed package redirected to an untrusted download host.");
        }

        response.EnsureSuccessStatusCode();
        return response;
    }

    private static bool IsTrustedDownloadHost(string host) => host.ToLowerInvariant() is
        "api.github.com" or
        "github.com" or
        "objects.githubusercontent.com" or
        "github-releases.githubusercontent.com" or
        "release-assets.githubusercontent.com" or
        "mms.alliedmods.net";

    private static string ResolvePayloadRoot(string stagingRoot)
    {
        var candidates = new[]
        {
            Path.Combine(stagingRoot, "game", "csgo"),
            Path.Combine(stagingRoot, "csgo"),
            stagingRoot
        }.Concat(Directory.GetDirectories(stagingRoot).SelectMany(directory => new[]
        {
            Path.Combine(directory, "game", "csgo"),
            Path.Combine(directory, "csgo"),
            directory
        }));

        return candidates.FirstOrDefault(candidate =>
                   Directory.Exists(Path.Combine(candidate, "addons")) ||
                   Directory.Exists(Path.Combine(candidate, "cfg")))
               ?? throw new InvalidDataException("The mod archive does not contain a CS2 addons or cfg payload.");
    }

    private static PackageDeployment ResolveDeployment(string packageId, string stagingRoot, string csgoRoot)
    {
        if (packageId == "cs2-tags")
        {
            var pluginAssembly = Directory
                .EnumerateFiles(stagingRoot, "CS2-Tags.dll", SearchOption.AllDirectories)
                .SingleOrDefault();
            if (pluginAssembly is null)
            {
                throw new InvalidDataException("The CS2-Tags archive does not contain CS2-Tags.dll.");
            }

            return new PackageDeployment(
                Path.GetDirectoryName(pluginAssembly)!,
                Path.Combine(csgoRoot, "addons", "counterstrikesharp", "plugins", "CS2-Tags"));
        }

        return new PackageDeployment(ResolvePayloadRoot(stagingRoot), csgoRoot);
    }

    private static async Task CopyPayloadAsync(string sourceRoot, string destinationRoot, CancellationToken cancellationToken)
    {
        var destination = Path.GetFullPath(destinationRoot) + Path.DirectorySeparatorChar;
        foreach (var sourceFile in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(sourceRoot, sourceFile);
            var target = Path.GetFullPath(Path.Combine(destination, relative));
            if (!target.StartsWith(destination, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The staged mod payload escaped the CS2 directory.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await using var input = new FileStream(sourceFile, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
            await using var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
            await input.CopyToAsync(output, cancellationToken);
        }
    }

    private static async Task PatchGameInfoAsync(GameServerInstance server, CancellationToken cancellationToken)
    {
        var gameInfoPath = Path.Combine(server.InstallDirectory, "game", "csgo", "gameinfo.gi");
        if (!File.Exists(gameInfoPath))
        {
            throw new InvalidOperationException("Metamod was extracted, but game/csgo/gameinfo.gi is missing.");
        }

        var lines = (await File.ReadAllLinesAsync(gameInfoPath, cancellationToken)).ToList();
        if (lines.Any(line => line.Contains("Game csgo/addons/metamod", StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var searchPaths = lines.FindIndex(line => line.Trim().Equals("SearchPaths", StringComparison.OrdinalIgnoreCase));
        var openingBrace = searchPaths < 0 ? -1 : lines.FindIndex(searchPaths + 1, line => line.Trim() == "{");
        if (openingBrace < 0)
        {
            throw new InvalidDataException("Could not safely locate SearchPaths in gameinfo.gi.");
        }

        var backupPath = $"{gameInfoPath}.dkay-original";
        if (!File.Exists(backupPath))
        {
            File.Copy(gameInfoPath, backupPath);
        }

        var indent = openingBrace + 1 < lines.Count
            ? lines[openingBrace + 1][..(lines[openingBrace + 1].Length - lines[openingBrace + 1].TrimStart().Length)]
            : "\t\t";
        lines.Insert(openingBrace + 1, $"{indent}Game csgo/addons/metamod");
        await WriteAllLinesAtomicallyAsync(gameInfoPath, lines, cancellationToken);
    }

    private static IReadOnlyDictionary<string, string> BuildProfileConVars(
        Cs2ModeProfile profile,
        Cs2ModePresetDescriptor preset) =>
        Cs2ModeCatalog.BuildConVars(
            preset,
            new ApplyCs2ModePresetRequest(
                profile.PresetId,
                profile.MapName,
                profile.WorkshopId,
                profile.BotQuota,
                profile.BotDifficulty,
                false,
                profile.Overrides,
                profile.CombatMode,
                profile.AmmoMode,
                profile.HudMode,
                profile.RespawnMode));

    private static Cs2ModeProfile NormalizeProfile(
        Cs2ModeProfile profile,
        Cs2ModePresetDescriptor preset) =>
        profile with
        {
            CombatMode = Cs2ModeCatalog.ResolveCombatMode(preset, profile.CombatMode),
            AmmoMode = Cs2ModeCatalog.ResolveAmmoMode(preset, profile.AmmoMode),
            HudMode = Cs2ModeCatalog.ResolveHudMode(preset, profile.HudMode),
            RespawnMode = Cs2ModeCatalog.ResolveRespawnMode(preset, profile.RespawnMode)
        };

    private static async Task WriteProfileConfigurationAsync(
        GameServerInstance server,
        Cs2ModeProfile profile,
        Cs2ModePresetDescriptor preset,
        IReadOnlyDictionary<string, string> convars,
        CancellationToken cancellationToken)
    {
        var mapsRoot = Path.Combine(GetCfgRoot(server), "dkay", "maps");
        Directory.CreateDirectory(mapsRoot);
        var lines = new List<string>
        {
            "// Generated by DKay Game Server Dock. Edit the preset in the admin panel.",
            $"// Preset: {preset.Name}; map: {profile.MapName}; generated: {DateTimeOffset.UtcNow:O}",
            "bot_kick"
        };
        lines.AddRange(convars
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => $"{pair.Key} {FormatCfgValue(pair.Value)}"));
        await WriteAllLinesAtomicallyAsync(
            Path.Combine(mapsRoot, $"{profile.Id}.cfg"),
            lines,
            cancellationToken);
    }

    private async Task WriteActiveCombatConfigurationAsync(
        GameServerInstance server,
        Cs2ModeProfile profile,
        Cs2ModePresetDescriptor preset,
        CancellationToken cancellationToken)
    {
        var combatMode = Cs2ModeCatalog.ResolveCombatMode(preset, profile.CombatMode);
        var ammoMode = Cs2ModeCatalog.ResolveAmmoMode(preset, profile.AmmoMode);
        var hudMode = Cs2ModeCatalog.ResolveHudMode(preset, profile.HudMode);
        var respawnMode = Cs2ModeCatalog.ResolveRespawnMode(preset, profile.RespawnMode);
        var lines = new List<string>
        {
            "// Generated by DKay Game Server Dock. Applied after map and plugin configuration.",
            $"// Combat: {combatMode}; ammunition: {ammoMode}; respawn: {respawnMode}; SharpTimer HUD: {hudMode}"
        };
        lines.AddRange(Cs2ModeCatalog.BuildCombatConVars(combatMode, ammoMode)
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => $"{pair.Key} {FormatCfgValue(pair.Value)}"));
        lines.AddRange(Cs2ModeCatalog.BuildRespawnConVars(respawnMode)
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => $"{pair.Key} {FormatCfgValue(pair.Value)}"));

        var sharpTimerInstalled = ReadPackageMarker(server, "sharp-timer").Installed;
        if (sharpTimerInstalled)
        {
            lines.AddRange(Cs2ModeCatalog.BuildSharpTimerCombatCommands(combatMode, ammoMode)
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => $"{pair.Key} {FormatCfgValue(pair.Value)}"));
            lines.AddRange(Cs2ModeCatalog.BuildSharpTimerHudCommands(hudMode)
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => $"{pair.Key} {FormatCfgValue(pair.Value)}"));
        }

        await WriteAllLinesAtomicallyAsync(
            Path.Combine(GetCfgRoot(server), "dkay-combat.cfg"),
            lines,
            cancellationToken);

        if (sharpTimerInstalled)
        {
            await EnsureSharpTimerCombatOverridesAsync(server, cancellationToken);
        }
    }

    private static async Task EnsureSharpTimerCombatOverridesAsync(
        GameServerInstance server,
        CancellationToken cancellationToken)
    {
        var sharpTimerRoot = Path.Combine(GetCfgRoot(server), "SharpTimer");
        await EnsureCombatExecDirectiveAsync(
            Path.Combine(sharpTimerRoot, "custom_exec.cfg"),
            "// DKay managed profile policy followed by the latest live overrides.",
            cancellationToken);

        var mapExecRoot = Path.Combine(sharpTimerRoot, "MapData", "MapExecs");
        if (!Directory.Exists(mapExecRoot))
        {
            return;
        }

        foreach (var path in Directory.EnumerateFiles(mapExecRoot, "*.cfg", SearchOption.TopDirectoryOnly))
        {
            await EnsureCombatExecDirectiveAsync(
                path,
                "// DKay managed profile policy and live overrides. Keep these lines last.",
                cancellationToken);
        }
    }

    private static async Task EnsureCombatExecDirectiveAsync(
        string path,
        string comment,
        CancellationToken cancellationToken)
    {
        var lines = File.Exists(path)
            ? (await File.ReadAllLinesAsync(path, cancellationToken)).ToList()
            : [];
        lines.RemoveAll(line =>
            line.TrimStart().StartsWith("// DKay managed", StringComparison.OrdinalIgnoreCase) ||
            Regex.IsMatch(
                line,
                "^\\s*exec(?:ifexists)?\\s+\"?(?:cfg/)?dkay-(?:combat|live)\\.cfg\"?\\s*(?://.*)?$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));

        while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[^1]))
        {
            lines.RemoveAt(lines.Count - 1);
        }

        if (lines.Count > 0 && !string.IsNullOrWhiteSpace(lines[^1]))
        {
            lines.Add(string.Empty);
        }

        lines.Add(comment);
        lines.Add("exec dkay-combat.cfg");
        lines.Add("exec dkay-live.cfg");
        await WriteAllLinesAtomicallyAsync(path, lines, cancellationToken);
    }

    private void AlignLiveSettingsWithPreset(
        GameServerInstance server,
        IReadOnlyDictionary<string, string> convars)
    {
        runtime.AlignPersistedLiveSettingsWithPreset(server, convars);
    }

    private static string FormatCfgValue(string value)
    {
        if (NumericValuePattern().IsMatch(value))
        {
            return value;
        }

        var escaped = value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
        return $"\"{escaped}\"";
    }

    private static ModeDocument ReadModeDocument(GameServerInstance server)
    {
        var path = GetModeDocumentPath(server);
        if (!File.Exists(path))
        {
            return new ModeDocument(null, []);
        }

        try
        {
            return JsonSerializer.Deserialize<ModeDocument>(File.ReadAllText(path), FileJsonOptions)
                   ?? new ModeDocument(null, []);
        }
        catch (JsonException)
        {
            return new ModeDocument(null, []);
        }
    }

    private static Cs2ModeProfile GetActiveProfileForPolicyChange(ModeDocument document)
    {
        var profile = document.ActiveProfileId is null
            ? null
            : document.Profiles.FirstOrDefault(item => string.Equals(item.Id, document.ActiveProfileId, StringComparison.Ordinal));
        return profile ?? throw new InvalidOperationException(
            "Select and activate a CS2 map profile before changing its live gameplay policies.");
    }

    private static Task PersistUpdatedProfileAsync(
        GameServerInstance server,
        ModeDocument document,
        Cs2ModeProfile profile,
        CancellationToken cancellationToken)
    {
        var profiles = document.Profiles
            .Select(item => string.Equals(item.Id, profile.Id, StringComparison.Ordinal) ? profile : item)
            .ToArray();
        return WriteJsonAtomicallyAsync(
            GetModeDocumentPath(server),
            document with { Profiles = profiles },
            cancellationToken);
    }

    private static PackageMarker ReadPackageMarker(GameServerInstance server, string packageId)
    {
        var path = GetPackageMarkerPath(server, packageId);
        if (!File.Exists(path))
        {
            return new PackageMarker(false, null, null);
        }

        try
        {
            return JsonSerializer.Deserialize<PackageMarker>(File.ReadAllText(path), FileJsonOptions)
                   ?? new PackageMarker(false, null, null);
        }
        catch (JsonException)
        {
            return new PackageMarker(false, null, null);
        }
    }

    private static Task WritePackageMarkerAsync(
        GameServerInstance server,
        PackageMarker marker,
        string packageId,
        CancellationToken cancellationToken) =>
        WriteJsonAtomicallyAsync(GetPackageMarkerPath(server, packageId), marker, cancellationToken);

    private static string GetCsgoRoot(GameServerInstance server) => Path.Combine(server.InstallDirectory, "game", "csgo");
    private static string GetCfgRoot(GameServerInstance server) => Path.Combine(GetCsgoRoot(server), "cfg");
    private static string GetModeDocumentPath(GameServerInstance server) => Path.Combine(GetCfgRoot(server), "dkay", "modes.json");
    private static string GetPackageMarkerPath(GameServerInstance server, string packageId) =>
        Path.Combine(GetCsgoRoot(server), "addons", ".dkay", $"{packageId}.json");

    private static async Task WriteAllLinesAtomicallyAsync(string path, IEnumerable<string> lines, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        await File.WriteAllLinesAsync(temporary, lines.ToArray(), new UTF8Encoding(false), cancellationToken);
        File.Move(temporary, path, true);
    }

    private static async Task WriteJsonAtomicallyAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
        {
            await JsonSerializer.SerializeAsync(stream, value, FileJsonOptions, cancellationToken);
        }

        File.Move(temporary, path, true);
    }

    private sealed record ModeDocument(string? ActiveProfileId, IReadOnlyList<Cs2ModeProfile> Profiles);
    private sealed record PackageMarker(bool Installed, string? Version, DateTimeOffset? InstalledAt);
    private sealed record PackageSource(PackageSourceKind Kind, string? Repository);
    private sealed record PackageDownload(Uri Url, string Version);
    private sealed record PackageDeployment(string SourceRoot, string DestinationRoot);

    private enum PackageSourceKind
    {
        GitHubRelease,
        MetamodSnapshot
    }

    [GeneratedRegex("^mmsource-[A-Za-z0-9._-]+-(windows|linux)\\.zip$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex MetamodFilePattern();

    [GeneratedRegex("^-?[0-9]+(?:\\.[0-9]+)?$", RegexOptions.CultureInvariant)]
    private static partial Regex NumericValuePattern();

    [GeneratedRegex("^[1-9][0-9]{0,19}$", RegexOptions.CultureInvariant)]
    private static partial Regex WorkshopIdPattern();

    [GeneratedRegex("(?<![A-Za-z0-9_-])(?<map>(?:surf|kz|bhop|de|cs|aim|awp|fy|ka|mg|ze|zm|jb|gg|dm|training)_[A-Za-z0-9_-]+)(?![A-Za-z0-9_-])", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex WorkshopMapNamePattern();
}
