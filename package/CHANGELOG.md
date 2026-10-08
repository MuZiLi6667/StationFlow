# Changelog

## 0.2.0

Initial release.

- **Overview & Naming**: virtualized table of all logistics stations, multi-select batch rename with template variables and presets, auto-recommended template, cluster-wide duplicate avoidance, rename undo
- **Rule-based Pairing**: Name-driven (Mode 1) and Slot-driven (Mode 2) × three mechanisms (Point-to-Point / Planet Route / Star-System Route); generate → preview → transactional apply → undo
- **Logistics Network**: planet × item traffic heatmap (green output / orange input / yellow both / gray storage)
- **Diagnostics**: standstill risk / missing supply / tight supply
- **Batch Tools**: topology pairing (Ring / Chain / Star / All-to-All), batch behavior, batch groups
- **Auto-Pilot**: incremental pairing triggered by station-count changes (off by default)
- Config persistence via BepInEx config file
