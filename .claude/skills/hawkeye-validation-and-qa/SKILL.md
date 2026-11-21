---
name: hawkeye-validation-and-qa
description: >
  What counts as evidence in Hawkeye before anything is called fixed, working, tested or done:
  the evidence ladder, the render harness (check visuals with no game running), the live smoke run,
  the in-game test protocol the owner runs on a training dummy, and the current coverage holes.
  Load before claiming a result, before handing a build to the owner, or when planning a test.
  Does NOT cover change classes and gates (hawkeye-change-control) or symptom triage
  (hawkeye-debugging-playbook).
---

# Hawkeye Validation and QA

As of v0.1.0, 2026-11-20.

## 1. The evidence ladder (say which rung you reached)

1. **Builds.** `dotnet build Hawkeye.slnx` with 0 errors. Proves almost nothing about behaviour.
2. **Unit tests.** `dotnet test --project Hawkeye.Tests/Hawkeye.Tests.csproj`. 5 tests at v0.1.0:
   damage stats, timer expiry, timer reset by 0 ms, settings round-trip, corrupt settings file.
3. **Rendered.** The page or window drawn to an image and LOOKED AT (section 2).
4. **Ran.** Hawkeye launched; the log shows "LOCKED" and "SYNCHRONIZED"; screenshot taken.
5. **In game.** The owner (or a session with the owner) saw it work in a real fight (section 3).

"Fixed" means the rung that matches the bug. A tracker bug seen in game is not fixed at rung 3.
Report the rung reached in plain words, and what was NOT checked.

## 2. Checking visuals without the game

- **Meter page:** build a static HTML page that inlines `Pages/MeterPage.razor.css` and
  `wwwroot/css/hawkeye.css` with sample rows, then screenshot it headless:
  `"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe" --headless=new --disable-gpu
  --hide-scrollbars --force-device-scale-factor=2 --window-size=330,320 --screenshot=OUT.png file:///PAGE.html`
- **Tracker icons:** a small console project in the session scratchpad that references
  `Hawkeye.App/Hawkeye.App.csproj`, creates `TrackerIconWindow`s with real `TimedEventTracker`s,
  lifts each window's `Content` into a `StackPanel`, and saves it with `RenderTargetBitmap`.
- **Window behaviour** (click-through, z-order): the same harness can read the extended window
  style (`GetWindowLongPtr(-20)`, bit `0x20` = click-through) and walk the z-order with
  `GetWindow(h, GW_HWNDPREV)`. This is how I1 was proven.
- You cannot click Hawkeye from a script while the game has focus (I3). Use the harness.

## 3. In-game test protocol (needs the owner)

Training dummy, Hawkeye running (as administrator for FPS), Settings closed.

| # | Do | Expect |
|---|---|---|
| 1 | Hit the dummy for 30 s | Your row appears in teal; DPS climbs; timer counts |
| 2 | Compare total damage with the game's own numbers | Within a few percent. A big gap = lost hits (protocol skill section 4) |
| 3 | Cast a tracked Cooldown skill | Its icon darkens with a countdown matching the game's cooldown |
| 4 | Apply a tracked Buff skill (e.g. Marking Shot) | Green frame and countdown; red pulse when it ends |
| 5 | Open Settings, drag an icon that existed before Settings opened | It moves; position survives a restart |
| 6 | Status line | Ping close to the game's; FPS close to the game's own counter |

Record results in `HANDOFF - CURRENT SESSION.md`. If 2 fails, turn on `RecordPackets` and keep
the capture for offline work.

## 4. Coverage holes (as of 2026-11-20)

- Nothing in sections 3.1 to 3.6 has been run yet.
- No test covers the decoders; a recorded capture plus a replay test is the missing piece.
- No test covers `OverlayManager` sync (add/remove skill opens/closes windows).

## When NOT to use this skill

- Deciding if a change is allowed: hawkeye-change-control.
- Something is broken and you do not know why: hawkeye-debugging-playbook.

## CHANGES

- 2026-11-20: written for v0.1.0, modelled on Korathia's olympus-validation-and-qa.
