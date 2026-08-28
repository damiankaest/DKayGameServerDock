using System.Globalization;
using System.Text.Json;
using DKay.GameServerDock.Application.Models;
using DKay.GameServerDock.Domain;

namespace DKay.GameServerDock.Application.Services;

public static class ServerPublicationSettings
{
    private const string PublishedKey = "_dock.public.published";
    private const string PublicPortKey = "_dock.public.port";
    private const string ExternalInstallationKey = "_dock.storage.external";

    public static ServerPublicationState Read(GameServerInstance server)
    {
        var settings = ReadSettings(server.SettingsJson);
        var published = settings.TryGetValue(PublishedKey, out var publishedValue) &&
                        bool.TryParse(publishedValue, out var parsedPublished) &&
                        parsedPublished;
        var publicPort = settings.TryGetValue(PublicPortKey, out var portValue) &&
                         int.TryParse(portValue, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedPort) &&
                         parsedPort is >= 1 and <= 65535
            ? parsedPort
            : server.Port;

        return new ServerPublicationState(published, publicPort);
    }

    public static string Apply(GameServerInstance server, UpdateServerPublicationRequest request)
    {
        var publicPort = request.PublicPort ?? server.Port;
        if (publicPort is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "The public port must be between 1 and 65535.");
        }

        var settings = ReadSettings(server.SettingsJson);
        settings[PublishedKey] = request.Published ? "true" : "false";
        settings[PublicPortKey] = publicPort.ToString(CultureInfo.InvariantCulture);
        return JsonSerializer.Serialize(settings);
    }

    public static string MergeGameSettings(
        GameServerInstance server,
        IReadOnlyDictionary<string, string> gameSettings)
    {
        var publication = Read(server);
        var merged = new Dictionary<string, string>(gameSettings, StringComparer.Ordinal);
        merged[PublishedKey] = publication.Published ? "true" : "false";
        merged[PublicPortKey] = publication.PublicPort.ToString(CultureInfo.InvariantCulture);
        if (IsExternalInstallation(server))
        {
            merged[ExternalInstallationKey] = "true";
        }
        return JsonSerializer.Serialize(merged);
    }

    public static string MarkExternalInstallation(IReadOnlyDictionary<string, string> gameSettings)
    {
        var settings = new Dictionary<string, string>(gameSettings, StringComparer.Ordinal)
        {
            [ExternalInstallationKey] = "true"
        };
        return JsonSerializer.Serialize(settings);
    }

    public static bool IsExternalInstallation(GameServerInstance server)
    {
        var settings = ReadSettings(server.SettingsJson);
        return settings.TryGetValue(ExternalInstallationKey, out var value) &&
               bool.TryParse(value, out var external) &&
               external;
    }

    public static void RemoveMetadata(IDictionary<string, string> settings)
    {
        settings.Remove(PublishedKey);
        settings.Remove(PublicPortKey);
        settings.Remove(ExternalInstallationKey);
    }

    private static Dictionary<string, string> ReadSettings(string settingsJson) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(settingsJson) ?? [];
}
