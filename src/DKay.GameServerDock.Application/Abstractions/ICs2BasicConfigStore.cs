using DKay.GameServerDock.Application.Models;
using DKay.GameServerDock.Domain;

namespace DKay.GameServerDock.Application.Abstractions;

public interface ICs2BasicConfigStore
{
    Cs2BasicConfiguration Read(GameServerInstance server);
    Cs2BasicConfiguration Save(GameServerInstance server, Cs2BasicConfiguration configuration);
    void Prepare(GameServerInstance server);
}
