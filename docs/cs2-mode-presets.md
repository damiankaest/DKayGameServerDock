# CS2 mode presets and managed mods

The **Modes & maps** tab is available on every installed Counter-Strike 2 instance. A profile combines one map, an optional Steam Workshop item, bot behavior, a validated ConVar set and a recommended mod stack. Applying another map creates another profile; applying an existing map updates it and makes it active for the next start.

The shipped values are safe, practical starting points. There is no single universally perfect Surf/KZ/Bhop configuration: map authors and competitive communities sometimes require different physics values. The exposed values can therefore be adjusted per map without allowing raw console commands.

## Presets

| Preset | Baseline | Managed stack |
|---|---|---|
| Classic / Practice | Competitive-style rounds and team balance | None |
| Surf | High air acceleration, long rounds, manual bhop timing | Metamod, CounterStrikeSharp, CS2-Tags, Movement Unlocker, RampBugFix, SharpTimer |
| KZ / Climb | KZ movement baseline and long rounds | Metamod, official KZGlobalTeam CS2KZ |
| Bunny Hop | Automatic hopping, strong air control, long rounds | Metamod, CounterStrikeSharp, CS2-Tags, Movement Unlocker, SharpTimer |
| ScoutzKnivez | SSG 08 + knife spawns, low gravity, no economy | None; native rules only |
| RPG Arena | Fast respawns and progression-ready arena rules | Metamod and CounterStrikeSharp automatically; Warcraft/RPG package remains manual and experimental |

## Admin workflow

1. Stop the CS2 server. Presets and plugins cannot be changed while its process is running.
2. Open **Modes & maps** and select a preset.
3. Enter a map such as `surf_utopia_njv`. Use only the map name, not a path or command.
4. Optionally enter its numeric Steam Workshop ID. When present, launch uses `host_workshop_map` rather than `map`.
5. Adjust bot quota/difficulty and the preset's exposed values.
6. Keep **Install trusted preset stack** enabled to queue the registered dependencies.
7. Apply the profile. Wait until all package jobs return the instance to `Stopped`, then start it.

The guest portal displays the active preset and map but never exposes the Workshop ID, filesystem paths, package inventory or configuration values.

## Generated files

The manager owns these files inside the instance:

```text
game/csgo/cfg/dkay-server.cfg
game/csgo/cfg/dkay-mode.cfg
game/csgo/cfg/dkay/maps/<profile>.cfg
game/csgo/cfg/dkay/modes.json
game/csgo/addons/.dkay/<package>.json
```

`dkay-server.cfg` executes `dkay-mode.cfg`; the latter selects one generated per-map file. Do not place secrets in a mode profile. The normal server password remains in `dkay-server.cfg` and is removed from API responses.

The **Live control** page deliberately owns a separate layer:

```text
.dkay/gslt-token
.dkay/live-settings.json
game/csgo/cfg/dkay-bootstrap.cfg
game/csgo/cfg/dkay-gslt.cfg
game/csgo/cfg/dkay-live.cfg
```

Files below `.dkay` are canonical private state. Before an update, an existing manually created `dkay-gslt.cfg` is migrated there. On every later start the Dock regenerates the public cfg files, loads RCON and GSLT before the first map, applies the selected map preset and finally applies the administrator's live overrides. This order lets a preset provide its baseline without overwriting explicit live values such as warmup duration, gravity, maximum velocity or private-server cheats.

`sv_cheats` is a global CS2 server variable, not a per-Steam-user permission. Enabling **Private-server cheats** therefore affects every connected player. Keep it enabled only on a trusted private server.

Metamod installation adds `Game csgo/addons/metamod` as the first `SearchPaths` entry in `gameinfo.gi`. The unmodified file is saved once as `gameinfo.gi.dkay-original`. Steam updates may overwrite `gameinfo.gi`; the Dock reapplies the entry after a managed CS2 update when the Metamod marker is present.

## Package security and update behavior

- Package IDs, publishers, project URLs and dependency graphs are compiled into the server; requests cannot supply a URL.
- A recommended package stack is processed as one ordered job. If one dependency fails, later packages are not attempted and the first actionable error remains visible.
- GitHub downloads use the selected repository's latest release ZIP. Metamod uses the official AlliedModders 2.0 snapshot channel.
- Downloads are capped at 256 MB; expanded archives are capped at 512 MB and 10,000 entries.
- Extraction happens in a staging directory. Absolute paths, `..` traversal and symbolic links are rejected before deployment.
- Automatic installation still executes third-party native or managed code when CS2 starts. Review the linked upstream and license before enabling an experimental package.
- A CS2 update can temporarily break Metamod or CounterStrikeSharp compatibility. Update the game first, then explicitly update the package stack and inspect the console before publishing the server again.

The current package markers track which release the Dock deployed; they are not a cryptographic attestation. Checksum pinning, rollback and post-update compatibility probes remain planned hardening work.
