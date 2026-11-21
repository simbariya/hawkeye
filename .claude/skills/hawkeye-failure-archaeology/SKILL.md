---
name: hawkeye-failure-archaeology
description: >
  Hawkeye's incident log: every real failure, dead end and environment trap, with symptom, root
  cause, fix, evidence and the lesson. Load before working near any of them, when a symptom feels
  familiar, or to record a new incident (next free number at the bottom). Other docs cite incidents
  by number (I1, I2...) and only this file tells the full story.
---

# Hawkeye Failure Archaeology

## Summary table

| # | Incident | Status |
|---|---|---|
| I1 | Tracker icon could not be dragged (Deadshot) | FIXED v0.1.0 |
| I2 | Status line items ran together | FIXED v0.1.0 |
| I3 | Scripted mouse input blocked while the game runs | STANDING ENVIRONMENT FACT |
| I4 | sed through Bash mangled a Windows path | STANDING RULE |
| I5 | Upstream: cooldown picker closed the buff picker | GONE (settings rewritten) |
| I6 | Upstream: overlay Dispose re-subscribed instead of unsubscribing | GONE (file removed) |
| I7 | Inherited: ~80,000 damage messages failed to decode | OPEN |
| I8 | Closing a window from its own button crashed Hawkeye | FIXED v0.1.1 |
| I9 | Buffs ignored because Hawkeye did not know who you are | FIXED v0.1.1, confirmed by the owner 2026-11-21 |
| I10 | The I9 guess flipped "you" to other rangers | FIXED v0.1.2, awaiting in-game proof |

## The incidents in full

### I1 - Tracker icon could not be dragged (fixed v0.1.0)

- **Symptom:** the owner could drag newly added icons but not Deadshot, which had been tracked
  since startup.
- **Root cause:** the icons and the Settings window are all always-on-top windows; among those the
  most recently activated is drawn on top. Deadshot's default position sat under where Settings
  opens, so Settings covered it. Icons added while Settings was open were created later and sat
  above it. The click-through toggle itself worked (proven separately).
- **Evidence:** the test run's settings file saved a position for Marking Shot (added with Settings
  open) and none for Deadshot. Harness, before the fix: "icon above settings = False"; after:
  "True", with click-through off in edit mode and back on after.
- **Fix:** entering edit mode raises every icon above all windows without activating it
  (`TrackerIconWindow.BringToFront`); clicking into Settings raises them again.
- **Lesson:** "can't click it" on an overlay: check z-order before input handling.

### I2 - Status line items ran together (fixed v0.1.0)

- **Symptom:** "18 ms FPS admin CPU 34%" with single spaces.
- **Root cause:** items were joined with three spaces; HTML collapses runs of whitespace.
- **Fix:** each item its own `<span>` in a flex row with a gap.
- **Lesson:** spacing in web UI is layout, never spaces in strings.

### I3 - Scripted mouse input blocked while the game runs (environment fact)

- **Symptom:** `SetCursorPos` / `mouse_event` returned false; nothing was clicked.
- **Root cause (likely, not verified):** Windows blocks input injection into a higher-privilege
  foreground app (UIPI). The game or its anti-cheat probably runs with higher rights than the shell.
- **Rule:** do not plan UI tests that click through scripts during a game session. Use the
  harness (hawkeye-validation-and-qa section 2) or ask the owner to click.

### I4 - sed through Bash mangled a Windows path (standing rule)

- **Symptom:** a harness project referenced `C:SERSBISONDOCUMENTSAION 2 DPS METER...` and failed
  to build.
- **Root cause:** backslashes in a sed replacement were consumed as escapes (`\U` uppercases).
  Same class as Korathia's "the Bash tool mangles backslashes" law.
- **Rule:** write files with the Write tool or a Python script that uses forward slashes; never
  put Windows paths through sed or heredocs.

### I5 - Upstream: adding a cooldown skill closed the wrong picker

- In RATmeter's settings, adding a cooldown skill reset `_buffPickerOpen` instead of
  `_skillCdPickerOpen`. Found 2026-11-20; fixed, then the whole page was rewritten.

### I6 - Upstream: overlay Dispose re-subscribed instead of unsubscribing

- RATmeter's `SkillCdOverlay.Dispose` did `WindowStateUpdated = OnLiveStateChanged` (assignment)
  instead of `-=`, so disposing replaced every other subscriber. The file was removed in v0.1.0.
- **Lesson:** event cleanup is `-=`; review every Dispose for it.

### I7 - Inherited: damage messages that fail to decode (OPEN)

- **Symptom:** the owner's old meter logs (2026-11-04 to 11-20) hold 80,530 "Error processing
  packet for opcode 0x3804" (most "Need N bytes but only N remain") and 1,188 for `0x3805`, plus
  about 2 million `[TCP GAP]` warnings.
- **Meaning:** each failure is a hit that never reached the meter, so DPS reads low in busy fights.
- **Status:** OPEN. The running version was v1.11.3; upstream had reached v1.11.7, which Hawkeye
  is built on, so some of this may already be fixed. Measure first (protocol skill section 5).

### I8 - Closing a window from its own button crashed Hawkeye (fixed v0.1.1)

- **Symptom:** Hawkeye vanished twice in the owner's first session. Windows event log: .NET Runtime
  1026, "CoreWebView2 members cannot be accessed after the WebView2 control is disposed", raised
  from Blazor's error notifier on the UI dispatcher.
- **Root cause:** the Settings ✕ (and the meter's ✕) run inside the window's web view. `Close()`
  inside the click destroyed the web view mid-event; the redraw Blazor does after every event then
  posted to the destroyed control, and the exception was unhandled, so the process died.
- **Evidence:** harness that closes its own window from a page: synchronous close = 3 unhandled
  errors with that exact message; deferred close = 0.
- **Fix:** close at `DispatcherPriority.Background` (after the click and its redraw); app-wide
  `DispatcherUnhandledException` logs and keeps running.
- **Lesson:** never destroy a web view from inside its own event. Check the Windows event log
  (Application, ".NET Runtime" 1026) for crashes the app could not log itself.

### I9 - Buffs ignored because Hawkeye did not know who you are (fixed v0.1.1, awaiting in-game proof)

- **Symptom:** Marking Shot in Buff mode stayed red while the game showed the buff counting down.
- **Root cause (most likely):** "you" is learned only from the login/zone player-info message.
  Hawkeye started mid-session (and restarted after I8) never got it, so `IsCurrentPlayer` was false
  for everyone and every buff and cast was filtered out. NOT proven: the old log had no buff lines.
- **Fix:** identify you from a cast plus the matching cooldown update (only yours are sent);
  accept tracked buffs from anyone until you are known; log `[ME]`, `[TRACK]`, `[SEEN]`.
- **Still possible:** Marking Shot's effect uses a different id than the skill. The `[SEEN]` lines
  will show the real id after one cast.

### I10 - The I9 guess flipped "you" to other rangers (fixed v0.1.2, awaiting in-game proof)

- **Symptom:** the meter's timer jumped between fights and another ranger's row became "you"
  (green). The owner's Marking Shot icon restarted on other people's casts and looked like a cooldown.
- **Root cause:** I9's rule "a cast plus a cooldown update for that skill within 2 s = you" assumed
  the cooldown message (`0x3847`) comes only after your cast. The server also re-sends your whole
  list regularly, with "0 ms left" for ready skills. Any ranger casting a skill you also have within
  2 s of such a list "matched". The rule also ran when you were already known (from the login
  message), so it overwrote the truth, back and forth. The active fight follows "your" last hit, so
  it followed whichever ranger was "you" at the time; their buffs were accepted as "on you".
- **Evidence:** log 2026-11-21: "you known: True" for entity 8068 from the start, then 12 `[ME]` lines
  switching between 8068, 8511 and 12418; 8511's and 12418's 15 s Marking Shot buffs logged as
  "on you: True", while the owner's own is 10 s.
- **Fix:** the server's identification is final (`MarkCurrentPlayer` refuses once you are known, and
  the login message clears any wrong guess). A guess needs two cooldown STARTS (0 or unseen to
  counting, or jumping back up) each matched to exactly one caster, with no rival within 2 votes.
  The meter now sticks to your fight however long since your last hit. Tests:
  `TrackerIdentityTests`, `SessionModesTests.MeterSticksToYourFight...`.
- **Lesson:** a fallback heuristic must never override a fact, and "only X sends Y" claims about
  the protocol need checking against a log, not assuming. Log the evidence for every guess.

Next free number: **I11**.

## CHANGES

- 2026-11-20: created with I1 to I7 from the first session.
