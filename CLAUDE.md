# CLAUDE.md

This file guides Claude Code (and any human) working in this repository.

> COLD PICKUP: read `START HERE.md` first. It says where things stand and the reading order.
> Every live system is catalogued in `SYSTEMS - WHAT EACH DOES.md` (what it does, where it lives,
> which settings drive it).

## What this is

**Hawkeye** is a damage meter overlay for the MMO Aion 2 (global servers), built for the owner's own
use and stream. It listens to the game's network traffic with Npcap (it never reads or touches the
game process), decodes the combat messages, and shows:

- a meter window: one bar per player, exact DPS, fight timer, and an optional status line with
  ping, game FPS, system CPU and RAM;
- a skill tracker: each tracked skill is its own small icon you can drag anywhere on screen,
  following either its cooldown or the buff it gives.

It started cut down on purpose (owner, 2026-11-20: "get rid of all the other features, just leave
opacity and allow it to be draggable"); features come back only when the owner asks. Since then the
owner asked for and got: fight history (last 50 + kept forever) and a combat analysis window (v0.1.5).
Still removed: the stat calculator, the updater, the old buff bar, DPS graphs; see `PARKED IDEAS.md`.

**Origin and licence.** Hawkeye started 2026-11-20 as a fork of RATmeter
(https://github.com/Kuroukihime/AIon2-Dps-Meter, GPL-3.0). The packet engine (capture, stream
rebuild, decoding, combat sessions) is theirs, renamed; the app around it was rewritten. GPL-3.0
applies to the whole project:
- `LICENSE` keeps the original copyright line. Never remove it.
- `README.md` keeps the one-line credit. Never remove it.
- If Hawkeye is ever shared, its source must be shared under GPL-3.0 too.

**The GitHub repo is PUBLIC** (`https://github.com/simbariya/hawkeye`, standalone since 2026-11-21;
the older fork `simbariya/AIon2-Dps-Meter` is no longer pushed to). Nothing personal and
no secret goes in a commit: no real names, emails, API keys, Npcap captures or settings files.
The owner chose to be credited publicly as **Simba** with the Twitch link **twitch.tv/drake22x**
(2026-11-21, `Hawkeye.App/Branding.cs`); those two are fine. Nothing else about the owner is.

## Start with the skill library

`.claude/skills/` holds the operating knowledge (same system as the Korathia project). Load the
relevant skill before working; **`hawkeye-change-control` before ANY change.**

| Skill | Load it when |
|---|---|
| `hawkeye-change-control` | Before any change. Classifies it, names its gate, lists the rules that cannot bend |
| `hawkeye-architecture-contract` | Touching any code: the pipeline, threading, settings, the windows, file map |
| `hawkeye-protocol-and-captures` | Anything about the game's messages: opcodes, layouts, recording and replaying fights |
| `hawkeye-validation-and-qa` | Before saying "works", "fixed" or "done" |
| `hawkeye-debugging-playbook` | Something is wrong: symptom to cause table |
| `hawkeye-failure-archaeology` | Before repeating a past mistake; the incident log |
| `hawkeye-docs-and-writing` | Writing any doc, changelog entry or UI text |
| `hawkeye-build-and-run` | Building, running, publishing, the dev environment |
| `image-generator` (local only) | Making logos or any image (spends the owner's OpenAI credits) |
| `project-discipline-and-methodology` (local only) | Portable: evidence bar, predict-before-running, session-end checkpoint |
| `ai-delegation-and-model-routing` (local only) | Portable: what to delegate, to which model, and when to stop |

"Local only" skills are copied from the owner's private Korathia project and hold its private paths,
so they are kept out of git (`.git/info/exclude`) and exist only on the owner's PC.

## Build, test, run

```
dotnet build Hawkeye.slnx                                  # everything
dotnet test --project Hawkeye.Tests/Hawkeye.Tests.csproj   # unit tests (must stay green)
Hawkeye.App\bin\Debug\net10.0-windows10.0.20348.0\Hawkeye.exe   # run it
```

- .NET 10 SDK at `C:\Program Files\dotnet` (installed 2026-11-20). Npcap must be installed with
  "WinPcap API-compatible Mode" ticked.
- Settings: `%AppData%\Hawkeye\settings.json`. Logs: `%AppData%\Hawkeye\logs\hawkeye-YYYYMMDD.txt`.
- FPS needs Hawkeye to run as administrator (Windows only gives frame events to elevated programs).
- Full detail, including the render harness used to check visuals without the game:
  `hawkeye-build-and-run` and `hawkeye-validation-and-qa`.

## Rules that are not up for debate

1. **Three looks, owner-chosen (2026-11-21), nothing else.** Monolith (default), Aurora, Graphite &
   Vermilion, picked in Settings > Look. They were chosen from two rounds of mock-ups
   (`..\design-options\`). Do not add looks or restyle one unasked. The meter's styles live in
   `wwwroot/css/meter.css` (shared with the stream page); window palettes in `hawkeye.css` (`theme-*`).
2. **Square corners everywhere.** Meter, icons, settings, buttons. `hawkeye.css` forces it.
3. **No em or en dashes in anything the user sees** (UI text, tooltips, messages). Use "to" or a
   plain hyphen.
4. **Never touch the game process.** Network capture and Windows' own event stream only. No memory
   reading, no injection, no input sending to the game.
5. **Push, publish and anything public need the owner's explicit word** in the current message.
   Spending money (OpenAI images) needs the owner's go for that spend.
6. **Every change ends with the update duty** in `hawkeye-docs-and-writing`: changelog entry,
   `SYSTEMS - WHAT EACH DOES.md` if a system changed, the checkpoint in `HANDOFF - CURRENT SESSION.md`.

## Shell hazards (inherited from Korathia, both bit this project on day one)

- **The Bash tool mangles backslashes in heredocs and sed.** A sed edit turned a Windows project
  path into `C:SERSBISON...` (I4). Write files with the Write tool; use Bash to run things.
- **Scripted mouse input (`SetCursorPos`/`mouse_event`) was blocked during a game session**
  (likely Windows blocking input to a higher-privilege app; not verified). You cannot click Hawkeye's UI from a script during a session; use the render
  harness instead (I3).
- Never kill processes by image name except Hawkeye's own test instance (`Hawkeye.exe`). The game
  (`AION2.exe`) and the owner's other tools may be running.

## Token hygiene

- Do not read `Hawkeye.Core/GameData/Assets/*.json` whole (`mobs.json` is 800 KB, `skills.json`
  300 KB). Grep them.
- Do not read old meter logs whole; they reach 400k lines. Count and group with grep/uniq.
- Start a big batch in a fresh session with the checkpoint written first (see the portable
  delegation skill: session length is the dominant cost).
