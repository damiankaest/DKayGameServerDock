# First Windows test checklist

This checklist exercises the complete first vertical slice without exposing the Dock to the internet. Minecraft Paper is the recommended first game because its installation is much smaller than Counter-Strike 2.

## 1. Prepare build tools

Install the .NET 10 SDK and Node.js 24 on the PC that builds the application. They may be removed from the server later because the produced package is self-contained.

Open an elevated PowerShell terminal in the repository and run:

```powershell
.\scripts\test-windows-host.ps1 -IncludeBuildTools
```

The application-data and game-server checks must be green. Java and SteamCMD are only required for their respective games.

## 2. Configure Minecraft Java

Install a Java version supported by the current Paper release, then save the executable path as a machine environment variable:

```powershell
[Environment]::SetEnvironmentVariable(
  'DGS_JAVA_PATH',
  'C:\Program Files\Eclipse Adoptium\jdk-25\bin\java.exe',
  'Machine')
```

Open a new elevated terminal after changing machine environment variables.

## 3. Publish and install

```powershell
.\scripts\publish-windows.ps1
.\scripts\install-windows-service.ps1 -OpenLanFirewall
```

The second command only finishes successfully after `http://127.0.0.1:5080/health` responds. Re-running it updates the existing installation safely.

## 4. Bootstrap the Dock

1. Open `http://localhost:5080` on the server PC.
2. Create the first local administrator.
3. Open **Host** and select **Run checks**.
4. Confirm both storage paths are writable and Java is ready.
5. Copy the displayed LAN address and open it from a phone or another PC on the same network.

Do not create a router port-forward for port 5080.

## 5. Install the first Paper server

1. Open **Game library → Minecraft Paper → Create server**.
2. Use a clear test name, `latest`, 2–4 GB RAM and port `25565`.
3. Accept the Minecraft EULA explicitly.
4. Create the server and watch the live installation progress.
5. Wait until its state changes from `Installing` to `Stopped`.
6. Start it and wait for the first world generation to finish in the console.
7. Send a harmless command such as `say DKayGameServerDock test`.
8. Stop the server through the UI and confirm the process and activity log update.

## 6. Optional LAN connection

Allow TCP `25565` through Windows Firewall for the local subnet, then connect Minecraft to `SERVER-LAN-IP:25565`. Game ports are intentionally not opened by the Dock installer.

## 7. Capture failures

If something fails, keep these details together:

- screenshot of **Host → Installation readiness**
- server status and `LastError`
- the last console lines
- Windows service state from `Get-Service DKayGameServerDock`
- recent service events from Windows Event Viewer

That information is enough to distinguish runtime, permissions, download and process-management failures quickly.
