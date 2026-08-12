# Install on a Windows game-server PC

The recommended installation uses the self-contained `win-x64` release package. The server PC does **not** need Git, Node.js or a .NET SDK.

## Fast installation

1. Open [GitHub Releases](https://github.com/damiankaest/DKayGameServerDock/releases) and download `DKayGameServerDock-win-x64.zip` from the newest release.
2. Right-click the ZIP, select **Properties**, choose **Unblock** when Windows shows that option, then extract it.
3. Double-click **Setup.cmd** and approve the administrator prompt.
4. Follow the six setup pages. The recommended choices work for a normal Windows 11 home server.
5. When setup finishes, the administrator page opens at `http://localhost:5080`.
6. Create the first administrator account and open **Host** to check readiness.

Re-running `Setup.cmd` from a newer release performs an in-place update/repair. Application data and installed game servers are kept outside the program directory.

## What the wizard configures

| Step | Result |
|---|---|
| Application | Copies the self-contained backend and Angular UI to `C:\Program Files\DKayGameServerDock` |
| Data | Creates `C:\ProgramData\DKayGameServerDock` for SQLite and setup information |
| Game storage | Creates `C:\GameServers` for independently managed instances |
| Windows service | Registers automatic start/recovery as restricted `NT AUTHORITY\LocalService` |
| Permissions | Grants modify access to data/game/SteamCMD directories and read/execute access to application/Java directories |
| CS2 | Detects or downloads Valve SteamCMD into `C:\Tools\SteamCMD` |
| Minecraft | Detects Java or offers Microsoft OpenJDK 21 LTS through `winget` |
| LAN access | Creates a TCP firewall rule for the administrator port restricted to `LocalSubnet` |
| Guest access | Optionally creates a separate public guest-list listener and firewall rule |
| Verification | Starts the service, waits for `/health`, writes an installation summary and opens the UI |

The wizard never modifies the FRITZ!Box, enables UPnP or exposes the entire PC. Router changes stay explicit.

## Public guest page with a FRITZ!Box 5690 Pro

When asked about friend access:

1. Enable the read-only guest server list.
2. Enter the MyFRITZ or DynDNS hostname without `http://`, for example `your-name.myfritz.net`.
3. Keep guest port `5081` unless it is already used.
4. After setup, create one FRITZ!Box TCP forwarding rule from external port `5081` to this PC on internal port `5081`.
5. Never forward administrator port `5080`.

The public page will be `http://your-name.myfritz.net:5081/join`. It stays empty until an administrator explicitly publishes a server. Each game additionally needs its own TCP/UDP forwarding rule; the server detail page shows the required protocol and port.

See [FRITZ!Box public access](fritzbox-public-access.md) for IPv4, IPv6 and DS-Lite checks.

## One-script installation

After the first GitHub release exists, an elevated PowerShell can download and launch its verified release structure:

```powershell
$installer = Join-Path $env:TEMP 'Install-DKayGameServerDock.ps1'
Invoke-WebRequest `
  'https://raw.githubusercontent.com/damiankaest/DKayGameServerDock/main/Install-DKayGameServerDock.ps1' `
  -OutFile $installer
powershell.exe -ExecutionPolicy Bypass -File $installer
```

This bootstrap script resolves the latest GitHub release, accepts only the expected `github.com` release asset URLs, verifies the ZIP against its release SHA-256 file, extracts it into a temporary directory and launches the same interactive wizard. Downloading the ZIP manually is easier to audit and remains the recommended method.

## Updating

1. Download and extract the newer release ZIP.
2. Run its `Setup.cmd`.
3. Keep the displayed existing values or change the desired setting.
4. Confirm. The wizard stops the service and saves a timestamped SQLite copy under `setup-backups`.
5. It replaces only application binaries, reapplies configuration and validates health.

Before an important upgrade, back up `C:\ProgramData\DKayGameServerDock`. Game files in `C:\GameServers` are not overwritten by an application update.

## Unattended/source installation for developers

Building from source is only needed for contributors. Install .NET 10 SDK and Node.js 24, then run from an elevated PowerShell:

```powershell
.\scripts\package-windows.ps1
.\artifacts\package-win-x64\Setup.cmd
```

The lower-level service installer remains available for automation:

```powershell
.\scripts\install-windows-service.ps1 `
  -SourceDirectory '.\artifacts\win-x64' `
  -DataRoot 'C:\ProgramData\DKayGameServerDock' `
  -ServersRoot 'C:\GameServers' `
  -SteamCmdPath 'C:\Tools\SteamCMD\steamcmd.exe' `
  -OpenLanFirewall `
  -EnablePublicPortal `
  -PublicHost 'your-name.myfritz.net'
```

## Troubleshooting

- **Setup closes immediately:** open an elevated PowerShell, change into the extracted folder and run `.\Setup-DKayGameServerDock.ps1` to keep the full error visible.
- **Windows blocks the scripts:** unblock the downloaded ZIP before extracting, or use `Unblock-File .\Setup-DKayGameServerDock.ps1`.
- **Health check fails:** run `Get-Service DKayGameServerDock`, inspect Windows Event Viewer and open `C:\ProgramData\DKayGameServerDock\installation-summary.txt`.
- **UI works only locally:** verify the wizard created `DKay Game Server Dock UI (LAN)` and that Windows categorizes the active network as Private.
- **CS2 readiness is yellow:** confirm `C:\Tools\SteamCMD\steamcmd.exe` exists and rerun setup with CS2 support enabled.
- **Minecraft readiness is yellow:** run `java -version`, rerun setup and either install Microsoft OpenJDK or enter the full `java.exe` path.
- **Guest page works but friends cannot join a game:** the guest page and game server use separate ports. Add the game's indicated TCP/UDP Windows Firewall and FRITZ!Box rules.

The SteamCMD URL comes from Valve's official SteamCMD documentation. The optional Java installation uses Microsoft's documented `Microsoft.OpenJDK.21` winget package.
