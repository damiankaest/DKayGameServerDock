using DKay.GameServerDock.Application.Abstractions;
using DKay.GameServerDock.Application.Models;
using DKay.GameServerDock.Domain;

namespace DKay.GameServerDock.Application.Services;

public sealed class Cs2BasicControlService(
    IServerRepository servers,
    IGameModuleRegistry modules,
    IProcessSupervisor processes,
    ICs2BasicConfigStore configStore,
    IServerEventSink events,
    IClock clock)
{
    private static readonly IReadOnlyDictionary<string, Func<Cs2BasicConfiguration, string>> ExpectedValues =
        new Dictionary<string, Func<Cs2BasicConfiguration, string>>(StringComparer.Ordinal)
        {
            ["sv_enablebunnyhopping"] = configuration => configuration.AutoBhop ? "1" : "0",
            ["sv_autobunnyhopping"] = configuration => configuration.AutoBhop ? "1" : "0",
            ["sv_gravity"] = configuration => configuration.Gravity.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["bot_quota"] = configuration => configuration.BotQuota.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };

    public async Task<Cs2BasicConfigurationState> GetAsync(Guid serverId, CancellationToken cancellationToken)
    {
        var server = await GetCs2ServerAsync(serverId, cancellationToken);
        configStore.Prepare(server);
        var running = server.Status == ServerStatus.Running && processes.GetSnapshot(server.Id).IsRunning;
        return new Cs2BasicConfigurationState(
            configStore.Read(server),
            running,
            false,
            running
                ? "Gespeicherte Basic-Konfiguration. Mit Anwenden wird sie live gesetzt und geprüft."
                : "Gespeicherte Basic-Konfiguration. Sie wird beim nächsten Start zuletzt geladen.",
            new Dictionary<string, string>(StringComparer.Ordinal),
            null);
    }

    public async Task<Cs2BasicConfigurationState> SaveAsync(
        Guid serverId,
        SaveCs2BasicConfigurationRequest request,
        CancellationToken cancellationToken)
    {
        var server = await GetCs2ServerAsync(serverId, cancellationToken);
        var configuration = Validate(request);
        configStore.Save(server, configuration);

        var running = server.Status == ServerStatus.Running && processes.GetSnapshot(server.Id).IsRunning;
        if (!running)
        {
            const string savedMessage = "Basic-Konfiguration gespeichert. Sie wird beim nächsten Start zuletzt geladen.";
            await RecordChangeAsync(server, savedMessage, cancellationToken);
            return new Cs2BasicConfigurationState(
                configuration,
                false,
                false,
                savedMessage,
                new Dictionary<string, string>(StringComparer.Ordinal),
                null);
        }

        var adapter = modules.GetRequired(server.TemplateId).Adapter;
        var applyResult = await adapter.ExecuteConsoleCommandAsync(
            server,
            processes,
            adapter.NormalizeConsoleCommand("exec dkay-basic.cfg"),
            cancellationToken);
        var observed = new Dictionary<string, string>(StringComparer.Ordinal);
        var outputs = new List<string>();
        if (!string.IsNullOrWhiteSpace(applyResult.Output)) outputs.Add(applyResult.Output);

        foreach (var key in ExpectedValues.Keys)
        {
            var result = await adapter.ExecuteConsoleCommandAsync(
                server,
                processes,
                adapter.NormalizeConsoleCommand(key),
                cancellationToken);
            if (!string.IsNullOrWhiteSpace(result.Output)) outputs.Add(result.Output);
            if (Cs2LiveControlService.TryReadConsoleVariable(key, result.Output, out var value))
            {
                observed[key] = value.Trim().Trim('"');
            }
        }

        var failures = ExpectedValues
            .Where(pair => !observed.TryGetValue(pair.Key, out var value) ||
                           !ValuesMatch(pair.Value(configuration), value))
            .Select(pair => pair.Key)
            .ToArray();
        var applied = failures.Length == 0;
        var message = applied
            ? "Basic-Konfiguration gespeichert, live angewendet und aus CS2 zurückgelesen."
            : $"Basic-Konfiguration wurde gespeichert, aber die Live-Prüfung ist fehlgeschlagen: {string.Join(", ", failures)}.";
        await RecordChangeAsync(server, message, cancellationToken);
        return new Cs2BasicConfigurationState(
            configuration,
            true,
            applied,
            message,
            observed,
            string.Join(Environment.NewLine, outputs));
    }

    private async Task<GameServerInstance> GetCs2ServerAsync(Guid serverId, CancellationToken cancellationToken)
    {
        var server = await servers.FindAsync(serverId, cancellationToken)
            ?? throw new KeyNotFoundException($"Server '{serverId}' was not found.");
        if (!string.Equals(server.TemplateId, "counter-strike-2", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Die persistente Basic-Konfiguration ist zunächst nur für CS2 verfügbar.");
        }

        return server;
    }

    private static Cs2BasicConfiguration Validate(SaveCs2BasicConfigurationRequest request)
    {
        if (request.Gravity is < 100 or > 2000)
        {
            throw new InvalidOperationException("Gravity muss zwischen 100 und 2000 liegen.");
        }

        if (request.BotQuota is < 0 or > 32)
        {
            throw new InvalidOperationException("Die Bot-Anzahl muss zwischen 0 und 32 liegen.");
        }

        return new Cs2BasicConfiguration(request.AutoBhop, request.Gravity, request.BotQuota);
    }

    private static bool ValuesMatch(string expected, string actual) =>
        decimal.TryParse(expected, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var expectedNumber) &&
        decimal.TryParse(actual, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var actualNumber)
            ? expectedNumber == actualNumber
            : string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase);

    private Task RecordChangeAsync(GameServerInstance server, string message, CancellationToken cancellationToken) =>
        events.RecordAsync(
            ServerEvent.Create(server.Id, ServerEventType.ConfigurationChanged, message, clock.UtcNow),
            cancellationToken);
}
