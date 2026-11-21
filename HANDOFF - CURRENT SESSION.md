# HANDOFF - CURRENT SESSION

Append-only running record. Write DURING the work, not after: a session that dies mid-batch is
resumed from here. The CHECKPOINT block at the END is the live state; everything above it is history.

## Session 2026-11-20 (first session)

- Studied the RATmeter app the owner used (logs, debug symbols, settings), then its GPL-3.0 source.
- Forked to `simbariya/AIon2-Dps-Meter` (public), cloned to `my-meter`, branch `skill-tracker`.
- Installed the .NET 10 SDK (10.0.404) with winget.
- Built the skill tracker; owner then asked for: closer layout, square corners, Deadshot drag fix,
  Cooldown/Buff mode per skill, rename and strip to meter + opacity + drag, ping/FPS/CPU/RAM with
  switches, a new logo, and this documentation system copied from Korathia.
- Name chosen by the owner: **Hawkeye**. Logo chosen: **option 2** (crosshair eye) from
  `C:\Users\Bison\Documents\Aion 2 DPS meter\icon-options\hawkeye-icon-sheet.png`.
- Spend: one gpt-image-2 image, $0.165, authorised by the owner ("use my openai credits").
  The key is read from the Korathia repo's `.env`; it was never copied into this repo.

## CHECKPOINT (2026-11-20, end of session 1)

- **Tree:** all v0.1.0 work is in the working tree of `my-meter` on branch `skill-tracker`.
  NOTHING IS COMMITTED. Git has no user name/email configured for this repo; the owner has not
  said which identity to use. Ask before the first commit. Push only on the owner's word.
- **Gates at this point:** `dotnet build Hawkeye.slnx` clean; `dotnet test` 5/5 pass; app launches,
  locks onto the game traffic, live ping shown; meter and icon visuals checked in the render
  harness; drag fix proven in the harness.
- **Open, needs the owner in game** (`hawkeye-validation-and-qa` section 3):
  1. DPS rows during a real fight (training dummy) vs the in-game damage numbers.
  2. A Ranger cooldown icon darkening and counting down after a cast.
  3. Marking Shot in Buff mode: does a buff/mark event arrive, with which id?
  4. FPS when Hawkeye is run as administrator.
  5. Drag an icon that existed before Settings opened (the Deadshot case) in the real app.
- **Known inherited weakness:** damage-message decode failures (see SYSTEMS section 2). Measure
  with `RecordPackets` on a dummy fight before trying to fix.
- **Owner preferences learned:** one look only, square corners, keep it simple, plain-language
  explanations, show visual choices as images.

## CHECKPOINT (2026-11-20, v0.1.1) - supersedes the one above

- Owner reported: Marking Shot buff icon never counted down; Hawkeye crashed twice.
- Crash (I8): Windows event log showed the WebView2-disposed error; reproduced in a harness and
  fixed (deferred close + logged safety net). Proven at the harness rung, not yet in the real app.
- Buff (I9): most likely cause is Hawkeye not knowing which entity is the owner when started
  mid-session. Fixed by identifying the owner from cast + cooldown echo, and accepting tracked buffs
  from anyone until known. Unit-tested (10/10). NOT yet proven in game.
- NEXT, with the owner in game: start Hawkeye, cast Deadshot once, then Marking Shot, then read the
  log for `[ME]`, `[TRACK] buff Marking Shot` and `[SEEN]` lines. If no `[TRACK] buff` line appears
  but a `[SEEN]` line does, Marking Shot's effect uses a different id: point the tracker at it.
- Still nothing committed; git identity still not given.

## CHECKPOINT (2026-11-21, v0.1.2) - supersedes the ones above

- Owner confirmed in game: v0.1.1 buff tracking and crash fix work.
- Owner reported: meter timer jumped between fights, another ranger shown as "you", Marking Shot
  (Buff mode) "also showing cooldown". One mechanism for all three: I10 (the I9 guess overrode the
  login identity). Fixed; 22/22 tests. NOT yet proven in game.
- Also built (owner requests): total damage and deaths switches (deaths per boss across pulls),
  dummy Timed 1/2/3 min and Start/Stop, coloured ping, clickable FPS restart-as-admin, cut-out logo
  over the frame corner, no resize grip (edge resize), tracker icons only in boss/dummy fights with
  an "Always show" switch. Confirmed by code + data: only bosses (1,344) and dummies (164) in
  mobs.json open a fight.
- The owner's Hawkeye was running during the build, so the normal output folder still holds
  v0.1.1. Verified build is in the session scratchpad. NEXT: owner closes Hawkeye, then
  `dotnet build Hawkeye.slnx` and relaunch.
- NEXT in game: exactly one `[ME]` line or none per run; meter stays on your fight; edge resize;
  FPS click; a wipe shows deaths; dummy START/STOP; icons appear on the first boss hit.
- Still nothing committed; git identity still not given.

## CHECKPOINT (2026-11-21, after packaging) - supersedes the v0.1.2 one

- Git identity given (repo-local): Simba. First commit `4702a6e` on `skill-tracker`, LOCAL ONLY,
  NOT pushed. The 3 Korathia-derived skills are kept out of git via `.git/info/exclude`.
- Friend zip: `C:\Users\Bison\Documents\Aion 2 DPS meter\release\Hawkeye-0.1.2-win-x64.zip`
  (69.7 MB, .NET bundled). Launch-tested: locked onto game traffic.
- "Logo too big" (owner, after restarting): they ran the Debug folder that could not be rebuilt
  while Hawkeye was open, so old meter markup met new CSS; `--logo-size` was undefined there and
  the image showed at its natural 128 px. Debug output rebuilt 00:43; awaiting the owner's look.
- GPL: before the zip goes beyond one friend, push the source so the README's source link holds.

## CHECKPOINT (2026-11-21, v0.1.3) - supersedes the ones above

- Owner: buff sometimes not renewing (I11, worked around, cause open), whole-second countdown,
  status line to the bottom, no scrollbar, meter a bit smaller. All done; 23/23 tests.
- Packages: `..\release\Hawkeye-0.1.3-small.zip` (11.8 MB, needs .NET 10 Desktop Runtime).
  Older 0.1.2 zips and folders are stale; the owner's running copy (elevated, pid 19572 at 00:47)
  is the 0.1.2 small build from `..\release\Hawkeye-small`.
- NEXT in game: after a session, grep the log for `[BUFF-SKIP]` and `[BUFF]` types near any
  recast that got no `[TRACK] buff` line, to decode the I11 variant.

## CHECKPOINT (2026-11-21, v0.1.4) - supersedes the ones above

- Owner asks done: no fight total/deaths line, fit height to group (5 rows max), cooldown sweep
  follows cuts, outlined countdown, DPS "138k/s". 26/26 tests. Visuals checked in harnesses.
- The owner was running the Debug build (pid 43316) during the build, so the Debug folder holds
  v0.1.3 DLLs; its wwwroot is served from source, so a restart WITHOUT a rebuild mixes old code and
  new CSS (the I-logo trap). NEXT: owner closes Hawkeye, then `dotnet build Hawkeye.slnx`.
  Meanwhile a clean 0.1.4 build is in `..\release\0.1.4\Hawkeye` (zip 11.4 MB).
- Not proven in game: fit height with a real party message, the cooldown sweep on a real cut.

## CHECKPOINT (2026-11-21, pushed) - supersedes the ones above

- PUBLISHED: https://github.com/simbariya/hawkeye (public, standalone, not a fork; branch `main`),
  pushed on the owner's word ("push to git, as long as it's a separate project"). CI green
  (build + 26 tests, run 44849994200). Remotes: origin = hawkeye, fork = old GitHub fork (not
  pushed to any more), upstream = RATmeter.
- Version label on the meter (beside the logo) and in Settings, from the csproj <Version>.
- Owner runs `..\release\0.1.4\Hawkeye\Hawkeye.exe` (elevated). Debug output rebuilt and current.
  Share zip: `..\release\Hawkeye-0.1.4-small.zip` (built from `..\release\0.1.4-share`, has the
  version label and the new source link).
- Pushing again: only on the owner's word each time (CLAUDE.md rule 5).

## CHECKPOINT (2026-11-21, v0.1.5) - supersedes the ones above

- Built on request: combat analysis window (click a meter row; layout from the game's own window),
  history of the last 50 fights + kept-forever folder, and "someone else's fight never replaces
  yours" (meter shows your last fight from history between fights). 31/31 tests; analysis window
  checked in a render preview. NOT yet seen in game.
- Odyle tracking is designed but waits on a recording session with the owner (steps in the chat:
  close Hawkeye, set RecordPackets, relog/zone, search the unpacked stream for 360 and 2000).
- Local commits after the last push: not pushed. Push only on the owner's word.
- Packages: `..\release\Hawkeye-0.1.5-small.zip`. Owner runs `..\release\0.1.4\Hawkeye\Hawkeye.exe`.

## CHECKPOINT (2026-11-21, v0.1.6) - supersedes the ones above

- Streamer mode built (OBS Browser Source at http://localhost:7777/). Page verified in headless Edge
  with fake state; HttpListener on localhost verified without admin. NOT yet seen in OBS or with the
  real app running (avoided popping a test copy over the owner's game).
- Odyle recording session still pending. Pushes pending since 032bff4: ask the owner.
