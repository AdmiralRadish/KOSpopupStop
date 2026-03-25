# KOSpopupStop

Companion KSP plugin to prevent repeated kOS connectivity-manager selection prompts when saves/settings do not reliably persist under modded multiplayer setups.

## What it does
- Reads kOS connectivity custom-parameter state via reflection.
- Ensures `knownHandlerList` includes currently available manager names.
- Captures the first valid manager the user actually has selected and stores it locally.
- If kOS later loses the selected value, restores that same captured value only when it is still available.
- Avoids compile-time dependency on `kOS.dll` so it can coexist across kOS builds.

## Why this suppresses the popup
kOS shows the popup when:
- the selected manager is unavailable, or
- newly available managers are missing from `knownHandlerList`.

This plugin normalizes known handlers and preserves the user-selected handler, so kOS no longer sees a mismatch every load.

## Build
1. Copy `KOSpopupStop.example.user` to `KOSpopupStop.csproj.user`.
2. Set `KSPBT_GameRoot` to your KSP install path.
3. Build:
   - `dotnet build KOSpopupStop.csproj -c Release`

## Output
- `GameData/KOSpopupStop/Plugins/KOSpopupStop.dll`

## Runtime notes
- Loads once at game startup (`KSPAddon.Startup.Instantly`).
- Re-applies after save load and settings applied events if needed.
- Does not force CommNet or PermitAll; it follows the user selection.
- Local cache uses `PluginConfiguration` for this plugin type (machine-local), avoiding server-side setting overwrite behavior.
- No custom GUI is created by this mod (no window, toolbar, app-launcher button).

## Debug logging (console only)
- `DebugEnabled` is enabled by default in the plugin's local `PluginConfiguration` file.
- Set `DebugEnabled = False` there if you want to silence non-warning debug messages.
- Debug output is written only to KSP logs/console; there are no in-game UI controls.
