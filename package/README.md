# StationFlow

**Automation and batch management for interstellar logistics stations in Dyson Sphere Program.**

Late-game saves often have hundreds of logistics stations, and pairing them by hand — one by one, through the vanilla UI — is a huge pain. StationFlow lets you generate, preview and apply pairings in bulk, visualize the whole logistics network, and diagnose why a station isn't delivering.

## Features

### Overview & Naming (Tab 1)
- Table of every logistics station in the cluster: name, interstellar supply/demand items with icons, planet, behavior, groups, P2P route count
- Multi-select (Ctrl / Shift click) and batch rename with templates using variables:
  `{item}` `{role}` `{supply}` `{demand}` `{storage}` `{galaxy}` `{planet}` `{gid}` `{seq:2}` (Chinese aliases are also accepted)
- Preset templates (full form / classic / per-planet / storage) plus an auto-recommended template based on your selection
- Cluster-wide duplicate avoidance; one-click undo

### Rule-based Pairing (Tab 2)
- **Mode 1 — Name-driven**: parses `[item][role]` from station names and pairs supply/demand stations of the same item, sorted by demand gap (descending) and distance (ascending)
- **Mode 2 — Slot-driven**: reads each station's interstellar supply/demand slots directly. No naming required
- **Three pairing mechanisms**: Point-to-Point (station-to-station), Planet Route, and Star-System Route (pairs inside the same star system automatically fall back to Planet Routes, since vanilla routes cannot be expressed at system level for those)
- Parameters: max K supply stations per demand station, minimum supply ratio, allow/disallow cross-system pairing
- Generate → preview (＋ new / ✓ existing) → transactional apply (frame-budgeted writes, a single pairing-table rebuild) → one-click undo
- Single-source items are skipped with a notice (the game's built-in fallback pairing already covers them); storage slots and local logistics are never touched

### Logistics Network (Tab 3)
- Planet × item traffic heatmap: green = output, orange = input, yellow = both, dim = configured but idle, gray = storage
- Data comes from the game's own traffic statistics (≈ last minute); hover a cell for details

### Diagnostics (Tab 4)
- **Standstill risk**: behavior set to "Only / Designated" but with no P2P, no group and no route — in vanilla these have no fallback, so the station will not dispatch
- **Missing supply**: items demanded by interstellar stations but supplied by none
- **Tight supply**: total demand gap far exceeds the stock on hand

### Batch Tools (Tab 5)
- Topology pairing across the selected stations: Ring / Chain / Star / All-to-All (same-planet pairs are skipped automatically)
- Batch-set behavior (Ignore / Prioritize / Only / Designated); batch join / leave groups (1–30)
- Everything runs through the transactional executor: frame-budgeted and undoable

### Auto-Pilot
- Off by default. When enabled, it checks every 30 seconds for new stations and applies incremental pairing using your current rule settings (idempotent — only new pairs are written)
- The per-run action cap is configurable; it disables itself on error

## Installation

1. Install **BepInExPack_Dyson_Sphere_Program** (via r2modman or manually)
2. Install this mod (via r2modman or by unzipping into your game folder)
3. Launch the game and press **F8** to open the management panel

> Save-safe: the mod only uses native game data fields. Your saves load normally after uninstalling.

## How it works (why batch operations are safe)

- All writes go through vanilla entry points / native fields: point-to-point pairs into `station2stationRoutes`, routes into `astro2astroRoutes`, names through `WriteExtraInfoOnEntity`
- Changes are merged and the pairing table is rebuilt **exactly once** (`RefreshTraffic`); writes are spread across frames to avoid hitches
- The pairing table itself is not saved — the game rebuilds it on load — so a crash mid-operation cannot corrupt your save
- Every execution records an inverse action list, so results can be undone with one click

## Keys

| Key | Action |
|---|---|
| F8 | Toggle the management panel |
| F9 | Debug window (development use) |

## Compatibility

- Game 0.10.x (Mono, BepInEx 5.4.x)
- No known conflicts with UXAssist, Multfuntion_mod and similar mods (game dispatch logic is not altered)

## Feedback

Please attach the `[StationFlow]` lines from your `BepInEx/LogOutput.log`.
