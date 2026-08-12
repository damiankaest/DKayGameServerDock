using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace DKay.GameServerDock.Api.Hubs;

[Authorize]
public sealed class ServerEventsHub : Hub
{
    public Task JoinServer(Guid serverId) => Groups.AddToGroupAsync(Context.ConnectionId, GroupName(serverId));

    public Task LeaveServer(Guid serverId) => Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(serverId));

    public static string GroupName(Guid serverId) => $"server:{serverId:N}";
}

