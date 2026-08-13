# First Windows server test checklist

Use this checklist after installing a release with `Setup.cmd`. No build tools are required on the server PC.

## 1. Complete the setup wizard

Recommended first choices:

- default application, data and game-server directories
- CS2 support enabled so SteamCMD is installed
- Minecraft support only when you want to test Paper
- LAN administrator firewall enabled
- public guest page enabled only after a MyFRITZ/DynDNS hostname exists

Setup must finish with **installed and healthy**. It writes `C:\ProgramData\DKayGameServerDock\installation-summary.txt` with the local/LAN URLs and remaining router steps.

## 2. Bootstrap the administrator

1. Open `http://localhost:5080` on the server PC.
2. Create the first local administrator.
3. Open **Host** and run readiness checks.
4. Confirm application data and game-server storage are writable.
5. Confirm SteamCMD is ready for CS2 and Java is ready when Minecraft support was selected.
6. Open the displayed LAN URL from a phone or another PC on the same private network.

Do not create a router forwarding rule for administrator port `5080`.

## 3. Test Counter-Strike 2

1. Open **Game library → Counter-Strike 2 → Create server**.
2. Use a clear test name, the default port `27015`, an initial stock map and enough RAM.
3. Create the server and watch SteamCMD progress until its state is `Stopped`.
4. Open **Modes & maps**, select Classic first, apply a stock map without extra plugins and start the server.
5. Open **Live control**, store the App `730` GSLT, apply the desired private practice settings and run the self-test.
6. For Workshop maps, open **Modes & maps**, store a separate Steam Web API key, search the live CS2 Workshop and use **Add & activate**. Start the stopped server and confirm that the live console reports the Workshop download and a non-empty map.
7. Use **Start match**, **Add CT bot**, **Kill bots** and **Restart round** once to verify guided RCON controls without a gaming PC.
8. Confirm console output appears and stop the server through the UI.
9. Then test Surf/Bhop/KZ and the managed package stack separately.

Testing a vanilla profile before third-party packages distinguishes base installation failures from mod compatibility problems.

## 4. Optional Minecraft Paper test

1. Open **Game library → Minecraft Paper → Create server**.
2. Use `latest`, 2–4 GB RAM and port `25565`.
3. Accept the Minecraft EULA explicitly.
4. Wait for `Stopped`, start the server and let the first world generation finish.
5. Send `say DKayGameServerDock test` in the console, then stop it through the UI.

## 5. Test friend access

1. In the FRITZ!Box, forward only TCP guest port `5081` to the server PC.
2. Open `http://<myfritz-host>:5081/join` from a phone with Wi-Fi disabled.
3. In the LAN administrator UI, publish a stopped test server and confirm it appears as offline.
4. Add the game's separate Windows Firewall and FRITZ!Box TCP/UDP rule shown in server detail.
5. Start the server and ask a friend to copy the address from the guest page and connect.

Never configure the server PC as an exposed host and never forward `5080`.

## 6. Capture failures

Keep these details together:

- `C:\ProgramData\DKayGameServerDock\installation-summary.txt`
- screenshot of **Host → Installation readiness**
- `Get-Service DKayGameServerDock | Format-List *`
- `Get-CimInstance Win32_Service -Filter "Name='DKayGameServerDock'" | Select Name,State,StartName,PathName`
- server status, `LastError` and last console lines
- relevant Windows Event Viewer entries

That information separates service identity/ACL, runtime, download, game process, mod and network failures quickly.
