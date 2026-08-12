using DKay.GameServerDock.Domain;

namespace DKay.GameServerDock.Application.Models;

public sealed record GameTemplateDescriptor(
    string Id,
    string Name,
    string Description,
    string Category,
    string Icon,
    string Installer,
    int DefaultPort,
    int DefaultRamMb,
    GameCapability Capabilities,
    IReadOnlyList<TemplateSettingDefinition> Settings);

public sealed record TemplateSettingDefinition(
    string Key,
    string Label,
    string Type,
    bool Required,
    string? DefaultValue = null,
    IReadOnlyList<string>? Options = null,
    bool Secret = false);

