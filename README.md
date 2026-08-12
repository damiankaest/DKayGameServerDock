# DKayGameServerDock

DKayGameServerDock is a self-hosted control panel for native dedicated game servers on one Windows PC. It is the beginning of a private, game-agnostic alternative to hosted panels such as Nitrado: install a server from a template, control its process, inspect host resources, follow the live console and keep lifecycle events locally.

The project is independent from CouchClash. A later CouchClash integration can use the same REST API as the Angular UI, but is not a dependency.

> Status: early MVP. The architecture and the first vertical flow are implemented. The Angular frontend builds and tests successfully. The repository still needs a Windows host integration test before it should supervise important production servers.

## What already works

- Responsive dark-mode dashboard inspired by modern infrastructure panels
- Local administrator bootstrap and cookie-based login
- Live Windows/Linux host CPU, RAM, disk, network and uptime metrics
- First-run readiness checks for writable storage, Java and SteamCMD
- Typed game-template catalog with CS2 and Minecraft Paper
- SQLite persistence for instances, users and server events
- Server creation with background installation progress through SignalR
- Paper stable-build download through PaperMC's current v3 download service
- CS2 installation/update through a configured SteamCMD executable
- Native process start, graceful stop, force kill and restart
- Per-process PID, CPU, memory, uptime and exit code
- Persistent stdout/stderr capture and live web console
- Resource checks before start
- Argument-list based process launch (no shell command composition)
- Server-directory containment policy against path traversal
- Extension points for installers, adapters and game-specific capabilities

Player discovery, RCON/query adapters, backups, the file manager and configuration editing are deliberately visible as the next slices, not represented as finished.

## Architecture

```text
Angular UI ── REST + SignalR ── ASP.NET Core API
                                    │
                         Application orchestration
                           │                 │
                    Game modules      Process supervisor
                     │       │               │
                  Paper     CS2       native child processes
                           │
                        SQLite + local server directories
```

The core never switches on a game name. Each game supplies an `IGameModule`, an `IGameInstaller`, an `IGameServerAdapter`, a typed descriptor and a launch specification. See [docs/architecture.md](docs/architecture.md) and [docs/game-template.md](docs/game-template.md).

## Technology

- .NET 10 LTS, ASP.NET Core, C#
- EF Core 10 and SQLite
- SignalR
- Angular 21, TypeScript, SCSS
- xUnit and Vitest

## Requirements

For development:

- .NET 10 SDK
- Node.js 24 LTS and npm

For the Windows server host:

- Windows 11 x64
- No preinstalled .NET runtime when using the default self-contained Windows publish
- Java for Minecraft Paper; modern Paper versions may require Java 21 or Java 25 depending on the selected Minecraft version
- SteamCMD for Counter-Strike 2
- An unprivileged Windows service account with write access only to the configured data and game-server directories

## Development setup

Start the backend:

```powershell
dotnet restore DKayGameServerDock.slnx
dotnet run --project src/DKay.GameServerDock.Api
```

In a second terminal start Angular:

```powershell
cd src/web
npm ci
npm start
```

Open `http://localhost:4200`. On first access, create the local administrator. Angular proxies API and SignalR traffic to `http://localhost:5080`.

Run checks:

```powershell
dotnet test DKayGameServerDock.slnx
cd src/web
npm test -- --watch=false
npm run build
```

## Configuration

Configuration can be supplied through `appsettings.json` or environment variables. Environment variables win.

| Setting | Environment variable | Windows default |
|---|---|---|
| Application data | `DGS_DATA_ROOT` | `%ProgramData%\DKayGameServerDock` |
| Game servers | `DGS_SERVERS_ROOT` | `C:\GameServers` |
| SteamCMD executable | `DGS_STEAMCMD_PATH` | Must be configured |
| Java executable | `DGS_JAVA_PATH` | `java` from `PATH` |

Example:

```powershell
[Environment]::SetEnvironmentVariable('DGS_STEAMCMD_PATH', 'C:\Tools\SteamCMD\steamcmd.exe', 'Machine')
[Environment]::SetEnvironmentVariable('DGS_JAVA_PATH', 'C:\Program Files\Eclipse Adoptium\jdk-25\bin\java.exe', 'Machine')
```

No RCON password or administrator password belongs in the repository. Game-specific secrets are removed from API responses and are never written to application logs.

## Publish for Windows

From an elevated PowerShell terminal:

```powershell
.\scripts\test-windows-host.ps1 -IncludeBuildTools
.\scripts\publish-windows.ps1
.\scripts\install-windows-service.ps1 -OpenLanFirewall
```

The publish script builds Angular, copies it into the API's static assets and creates a self-contained `win-x64` package in `artifacts\win-x64`. The installation script safely stops an existing service during updates, registers automatic recovery, optionally opens a LAN-only firewall rule and waits for `/health` before reporting success. See the [first-run checklist](docs/first-run-checklist.md) and [Windows hosting guide](docs/windows-hosting.md).

## First Minecraft Paper server

1. Configure a compatible Java executable.
2. Open **Game Library → Minecraft Paper → Create server**.
3. Pick a name, version (`latest` is supported), RAM and port.
4. Explicitly accept the Minecraft EULA.
5. Create the instance and watch the installation progress.
6. Start it after the state changes to `Stopped`.

The installer uses PaperMC's stable channel and writes `eula.txt` plus a minimal `server.properties`. PaperMC requires a descriptive User-Agent, which the installer supplies.

## First Counter-Strike 2 server

1. Install SteamCMD and configure `DGS_STEAMCMD_PATH`.
2. Open **Game Library → Counter-Strike 2 → Create server**.
3. Configure name, initial map, slots, password and port.
4. Watch SteamCMD installation output in the server detail.
5. Start the instance after installation.

CS2 uses Steam app ID `730`. The generated password lives in `game\csgo\cfg\dkay-server.cfg` inside the instance; it is not passed on the process command line.

## Security boundaries

- The web panel binds to the LAN but does not open router ports.
- Login is required for all API and SignalR endpoints except health and initial authentication.
- Authentication cookies are HTTP-only and strict SameSite.
- Executables and arguments come from registered game modules, not arbitrary user input.
- `ProcessStartInfo.ArgumentList` keeps arguments separate and `UseShellExecute` is disabled.
- File operations must go through `IPathPolicy` and stay below an instance root.
- The service should run without administrator privileges after installation.
- Do not expose port 5080 directly to the internet. Add TLS and a trusted reverse proxy before any remote-access feature.

## Roadmap

1. RCON and query adapters for real player/map information
2. Editable settings with secret encryption at rest
3. Manual and scheduled backups with restore validation
4. Contained file manager with upload/download and text editing
5. CS2 Workshop maps, Metamod and CounterStrikeSharp inventory
6. Minecraft whitelist, worlds and Paper plugins
7. Crash backoff, autostart reconciliation and process reattachment
8. External API tokens for optional CouchClash integration
9. Linux host packaging

## Upstream documentation

- [PaperMC Downloads Service](https://docs.papermc.io/misc/downloads-service/)
- [Paper getting started](https://docs.papermc.io/paper/getting-started/)
- [Valve CS2 dedicated server guide](https://developer.valvesoftware.com/wiki/Counter-Strike_2/Dedicated_Servers)
- [Valve SteamCMD guide](https://developer.valvesoftware.com/wiki/SteamCMD)
