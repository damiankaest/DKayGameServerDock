# Basic Control

Basic Control is the deliberately small vertical slice used to verify the real Windows/CS2 host
before more presets, plugins or map automation are added.

## Included

- select an existing server registered in DKayGameServerDock;
- start, stop or restart the selected process;
- send a single-line command from the web UI;
- show the RCON or standard-input response in a short local browser history;
- persist and verify three CS2 values: auto-bhop, gravity and bot quota.

The command path uses the registered game adapter directly. CS2 commands use the protected local
RCON connection; no shell and no `.bat` file is involved.

## Persistent CS2 configuration

The canonical data is stored below the server instance at:

```text
.dkay/basic-config.json
```

The game-readable projection is regenerated at:

```text
game/csgo/cfg/dkay-basic.cfg
```

`dkay-basic.cfg` is loaded after the older preset, combat and live layers. On an existing instance,
the first Basic Control load migrates the three owned values from `.dkay/live-settings.json` when
available instead of resetting them to defaults.

When the server is running, saving performs this sequence:

1. validate and persist the typed values;
2. regenerate `dkay-basic.cfg` atomically;
3. execute `exec dkay-basic.cfg` over local RCON;
4. query every owned ConVar separately;
5. report whether the returned values match the saved configuration.

## Next slice: map-specific configuration

Map-specific configuration should build on this verified path. The intended structure is one base
configuration plus one optional typed override per map. Map changes should be issued by the API,
wait until the target map is confirmed through RCON, then apply the matching generated CFG and read
its owned values back. BAT files should not become configuration state because they provide no
reliable response, quoting boundary or live verification.
