# Windows 11 hosting

## Recommended layout

```text
C:\Program Files\DKayGameServerDock\   application binaries
C:\ProgramData\DKayGameServerDock\    SQLite and application data
C:\GameServers\                       one directory per server instance
C:\Tools\SteamCMD\steamcmd.exe        Steam installer runtime
```

All paths can be overridden. Do not store game files below the Git checkout.

## Recommended installation

Use the release ZIP and `Setup.cmd` described in [Install on Windows](install-windows.md). The wizard performs host preparation, runtime discovery, service configuration, least-privilege ACLs, firewall setup and health validation.

The default service identity is `NT AUTHORITY\LocalService`, not `LocalSystem`. Configuration is attached to the Windows service in its registry `Environment` value; global machine environment variables are not required.

## Publish and install

Developers can build and launch the identical package from an elevated PowerShell terminal in the repository:

```powershell
.\scripts\test-windows-host.ps1 -IncludeBuildTools
.\scripts\package-windows.ps1
.\artifacts\package-win-x64\Setup.cmd
```

The default service listens on `0.0.0.0:5080`. The installation script waits until `/health` responds and can create a Windows Firewall rule limited to the local subnet. Browse to `http://SERVER-LAN-IP:5080`, create the local administrator and open **Host → Run checks** before installing a game.

Do not change the service account in `services.msc` without granting the replacement identity the same explicit filesystem permissions.

## Firewall

Open only the ports you actually need on the private network:

- TCP 5080 for the Dock UI/API
- each game's TCP/UDP ports
- SSH only if Windows OpenSSH administration is desired

Do not create a router port-forward for 5080. The application does not attempt UPnP or router configuration.

If the install script was run without `-OpenLanFirewall`, create the rule manually. Example LAN-only firewall rule; adjust the local subnet:

```powershell
New-NetFirewallRule -DisplayName 'DKay Game Server Dock (LAN)' -Direction Inbound -Action Allow -Protocol TCP -LocalPort 5080 -RemoteAddress 192.168.0.0/16
```

## Updates

1. Back up `%ProgramData%\DKayGameServerDock` before important upgrades.
2. Download and extract the newer release.
3. Run `Setup.cmd`, keep or update the existing values and confirm.
4. Setup stops the service, backs up SQLite, mirrors the application payload, reapplies its restricted permissions and checks `/health`.

Game server files and application data are outside the publish directory, so application deployment does not overwrite them.

## Troubleshooting

- **UI opens locally but not from another device:** check Windows Firewall and confirm the service listens on `0.0.0.0:5080`.
- **CS2 installation immediately fails:** verify the service account can execute the exact `DGS_STEAMCMD_PATH` and write to `C:\GameServers`.
- **CS2 reports that no Steam client could be found:** update the Dock and start the instance again. The Dock now provisions SteamCMD's three 64-bit runtime DLLs automatically before each Windows start. If provisioning reports missing files, run the configured `steamcmd.exe +quit` once and retry.
- **Verify CS2 without a gaming PC:** open the instance's **Console** tab and run **Run self-test**. A green result proves that the process, local game port, RCON authentication and command execution are working.
- **Paper installation works but start fails:** run the configured Java executable as the service account and check the required Java major version.
- **Metrics are empty:** confirm the service account can enumerate fixed drives and network adapters.
- **Host readiness reports a missing runtime:** set `DGS_JAVA_PATH` or `DGS_STEAMCMD_PATH` as a machine environment variable and restart the Windows service.
- **A server remains `Running` after a Dock restart:** process reattachment is not in the first MVP; stop the orphaned process through Windows administration before starting it again in the Dock.
