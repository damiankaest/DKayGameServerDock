using DKay.GameServerDock.Application.Abstractions;
using DKay.GameServerDock.Application.Models;

namespace DKay.GameServerDock.Application.Services;

public sealed class GameModuleRegistry(IEnumerable<IGameModule> modules) : IGameModuleRegistry
{
    private readonly IReadOnlyDictionary<string, IGameModule> _modules = modules.ToDictionary(
        module => module.Descriptor.Id,
        StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<GameTemplateDescriptor> GetTemplates() =>
        _modules.Values.Select(module => module.Descriptor).OrderBy(template => template.Name).ToArray();

    public IGameModule GetRequired(string templateId) =>
        TryGet(templateId, out var module)
            ? module!
            : throw new KeyNotFoundException($"Unknown game template '{templateId}'.");

    public bool TryGet(string templateId, out IGameModule? module) => _modules.TryGetValue(templateId, out module);
}

