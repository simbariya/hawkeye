---
name: hawkeye-debugging-playbook
description: >
  Symptom-to-cause table for Hawkeye, plus the order to investigate in. Load when something is
  wrong: the meter stays empty, numbers look low, an icon will not move or never changes, FPS says
  admin, the app will not start, settings do not stick, the window vanished. Does NOT hold incident
  stories (hawkeye-failure-archaeology) or byte layouts (hawkeye-protocol-and-captures).
---

# Hawkeye Debugging Playbook

As of v0.1.0, 2026-11-20. Rule zero (project-discipline-and-methodology): find the ONE mechanism
that explains every symptom before changing code. Two failed fixes on the same symptom = stop, your
model of the bug is wrong.

## 1. Where to look first

- Log: `%AppData%\Hawkeye\logs\hawkeye-YYYYMMDD.txt`. Healthy start shows `[CAPTURE] Started`,
  `[DETECT] Winner`, `!!! LOCKED !!!`, `SYNCHRONIZED`.
- Settings: `%AppData%\Hawkeye\settings.json`.
- Group repeated log lines instead of reading them:
  `sed -E 's/^[0-9-]+ [0-9:.]+ //' LOG | sed -E 's/[0-9]+/N/g' | sort | uniq -c | sort -rn | head`

## 2. Symptom table

| Symptom | Likely cause | Check / fix |
|---|---|---|
| Message box "couldn't start listening" | Npcap missing or installed without WinPcap mode | Reinstall Npcap with "WinPcap API-compatible Mode" |
| Meter empty, no "LOCKED" in log | No heartbeat seen: game not in a zone yet, or traffic on an adapter Npcap does not see | Teleport or change zone; with a VPN, check the loopback adapter exists in Npcap |
| "LOCKED" then "[WATCHDOG] No heartbeat" repeating | Game reconnected on a new port or adapter | Normal after zoning; it re-detects by itself |
| Meter blank for everything; last "LOCKED" line says port 443 or 80 | Locked onto web traffic after the game went quiet (character switch, 2026-11-23) | v0.2.24 drops such a lock after 45 s (`[CAPTURE] port N sent no game message`); before that, restart Hawkeye |
| Meter empty on a training dummy, other fights fine | Hawkeye started next to the dummy (update restart) and never saw it appear, so it looks like trash (2026-11-23) | v0.2.23 remembers dummies (`known-dummies.json`); otherwise walk away until it leaves the screen and come back |
| Meter empty during a fight on trash mobs | `BossOnlyCapture` is true by design | Hit a boss or training dummy, or set it false in settings.json |
| DPS lower than the game shows | Lost hits: TCP gaps or damage-decode failures | Count `[TCP GAP]` and "Need N bytes" lines; record a capture (protocol skill section 5) |
| Many `Varint too long` / `Need N bytes` errors | Layout drift after a game patch, or the known 0x3804 issue | Protocol skill section 4; do not patch blind |
| Icon never darkens on cast (Cooldown mode) | The game sends that skill's cooldown under a different id, or not at all | Log the ids from `0x3847`/`0x3802` for that cast; compare with the tracked id |
| Hawkeye closes by itself | An unhandled error; Hawkeye could not log it | Windows Event Viewer > Application > ".NET Runtime" 1026 has the stack (I8) |
| Buff icon stays red | Buff arrives with another id, or on an entity that is neither you nor cast by you | Read the `[ME]`, `[TRACK]`, `[SEEN]` lines in the log after casting it (I9) |
| Icons gone outside fights | By design since v0.1.2: they show only in boss/dummy fights | Settings > Skill tracker > "Always show" |
| Meter jumps between fights, or another player shows as you (green) | Wrong "you" (I10) | Count `[ME]` lines; more than one, or any after the start, means the guess fired wrongly |
| Status line says "FPS: run as admin" | Not elevated | Click it (restarts elevated) or right-click Hawkeye.exe > Run as administrator |
| Icon cannot be dragged | Not in edit mode (Settings closed), or covered (I1) | Open Settings; icons should show a dashed outline and CD/BUFF tag |
| Icons invisible in game | Game in exclusive fullscreen | Use borderless or windowed mode |
| "-- FPS" while playing | Game process not named `AION2`, or DXGI events not arriving | Check `Get-Process AION2`; check the log for "FPS counter" warnings |
| Settings reset to defaults | settings.json corrupt or unreadable, so defaults load by design | Inspect the file; it is overwritten on the next save |
| Meter window off-screen | Saved position on a monitor that is gone | It is clamped on start; else delete the Window* keys from settings.json |
| Skill icons are a letter instead of art | Icon download from the game CDN failed | Check `IconCache\Skills\` next to the exe; network or CDN down |

## 3. Investigation order for "numbers are wrong"

1. Is the right fight active? (target name in the header)
2. Are hits being lost? Count gaps and decode errors over the fight.
3. Record the fight, replay it, compare per-hit against the game's combat log.
4. Only then touch a decoder, with a written prediction.

## CHANGES

- 2026-11-20: written for v0.1.0.
- 2026-11-21: v0.1.2 rows (icons hidden outside fights, wrong "you", FPS click).
