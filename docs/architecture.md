# Architecture

## Goals

DKayGameServerDock manages native dedicated-server processes on one host. It must be useful on Windows 11 now without locking the domain or application layers to Windows. The first version is a modular monolith: one deployable ASP.NET Core process, one Angular client, SQLite and local directories.

## Dependency direction

| Project | Responsibility | Depends on |
|---|---|---|
| `Domain` | Server state, persisted entities and event types | Nothing |
| `Application` | Use cases, policies, interfaces and state machine | Domain |
| `Infrastructure` | EF Core, metrics, installers, modules and process supervision | Application, Domain |
| `Api` | HTTP, authentication, SignalR, background jobs and static UI hosting | All inner projects |
| `web` | Angular UI using the public API | HTTP/SignalR contracts |

The domain has no reference to SteamCMD, Java, CS2, Minecraft, Windows or ASP.NET Core.

## Runtime flow

### Create and install

1. `POST /api/servers` validates the typed template and allocated port.
2. The orchestrator creates an instance in `Installing` state.
3. A bounded in-memory work queue receives an install item.
4. The background worker resolves the instance's registered module.
5. Its installer reports typed progress events.
6. Events are persisted and sent through SignalR.
7. Success transitions to `Stopped`; failures transition to `Error`.

### Start and stop

1. The state machine validates the requested transition.
2. Host RAM and disk headroom are checked.
3. The module produces a `ServerLaunchSpec` with a filename and separated arguments.
4. The process supervisor starts without a shell and redirects stdin/stdout/stderr.
5. Output is persisted and broadcast to the server's SignalR group.
6. A graceful stop writes only the adapter's trusted stop command to stdin. Force kill terminates the process tree.
7. Unexpected exits become `Crashed` and retain their exit code.

## Extension model

`IGameModule` is the composition root for a game. It exposes:

- a typed descriptor for UI discovery;
- an `IGameInstaller`;
- an `IGameServerAdapter`;
- a launch-spec builder.

The registry is keyed by descriptor ID. Adding a module requires DI registration, not editing switch/case logic in the orchestrator.

Capabilities are flags. They allow the UI to display common tabs and later game-provided modules such as Workshop, plugins, worlds or saves.

## Persistence

SQLite stores:

- `GameServerInstance`
- `ServerEvent`
- `LocalUser`

The MVP uses `EnsureCreated` to keep first-run deployment small. Before schema evolution begins, replace it with committed EF Core migrations. PostgreSQL can then be added through a provider-specific registration without changing the application layer.

## Known MVP limits

- The work queue is in-memory; an interrupted installation must be retried after restart.
- A running process is not reattached after the Dock service restarts.
- Console output is stored as individual events and needs retention/rotation for long-running public servers.
- Query/RCON player adapters are placeholders.
- Game settings are persisted as JSON; secrets are filtered from responses but encryption at rest is still required.
- Backups and file-manager endpoints are not implemented yet.

These are deliberate next slices rather than reasons to introduce a distributed architecture.

