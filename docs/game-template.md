# Game template and module contract

A game template is the discoverable metadata of an installed `IGameModule`. It tells the UI what can be created. Executable behavior remains in trusted server-side code.

## Descriptor fields

| Field | Meaning |
|---|---|
| `Id` | Stable machine key such as `minecraft-paper` |
| `Name` | Human-readable display name |
| `Description` | Short library summary |
| `Category` | Library grouping |
| `Icon` | Small built-in mark; later replaceable by a local asset |
| `Installer` | Installation mechanism label |
| `DefaultPort` | Suggested game port |
| `DefaultRamMb` | Suggested memory limit |
| `Capabilities` | Feature flags used for dynamic UI tabs |
| `Settings` | Typed setting definitions, defaults and secret marker |

## Installer contract

`IGameInstaller.InstallAsync` and `UpdateAsync` receive the persisted instance and an asynchronous progress callback. Installers must:

- create files only within `InstallDirectory`;
- use trusted URLs or registered tools;
- pass process arguments separately;
- never include secrets in progress text;
- throw a clear exception on a failed prerequisite or non-zero installer exit code;
- be safe to retry where practical.

Current implementations:

- `PaperInstaller`: resolves the current stable Paper build from the official v3 Downloads Service, downloads `paper.jar` and writes the base configuration.
- `Cs2Installer`: delegates to `SteamCmdInstaller` with app ID 730 and writes a CS2 cfg file.

## Adapter contract

`IGameServerAdapter` owns behavior that differs after installation:

- graceful stop command;
- console-command validation;
- players;
- current map.

The MVP `BasicGameServerAdapter` implements safe console input and the stop command. RCON/query implementations will replace it for richer modules.

## Adding a game

1. Create an installer or reuse an existing installation mechanism.
2. Implement an adapter.
3. Implement `IGameModule` with descriptor and launch spec.
4. Register the module as `IGameModule` in `ServiceCollectionExtensions`.
5. Add installer/launch/state tests and a Windows installation smoke test.
6. Document runtime prerequisites and upstream license/EULA requirements.

Do not accept executable paths or raw start commands from the create-server API. A future custom template library needs signed/trusted manifests plus a constrained execution schema before it can safely become user-extensible.

