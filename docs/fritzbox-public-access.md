# Publish game servers through FRITZ!Box 5690 Pro

The Dock uses two intentionally separate listeners:

- `5080`: administrator panel for the LAN only
- `5081`: read-only guest portal that only exposes `/join` and `/api/public/servers`

Never forward port `5080`, never enable automatic UPnP/PCP port forwarding for the server PC and never configure it as an exposed host.

## 1. Check internet reachability

In the FRITZ!Box open **Internet → Online-Monitor** and check whether the connection has a public IPv4 address and/or a reachable IPv6 prefix. With DS-Lite, inbound IPv4 may be unavailable unless the provider supports PCP. In that case use IPv6-capable clients, request a public IPv4/dual-stack option from the provider or use a VPN/relay solution.

## 2. Give the server PC a stable home-network address

Open **Heimnetz → Netzwerk → Netzwerkverbindungen**, edit the Windows server and enable the option to always assign the same IPv4 address. This keeps port-forwarding rules stable.

## 3. Configure a stable public name

Use MyFRITZ or another DynDNS provider. Set the resulting host name while installing the Dock service:

```powershell
.\scripts\install-windows-service.ps1 `
  -OpenLanFirewall `
  -EnablePublicPortal `
  -PublicHost 'your-name.myfritz.net'
```

The public host is displayed to guests, so enter only a host name or IP address—never a URL, path or credentials.

## 4. Windows Firewall

The service installer creates the guest-portal firewall rule automatically when `-EnablePublicPortal` is used. The administrator rule created with `-OpenLanFirewall` remains limited to `LocalSubnet`.

For a published game server, open only its required game port. Examples:

```powershell
.\scripts\set-game-port-firewall.ps1 -ServerName 'Friends Minecraft' -Port 25565 -Protocol TCP
.\scripts\set-game-port-firewall.ps1 -ServerName 'Friends CS2' -Port 27015 -Protocol Both
```

Use the same script with `-Remove` when a server is retired.

## 5. Add FRITZ!Box forwarding rules

Open **Internet → Freigaben → Portfreigaben**, select the Windows server and create only the required rules:

| Purpose | Protocol | External port | Port on device |
|---|---:|---:|---:|
| Guest server list | TCP | 5081 | 5081 |
| Minecraft Paper default | TCP | 25565 | 25565 |
| Counter-Strike 2 default | UDP | 27015 | 27015 |
| Counter-Strike 2 compatibility/query | TCP | 27015 | 27015 |

Only add a game's rule after that individual server has been published in the Dock. Remove the rule when it is no longer needed. Custom game ports require matching custom rules.

## 6. Publish an individual server

1. Sign in to the administrator panel through the LAN address on port `5080`.
2. Open the server detail page.
3. Under **Guest publication**, verify the external game port.
4. Select **Publish for friends**.
5. Copy the guest page address and send it to a friend.

The guest page contains no server IDs, internal IPs, paths, RCON data, password values, process IDs, logs, players or administrator actions. It only indicates whether the server is password protected.

## 7. Test from outside

Do not test through the home Wi-Fi. Disable Wi-Fi on a phone and open `http://your-name.myfritz.net:5081/join` through mobile data. Then test the game connection from a friend outside the home network.

The initial portal uses HTTP and exposes only deliberately public read-only data. Before using a custom public web domain broadly, add HTTPS through a dedicated reverse proxy. The administrator listener must remain private even after HTTPS is added.

## Troubleshooting

- **Guest page opens, game does not connect:** check the exact TCP/UDP game rule and the separate Windows Firewall rule for that game port.
- **Nothing is reachable over IPv4:** check for DS-Lite or carrier-grade NAT in the FRITZ!Box Online Monitor.
- **Address changes:** configure MyFRITZ or DynDNS and reinstall/update the service with the hostname.
- **Only IPv6 works:** friends need IPv6 connectivity; use the bracketed IPv6 address displayed by the Dock or a DNS name with an AAAA record.
