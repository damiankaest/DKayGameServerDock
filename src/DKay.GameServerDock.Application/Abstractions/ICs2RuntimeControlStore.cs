using DKay.GameServerDock.Application.Models;
using DKay.GameServerDock.Domain;

namespace DKay.GameServerDock.Application.Abstractions;

public interface ICs2RuntimeControlStore
{
    IReadOnlyList<Cs2LiveSettingDescriptor> SettingDefinitions { get; }
    IReadOnlyDictionary<string, string> ReadLiveSettings(GameServerInstance server);
    IReadOnlyDictionary<string, string> SaveLiveSettings(
        GameServerInstance server,
        IReadOnlyDictionary<string, string> values);
    string? ReadCombatModeOverride(GameServerInstance server);
    void SaveCombatModeOverride(GameServerInstance server, string combatMode);
    Cs2GsltState GetGsltState(GameServerInstance server);
    Cs2GsltState SaveGsltToken(GameServerInstance server, string token);
}
