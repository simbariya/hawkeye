# START HERE - Hawkeye, cold pickup

Written 2026-11-20 (v0.1.0, not yet released or pushed). If this date looks old, trust
`HAWKEYE - CHANGELOG.md` and the CHECKPOINT at the end of `HANDOFF - CURRENT SESSION.md` over it.

## What this is

Hawkeye is the owner's own Aion 2 damage meter: a small always-on-top meter window plus draggable
skill and buff timer icons. It reads the game's network traffic through Npcap and never touches
the game. Built in C# on .NET 10 (WPF windows, the meter and settings drawn as web pages inside
them). Repo: `C:\Users\Bison\Documents\Aion 2 DPS meter\my-meter`.

The owner streams Aion 2 on Twitch and plays a Ranger. Requests come in plain language, often
several at once; treat a list as one batch. Explain results without jargon.

## Where things stand (2026-11-20)

- v0.1.0 is built and runs. It was started the same day from a fork of RATmeter (GPL-3.0), then
  renamed, stripped to the meter plus the tracker, and given the owner's look.
- Verified: solution builds clean, 5/5 unit tests pass, the app starts, finds the game traffic,
  shows live ping, the meter page and the tracker icons. The icon drag fix is proven in the
  harness (`hawkeye-failure-archaeology` I1).
- NOT yet verified in a real fight: DPS rows during combat, cooldown timers from the game's
  cooldown message, buff timers from the game's buff message, FPS (needs a run as administrator).
  First job of the next session that has the owner in game: `hawkeye-validation-and-qa` section 3.
- Nothing is committed yet on branch `skill-tracker`; nothing is pushed. The owner has not yet
  said whether to commit/push or which name and email to commit under.

## The files that matter, in reading order

1. `CLAUDE.md` - operating rules, the skill table, the rules that cannot bend.
2. `HANDOFF - CURRENT SESSION.md` - the CHECKPOINT block at the END is the live state.
3. `HAWKEYE - CHANGELOG.md` - what shipped, newest first, with root causes.
4. `SYSTEMS - WHAT EACH DOES.md` - every live system, its settings and its owning files.
5. `PARKED IDEAS.md` - removed or not-yet-built features. Build nothing from it unasked.
6. `.claude/skills/` - the playbooks; `hawkeye-change-control` first.

## Hard rules that burn people

- One look, square corners, no em/en dashes in UI text (CLAUDE.md "Rules").
- Push, publish, spend: only on the owner's explicit word.
- The GitHub fork is public: no names, emails, keys, captures or settings in commits.
- Keep `LICENSE` and the README credit line (GPL-3.0).
- Write files with the Write tool, not Bash heredocs (backslashes get eaten).
