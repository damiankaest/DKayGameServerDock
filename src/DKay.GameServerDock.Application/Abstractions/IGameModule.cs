using DKay.GameServerDock.Application.Models;
using DKay.GameServerDock.Domain;

namespace DKay.GameServerDock.Application.Abstractions;

public interface IGameModule
{
    GameTemplateDescriptor Descriptor { get; }
    IGameInstaller Installer { get; }
    IGameServerAdapter Adapter { get; }
    ServerLaunchSpec BuildLaunchSpec(GameServerInstance server);
}

public interface IGameModuleRegistry
{
    IReadOnlyCollection<GameTemplateDescriptor> GetTemplates();
    IGameModule GetRequired(string templateId);
    bool TryGet(string templateId, out IGameModule? module);
}

