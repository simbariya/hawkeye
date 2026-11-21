---
name: hawkeye-change-control
description: >
  Load this BEFORE making any change to Hawkeye: editing code in Hawkeye.App / Hawkeye.Engine /
  Hawkeye.Core, changing the meter's look, touching the packet decoders, adding a setting or a
  feature, editing a doc of record or a skill, committing, pushing, publishing a build, or doing
  anything that spends money (OpenAI image generation). It classifies the change, names the gate
  it must pass, and lists the rules that cannot bend, each with the reason it exists.
  Does NOT cover: what counts as evidence (hawkeye-validation-and-qa), how to write the changelog
  (hawkeye-docs-and-writing), or incident stories (hawkeye-failure-archaeology).
---

# Hawkeye Change Control

Assumes zero prior context. Project root: `C:\Users\Bison\Documents\Aion 2 DPS meter\my-meter`.

## 1. Classify the change, then pass its gate

| Class | Examples | Gate before calling it done |
|---|---|---|
| (a) Look | Colours, spacing, fonts, icon states | Build; render check (harness or headless page) shown to the owner as an image |
| (b) Behaviour | Settings, tracker logic, window handling | Build; unit tests green; run the app; the specific behaviour observed (harness or live) |
| (c) Decoder | Anything in `Hawkeye.Engine/PacketProcessing/` | Build; tests; a RECORDED capture replayed with predicted numbers written down first (hawkeye-protocol-and-captures section 5) |
| (d) Engine plumbing | Capture, stream rebuild, sessions | (b) plus a live run that locks onto the game and shows ping |
| (e) Docs and skills | Any .md of record, any skill | Facts re-checked against the code the same session; dated |
| (f) Public | Commit, push to the PUBLIC fork, publishing an exe | The owner's explicit word in the current message. Scan the diff for personal data and secrets first |
| (g) Money | gpt-image-2 or any paid API | The owner's go for that spend; report the cost after (`image-generator`) |

A batch of several items is one unit: classify each, run the strictest gate once at the end.

## 2. Rules that cannot bend

| Rule | Why |
|---|---|
| **One look.** No layout options, themes or style pickers | Owner, 2026-11-20: "I want my look to be the only look available, no need to complicate" |
| **Square corners everywhere** | Owner, 2026-11-20: "no smoothened corners please". `hawkeye.css` forces `border-radius: 0` |
| **Keep it small.** Meter, status line, tracker, settings. Nothing else unless asked | Owner, 2026-11-20: "get rid of all the other features". Removed items live in `PARKED IDEAS.md` |
| **Never touch the game process** | Account safety. Only network capture (Npcap) and Windows' own event stream (FPS) are allowed |
| **No em or en dashes in UI text** | Inherited Korathia rule; use "to" or a hyphen |
| **Keep `LICENSE` and the README credit** | GPL-3.0 condition of using RATmeter's engine |
| **Nothing personal or secret in a commit** | The fork is public. No real names, emails, keys, `.env`, captures, settings files, logs, or screenshots with other players' names |
| **Push, publish, post: explicit owner word only** | Answering a question is not a go |
| **Write files with the Write tool, not Bash heredocs** | Backslashes get eaten (hawkeye-failure-archaeology I4) |

## 3. Definition of done (every batch)

1. The class gates above pass, and you SAW the result (not just "it built").
2. `HAWKEYE - CHANGELOG.md` has a new top entry with root causes (hawkeye-docs-and-writing).
3. `SYSTEMS - WHAT EACH DOES.md` updated in the same change if a system's behaviour changed.
4. `HANDOFF - CURRENT SESSION.md` checkpoint appended.
5. Anything not verified is SAID to be not verified, in the reply and in the checkpoint.

## 4. Before the first commit

Git in this repo has no user name or email set (as of 2026-11-20). Ask the owner which identity to
use; GitHub's private no-reply address keeps their email out of the public history. Commit messages
end with the attribution lines the session provides.

## When NOT to use this skill

- Evidence standards: hawkeye-validation-and-qa. Incident detail: hawkeye-failure-archaeology.
- Writing style and the changelog template: hawkeye-docs-and-writing.

## CHANGES

- 2026-11-20: written for v0.1.0, modelled on Korathia's olympus-change-control.
