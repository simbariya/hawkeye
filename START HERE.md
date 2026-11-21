# START HERE - Hawkeye, cold pickup

Updated 2026-11-21 evening (v0.2.15). If this date looks old, trust `HAWKEYE - CHANGELOG.md` and the
CHECKPOINT at the end of `HANDOFF - CURRENT SESSION.md` over it.

## What this is

Hawkeye is the owner's own Aion 2 damage meter: a small always-on-top meter window plus draggable
skill and buff timer icons. It reads the game's network traffic through Npcap and never touches
the game. Built in C# on .NET 10 (WPF windows, the meter and settings drawn as web pages inside
them). Repo: `C:\Users\Bison\Documents\Aion 2 DPS meter\my-meter`.

The owner streams Aion 2 on Twitch and plays a Ranger. Requests come in plain language, often
several at once; treat a list as one batch. Explain results without jargon.

## Where things stand (2026-11-21 evening)

- **Released on GitHub, public:** https://github.com/simbariya/hawkeye (standalone repo; the old fork is
  no longer pushed). Latest release v0.2.15, two zips each (full with .NET, small without). Copies from
  0.2.7 on update themselves from GitHub; 0.2.9 and 0.2.10 were broken (no window, I15) and need a
  manual unzip once.
- **What it does now:** DPS meter (boss and dummy fights, group fit, deaths), three looks, skill
  tracker (cooldown or buff per skill, on/off tick), fight history + combat analysis, Odyle on every
  character, streamer mode (OBS page with every window), auto-update, and PvP mode: kills, deaths,
  assists, AP and rank points, End session + PvP history, who-was-it lists, shugo.gg profile popup.
- **Verified:** 77 unit tests; every release is started from its packaged exe before publishing
  (build-and-run skill, step 4). PvP numbers matched the owner's called-out kills exactly.
- **Open (needs the owner in game):** exact AP/rank for the first kill (record a login: the totals
  are probably in the login data); target combat power / gear score (needs one target's exact numbers).
  See the HANDOFF checkpoint.

## The files that matter, in reading order

1. `CLAUDE.md` - operating rules, the skill table, the rules that cannot bend.
2. `HANDOFF - CURRENT SESSION.md` - the CHECKPOINT block at the END is the live state.
3. `HAWKEYE - CHANGELOG.md` - what shipped, newest first, with root causes.
4. `SYSTEMS - WHAT EACH DOES.md` - every live system, its settings and its owning files.
5. `PARKED IDEAS.md` - removed or not-yet-built features. Build nothing from it unasked.
6. `.claude/skills/` - the playbooks; `hawkeye-change-control` first.

## Hard rules that burn people

- Three owner-chosen looks only, square corners, no em/en dashes in UI text (CLAUDE.md "Rules").
- Push, publish, spend: only on the owner's explicit word.
- The GitHub repo is public: no names (character names included), emails, keys, captures or settings in commits.
- Keep `LICENSE` and the README credit line (GPL-3.0).
- Write files with the Write tool, not Bash heredocs (backslashes get eaten).
