# Windows 11 hosting

## Recommended layout

```text
C:\Program Files\DKayGameServerDock\   application binaries
C:\ProgramData\DKayGameServerDock\    SQLite and application data
C:\GameServers\                       one directory per server instance
C:\Tools\SteamCMD\steamcmd.exe        Steam installer runtime
```

All paths can be overridden. Do not store game files below the Git checkout.

## Prepare the host

1. Install the .NET 10 ASP.NET Core Runtime and Node.js only if building on the host.
2. Install SteamCMD into a dedicated tools directory.
3. Install the Java version required by the desired Paper build.
4. Create a local service account, for example `DKayDockService`.
5. Grant that account modify permission on the application-data and server roots, and read/execute permission on SteamCMD and Java.
6. Set `DGS_STEAMCMD_PATH` and `DGS_JAVA_PATH` as machine environment variables.

## Publish and install

Run from an elevated PowerShell terminal in the repository:

```powershell
.\scripts\publish-windows.ps1
.\scripts\install-windows-service.ps1
```

The default service listens on `0.0.0.0:5080`. Browse to `http://SERVER-LAN-IP:5080`, create the local administrator and verify host metrics before installing a game.

To run under a dedicated account, update the Windows service in `services.msc` after installation and provide that account's password there. The install script intentionally does not accept credentials.

## Firewall

Open only the ports you actually need on the private network:

- TCP 5080 for the Dock UI/API
- each game's TCP/UDP ports
- SSH only if Windows OpenSSH administration is desired

Do not create a router port-forward for 5080. The application does not attempt UPnP or router configuration.

Example LAN-only firewall rule; adjust the local subnet:

```powershell
New-NetFirewallRule -DisplayName 'DKay Game Server Dock (LAN)' -Direction Inbound -Action Allow -Protocol TCP -LocalPort 5080 -RemoteAddress 192.168.0.0/16
```

## Updates

1. Stop the Windows service.
2. Back up `%ProgramData%\DKayGameServerDock`.
3. Publish the new application over the program directory.
4. Start the service and check `/health` plus the dashboard.

Game server files and application data are outside the publish directory, so application deployment does not overwrite them.

## Troubleshooting

- **UI opens locally but not from another device:** check Windows Firewall and confirm the service listens on `0.0.0.0:5080`.
- **CS2 installation immediately fails:** verify the service account can execute the exact `DGS_STEAMCMD_PATH` and write to `C:\GameServers`.
- **Paper installation works but start fails:** run the configured Java executable as the service account and check the required Java major version.
- **Metrics are empty:** confirm the service account can enumerate fixed drives and network adapters.
- **A server remains `Running` after a Dock restart:** process reattachment is not in the first MVP; stop the orphaned process through Windows administration before starting it again in the Dock.

