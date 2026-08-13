using DKay.GameServerDock.Application.Services;
using DKay.GameServerDock.Domain;

namespace DKay.GameServerDock.Tests;

public sealed class ServerStateMachineTests
{
    [Theory]
    [InlineData(ServerStatus.Installing, ServerStatus.Stopped)]
    [InlineData(ServerStatus.Stopped, ServerStatus.Starting)]
    [InlineData(ServerStatus.Starting, ServerStatus.Running)]
    [InlineData(ServerStatus.Running, ServerStatus.Stopping)]
    [InlineData(ServerStatus.Stopping, ServerStatus.Stopped)]
    [InlineData(ServerStatus.Running, ServerStatus.Crashed)]
    [InlineData(ServerStatus.Error, ServerStatus.Starting)]
    public void Allows_expected_lifecycle_transitions(ServerStatus from, ServerStatus to)
    {
        Assert.True(ServerStateMachine.CanTransition(from, to));
    }

    [Theory]
    [InlineData(ServerStatus.Installing, ServerStatus.Running)]
    [InlineData(ServerStatus.Stopped, ServerStatus.Stopping)]
    [InlineData(ServerStatus.Running, ServerStatus.Installing)]
    [InlineData(ServerStatus.Updating, ServerStatus.Running)]
    public void Rejects_invalid_lifecycle_transitions(ServerStatus from, ServerStatus to)
    {
        Assert.Throws<InvalidOperationException>(() => ServerStateMachine.EnsureCanTransition(from, to));
    }
}
