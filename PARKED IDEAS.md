# PARKED IDEAS

Features that exist in RATmeter's history or were floated, and are NOT in Hawkeye. Build nothing
from this list unless the owner asks for that item by name. As of 2026-11-20.

## Removed on 2026-11-20 (owner: "get rid of all the other features, just leave opacity and allow it to be draggable")

| Feature | What it was | Where to find the old code |
|---|---|---|
| Combat history | Every boss fight saved to SQLite, a history browser with search and paging | RATmeter `Services/Session/Persistence/`, `Views/HistoryWindow*` (git history before the rename) |
| Player details | Click a row: per-skill table (hits, crit, back, perfect, double, min/avg/max), DPS graph, combat log, buff uptime | RATmeter `Views/PlayerDetailsWindow*`, `GraphDataCalculator.cs`, `BuffStatisticsCalculator.cs` |
| Stat efficiency calculator | Estimates which stat gives the most DPS | RATmeter `Services/StatEfficiency/` |
| Auto-updater | Checked upstream GitHub releases and replaced the exe | RATmeter `AionDpsMeter.Updater/`, `UpdateCheckerService.cs` |
| Buff overlay bar | A row of active buff timers | Replaced by Buff mode in the skill tracker |
| Layout picker | Standard and Compact layouts | Replaced by the one owner look |
| Show/hide hotkey | Ctrl+Shift+D minimised all windows | RATmeter `Utils/GlobalHotkey.cs` |
| Nickname hiding, class colours, row size, custom name/bar colours | Appearance options | RATmeter `SettingsPage` |

The owner's reference screenshot also showed a skill-breakdown table the owner liked the look of. If player
details ever come back, that screenshot is the style guide for it.

## Floated, not decided

- A yellow marker beside the row whose details are open (seen in the reference screenshot).
- Showing the meter as an OBS browser source for the stream.
