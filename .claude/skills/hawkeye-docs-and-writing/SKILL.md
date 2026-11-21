---
name: hawkeye-docs-and-writing
description: >
  How Hawkeye's documents are written and kept true: the docs-of-record table, the version homes,
  the changelog template, house style for docs and for UI text, and the update duty after every
  batch. Load when writing or changing any .md in the repo, a changelog entry, a skill, UI copy
  (labels, tooltips, messages), or when bumping the version. Modelled on Korathia's
  olympus-docs-and-writing. Does NOT cover approval and gates (hawkeye-change-control).
---

# Hawkeye Docs and Writing

As of v0.1.0, 2026-11-20. Root: `C:\Users\Bison\Documents\Aion 2 DPS meter\my-meter`.

## 1. Docs of record

| File | Role | Update when |
|---|---|---|
| `START HERE.md` | Cold-pickup entry: current state and reading order | State or reading order changes |
| `CLAUDE.md` | Operating rules, skill table, the rules that cannot bend | Workflow, architecture summary or rules change |
| `HANDOFF - CURRENT SESSION.md` | Append-only running record; CHECKPOINT at the end is the live state | DURING work: every result, every owner ruling |
| `HAWKEYE - CHANGELOG.md` | The one changelog, newest first | Every batch |
| `SYSTEMS - WHAT EACH DOES.md` | Every live system: what, settings, files | Same change as any behaviour change |
| `PARKED IDEAS.md` | Removed and not-built features | When something is removed, floated, or revived |
| `README.md` | Public face of the repo, keeps the GPL credit | Install steps or features change |
| `.claude/skills/*/SKILL.md` | Playbooks | When their facts change; add a dated CHANGES line |

Read order for a cold session: START HERE -> CLAUDE -> HANDOFF checkpoint -> SYSTEMS -> the skill.

## 2. Version homes (must agree)

- `Hawkeye.App/Hawkeye.App.csproj` `<Version>`
- The top entry of `HAWKEYE - CHANGELOG.md`
Bump per batch, not per edit. Scheme: `0.MINOR.PATCH` until the owner calls something 1.0.

## 3. Changelog template

```markdown
## vX.Y.Z (YYYY-MM-DD)

One line: what this batch was.

Added:
- **Name.** What it does, where it lives.

Changed:
- What changed and why.

Fixed:
- **What the owner saw.** Root cause: what was actually wrong and why it caused that. Fix: what
  changed. (Cite the incident: I1.)

Removed:
- What went, and where it is parked.
```

New entries at the TOP. Never edit past entries except to correct a fact.

## 4. House style

### Docs
1. **Date volatile facts** ("as of v0.1.0, 2026-11-20"). Undated status is useless later.
2. **Symptom -> root cause -> fix** for every real bug.
3. **No oversell.** Unproven = "not verified", "open", "candidate". Say which rung of the evidence
   ladder was reached (hawkeye-validation-and-qa).
4. **Headings name the thing.** No flourishes, no AI-lingo titles.
5. **Lead with the path, the number or the decision.** Full absolute paths for files outside the repo.
6. **Cite incidents by number** (I1); full stories only in hawkeye-failure-archaeology.
7. **Never end a doc promising future work.** Do it, then write it down.
8. **Public repo.** No real names, emails, keys, other players' names. Refer to "the owner";
   use they/them for anyone whose pronouns are not stated.

### UI text (anything the user sees in Hawkeye)
- No em or en dashes. "10 to 100%", not "10–100%".
- Short and plain: name the thing, state the one fact. ("Needs Hawkeye to run as administrator.")
- Numbers the way the game shows them: thousands separators ("137,985/s").

### Talking to the owner
- Plain language, no jargon; explain what something means for them.
- Visual choices are shown as images (render the options), not described.
- Say clearly what was not tested.

## 5. Update duty after EVERY batch

1. [ ] Changelog entry (section 3), version bumped if it is a release.
2. [ ] `SYSTEMS - WHAT EACH DOES.md` for any behaviour change.
3. [ ] `PARKED IDEAS.md` if a feature was removed or revived.
4. [ ] `CLAUDE.md` / `START HERE.md` if rules, state or reading order changed.
5. [ ] hawkeye-failure-archaeology if there was a real incident (next free number in that file).
6. [ ] `HANDOFF - CURRENT SESSION.md` checkpoint, written during the work.

## CHANGES

- 2026-11-20: written for v0.1.0.
