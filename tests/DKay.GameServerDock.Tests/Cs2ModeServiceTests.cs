using DKay.GameServerDock.Application.Abstractions;
using DKay.GameServerDock.Application.Models;
using DKay.GameServerDock.Application.Services;
using DKay.GameServerDock.Domain;

namespace DKay.GameServerDock.Tests;

public sealed class Cs2ModeServiceTests
{
    [Fact]
    public async Task Failed_optional_mod_install_keeps_base_server_startable()
    {
        var now = DateTimeOffset.UtcNow;
        var server = GameServerInstance.Create(
            Guid.NewGuid(),
            "CS2 test",
            "counter-strike-2",
            Path.Combine(Path.GetTempPath(), $"dkay-mode-service-{Guid.NewGuid():N}"),
            "latest",
            27015,
            null,
            null,
            4096,
            "{}",
            now);
        server.ChangeStatus(ServerStatus.Stopped, now);
        var repository = new TestServerRepository(server);
        var service = new Cs2ModeService(repository, new FailingModeManager(), new TestEventSink(), new TestClock(now));

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            service.InstallPackageAsync(server.Id, "broken-package", CancellationToken.None));

        Assert.Equal("broken archive", exception.Message);
        Assert.Equal(ServerStatus.Stopped, server.Status);
        Assert.Equal("broken archive", server.LastError);
        Assert.True(ServerStateMachine.CanTransition(server.Status, ServerStatus.Starting));
    }

    private sealed class FailingModeManager : ICs2ModeManager
    {
        public IReadOnlyList<Cs2ModePresetDescriptor> Presets => [];
        public IReadOnlyList<Cs2ManagedPackageDescriptor> Packages => [];
        public Cs2ModeProfile? GetActiveProfile(GameServerInstance server) => null;
        public Task<Cs2ModeState> GetStateAsync(GameServerInstance server, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<Cs2ModeState> ApplyPresetAsync(GameServerInstance server, ApplyCs2ModePresetRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<Cs2ModeProfile> ActivateProfileAsync(GameServerInstance server, string profileId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<Cs2ModeProfile> SetActiveCombatModeAsync(GameServerInstance server, string combatMode, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<Cs2ModeProfile> SetActiveRespawnModeAsync(GameServerInstance server, string respawnMode, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<Cs2ModeProfile> SetActiveHudModeAsync(GameServerInstance server, string hudMode, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Cs2WorkshopAccessState GetWorkshopAccessState(GameServerInstance server) =>
            new(false, null, false, "not configured");
        public Cs2WorkshopAccessState SaveWorkshopApiKey(GameServerInstance server, string key) =>
            throw new NotSupportedException();
        public Task<Cs2WorkshopSearchResult> SearchWorkshopMapsAsync(GameServerInstance server, string query, int take, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task RepairAfterGameUpdateAsync(GameServerInstance server, CancellationToken cancellationToken) => Task.CompletedTask;
        public IReadOnlyList<string> ResolveAutomaticInstallOrder(IEnumerable<string> packageIds) => packageIds.ToArray();
        public Task InstallPackageAsync(
            GameServerInstance server,
            string packageId,
            Func<InstallationProgress, CancellationToken, Task> reportProgress,
            CancellationToken cancellationToken) => throw new InvalidDataException("broken archive");
    }

    private sealed class TestServerRepository(GameServerInstance server) : IServerRepository
    {
        public Task<IReadOnlyList<GameServerInstance>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<GameServerInstance>>([server]);
        public Task<GameServerInstance?> FindAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<GameServerInstance?>(id == server.Id ? server : null);
        public Task AddAsync(GameServerInstance value, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SaveAsync(GameServerInstance value, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task DeleteAsync(GameServerInstance value, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<bool> IsPortAllocatedAsync(int port, CancellationToken cancellationToken) => Task.FromResult(false);
        public Task<IReadOnlyList<ServerEvent>> GetEventsAsync(Guid? serverId, int take, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ServerEvent>>([]);
        public Task AddEventAsync(ServerEvent serverEvent, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class TestEventSink : IServerEventSink
    {
        public Task RecordAsync(ServerEvent serverEvent, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task PublishInstallationProgressAsync(Guid serverId, InstallationProgress progress, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task PublishStatusAsync(Guid serverId, ServerStatus status, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class TestClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }
}
