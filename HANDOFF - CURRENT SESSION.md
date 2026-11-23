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

## CHECKPOINT (2026-11-21, v0.2.4) - supersedes the ones above

- LOOKS SETTLED: owner on Monolith v0.2.4: "ok that's a winner". Monolith = matte warm charcoal,
  faint grain, soft corner light, rows in a recessed well, tone-on-tone bands (no hairlines),
  compact 21 px rows. Aurora and Graphite & Vermilion stay as backups in Settings > Look.
  Owner's taste, in order: MINIMAL first, then matte and layered (Korathia card lessons), warm,
  readable; no lines-for-the-sake-of-lines, no neon, no clutter.
- Odyle: login, switch and live spends (0x610C) read; refill estimate WAITING on the owner:
  the cleric's full "695 (+?)" and the refill countdown/interval (refill adds 15 to CURRENT).
- Not pushed since 032bff4 (many local commits). Push only on the owner's word.
- Latest package: ..\release\0.2.4 and Hawkeye-0.2.4-small.zip.

## CHECKPOINT (2026-11-21, v0.2.6 RELEASED) - supersedes the ones above

- PUBLISHED on the owner's word: GitHub release v0.2.6 (https://github.com/simbariya/hawkeye/releases/tag/v0.2.6)
  with Hawkeye-0.2.6-win-x64.zip (.NET included, 68.5 MB) and Hawkeye-0.2.6-small.zip (needs the
  .NET 10 Desktop Runtime, 12.5 MB). CI green. Built from ..\release\0.2.6-github (fresh folder).
- Release recipe: publish small (--self-contained false) and full (--self-contained true
  -p:IncludeNativeLibrariesForSelfExtract=true), single file, DebugType=None; drop the WebView2 xml
  files; add LICENSE.txt and packaging/READ ME FIRST.txt ({VERSION} replaced); zip the Hawkeye folder;
  `gh release create vX.Y.Z` with both zips. Asset names must stay Hawkeye-<ver>-win-x64.zip and
  Hawkeye-<ver>-small.zip (the planned auto-updater will look for them).
- NEXT (owner asked): Clear button on the left strip; auto-update from GitHub releases.
- Local branch backup-before-scrub holds the pre-scrub history (character names): never push it.

## CHECKPOINT (2026-11-21, v0.2.7 built, NOT released)

- Clear button and auto-update built; 53/53 tests (the install script is run for real by a test).
- Local zips in ..\release\0.2.7-github. NOT pushed, NOT released: needs the owner's word.
- 0.2.6 has no updater: the owner installs 0.2.7 by hand once; from then on releases arrive by
  themselves. The first real end-to-end update proof comes with the release after 0.2.7.

## CHECKPOINT (2026-11-21, v0.2.11) - supersedes the ones above

- Live on GitHub: v0.2.11 (latest). PvP mode (crossed swords on the left strip): kills, deaths,
  assists (inferred), AP and rank points gained. SYSTEMS 6f; messages in the protocol skill.
- v0.2.9 and v0.2.10 are BROKEN (no meter window, I15) and still listed as releases. Anyone on them
  must unzip 0.2.11 by hand once. Owner was offered deleting them; not asked for yet.
- I14 is a wrong diagnosis on record; the 3 s restart waits it added are harmless.
- Open: confirm an assist against a called-out one; owner to run 0.2.11 from
  `..\release\0.2.11-github\full\Hawkeye` (his old folders hold broken 0.2.9).
- Release gate now includes starting the packaged exe (build-and-run skill, step 4).
- This checkpoint commit is local only (its push was not part of the owner's "PUSH").

## CHECKPOINT (2026-11-21 evening, v0.2.15 live) - supersedes the ones above

- Live on GitHub: v0.2.14. PvP mode complete: kills/deaths/assists (mob kills ignored), AP and rank,
  End session + History > PvP tab, who-was-it lists, shugo.gg profile popup (on stream too),
  Settings > PvP > Enemy server, remembered totals for the first kill.
- v0.2.15 (tracked skill tick box) released on the owner's word ("push latest to github"), with the
  full documentation pass (START HERE, SYSTEMS 4/6/7, changelog, protocol skill).
- Next session, needs the game running:
  1. First kill exact: start the recorder BEFORE the owner logs in (or goes to character select and
     back); search the login data for his AP total (0x561B values) and rank total.
  2. Target combat power / gear score: the owner targets one player and gives the exact numbers
     Questlog shows; search for them. May be an online lookup instead (official character site).
- Flaky once: UpdateInstallerTests failed in one full run right after a build, passed 6 times after.
  If it fails again, capture which assertion.

## CHECKPOINT (2026-11-22 midday, v0.2.15 live, 0.2.16 local) - supersedes the ones above

- Local, NOT pushed: 0.2.16 = rank line in the PvP panel (class rank, RP to the next rank, hover:
  overall rank, lifetime K/D/A, next update) + exact first-kill AP/rank from the login data; fight
  history 100 (was 50); History > PvP > Select to delete several sessions. 86 tests.
  Parsers replayed against the owner's recorded login (scratchpad `login-1122`, never in git): every
  number matched. Not yet seen live in the app: needs a login with 0.2.16 running.
- Checked: the game does NOT resend the ranking at the hourly update (recording `rank-hour` spans
  13:28 to 13:56: only rank point totals came). The line dims after the update time and says to open
  the PvP ranking in game. Confirmed 14:13: opening the PvP ranking sends 0x8D55 type 1 (class top
  500) and both 0x8D57 lines (recording `rank-open`).
- Still open: target combat power / gear score (needs one target's exact numbers).

## CHECKPOINT (2026-11-22 afternoon, v0.2.16 live) - supersedes the ones above

- v0.2.16 released on the owner's word ("push it"): PvP rank line (dims after the hourly update; open
  the PvP ranking in game to refresh), exact first-kill AP/rank from login data, fight history 100,
  History > PvP > Select, update check every 30 min while open. Packaged exe started clean before
  release (only the expected streamer-port warning from the owner's running copy). 86 tests.
- Not yet seen live in the app: the rank line and first-kill numbers with 0.2.16 itself running at a
  login (the parsers were replayed against recordings with every number matching).
- Still open: target combat power / gear score (needs one target's exact numbers).

## CHECKPOINT (2026-11-22 afternoon, v0.2.16 live, 0.2.17 local) - supersedes the ones above

- Local, NOT pushed: 0.2.17 = Theostone alerts (yours / hitting you, Settings > PvP) + rank line always
  shown with "open Abyss Rank" nudges. Also the unpushed docs commit about `gh release -R`. 90 tests.
- Not seen live: the alerts in a real fight (the decode path is the one that already credits Theostone
  damage in fights; the recording shows both directions).
- Flaky: UpdateInstallerTests.WaitsForTheAppToExit failed again right after a build ("old exe" still
  there); passed on rerun. Its wait is too tight for a busy disk: widen it if it fails a third time.
- Still open: target combat power / gear score; Theostone alerts on the stream page.

## CHECKPOINT (2026-11-22 afternoon, v0.2.17 live) - supersedes the ones above

- v0.2.17 released on the owner's word ("push it"): Theostone alerts + rank line always shown. Packaged
  exe started clean (streamer-port warning only, from the owner's running copy). 90 tests.
- The owner's copy is on 0.2.16, so it should offer 0.2.17 within 30 minutes (first live use of the
  new periodic check).
- Not seen live yet: Theostone alerts in a fight; the rank line with 0.2.17 at a login.
- Still open: target combat power / gear score; Theostone alerts on the stream page.

## CHECKPOINT (2026-11-22 afternoon, v0.2.17 live, 0.2.18 local) - supersedes the ones above

- Local, NOT pushed: 0.2.18 = ranking remembered per character across restarts (the game sends it once
  per hourly update), "reached" shows "official at HH:mm". 91 tests. Plus the docs commit "v0.2.17
  released".
- Not seen live yet: Theostone alerts in a fight; the remembered rank after a restart.
- Still open: target combat power / gear score; Theostone alerts on the stream page.

## CHECKPOINT (2026-11-22 15:00, v0.2.18 released) - supersedes the ones above

- v0.2.18 released on the owner's word ("yep push it"): ranking remembered across restarts. Packaged
  exe started clean apart from the known item below. 91 tests.
- Known, OLD (not from 0.2.16 to 0.2.18): damage messages 0x3804 sometimes fail to parse ("Need 1 bytes
  but only 0 remain" in DamagePacketProcessor.Parse, the inherited reader): 141 in one log on
  2026-11-21, 36 today in the owner's copy. Those hits are skipped. Next: capture a few failing
  messages and see which layout the reader does not know.
- Still open: target combat power / gear score; Theostone alerts on the stream page.

## CHECKPOINT (2026-11-22 afternoon, v0.2.18 live, 0.2.19 local) - supersedes the ones above

- Local, NOT pushed: 0.2.19 = Theostone alerts in their own draggable box (default; or on the meter),
  size and duration settings, debuffs on you stay until they end (0x382C removal), all on the stream
  page. 94 tests; the window rendered off-screen with the harness (scratchpad `theorender`).
- Not seen live yet: the alert box in game, a real cleanse ending an alert, the stream page drawing it.
- Known OLD: damage message parse errors (see the v0.2.18 checkpoint).
- Still open: target combat power / gear score.

## CHECKPOINT (2026-11-23, v0.2.19 live)

- v0.2.19 released (GitHub release v0.2.19, both zips; packaged exe smoke-started: locked on, synced, remembered rank loaded, no errors).
- Still not seen live: the alert box in a real fight, a cleanse ending an alert, the stream page drawing it.

- 0.2.20 local, not pushed: Settings footer Support button now opens the owner's Ko-fi (Branding.SupportUrl).
- v0.2.20 released 2026-11-23 (Ko-fi Support button); packaged exe smoke-started clean.
- 0.2.21 local, NOT pushed: Theostone tiles with words (Silence, Damage 1%...), Poison/Bleed ticks on the tile,
  your debuff on an enemy counts down; 100 tests; WPF tiles rendered (scratchpad theorender) and the CSS tiles
  in headless Edge (scratchpad meterpreview/tiles.html). Not seen live yet.
- v0.2.21 released 2026-11-23 (Theostone tiles); packaged exe smoke-started clean.
- 2026-11-23 cleanup (owner asked): release folder down to 0.2.21-github\full (running copy); old RATmeter
  install folder and all bin/obj deleted (6.65 GB to 218 MB); GitHub releases v0.2.6 to v0.2.20 and their tags
  deleted, v0.2.21 is the only one. New release routine in hawkeye-build-and-run section 4.
- 0.2.22 local, NOT pushed: tiles say YOU, word coloured by grade (green/blue/gold), CRIT / DOUBLE tags; 102 tests;
  rendered both versions (theorender, meterpreview/tiles.html).
- 0.2.22 also: the attacker's name is the tile's top line instead of ON YOU (bottom line no longer shows it).
- v0.2.22 released 2026-11-23 (YOU / attacker name, rarity colours, CRIT / DOUBLE); smoke start clean; GitHub v0.2.21
  deleted (only v0.2.22 left). Owner runs release\0.2.21-github\full (updates in place); release\0.2.22-github\full
  is a spare copy.
- 0.2.23 local, NOT pushed: dummies remembered by entity id (known-dummies.json; owner's file seeded with 10 from
  recordings), so a restart next to a dummy still records it. Cause found from the logs: last dummy fight saved
  22nd 15:23, update restarts mid-game on the 23rd. Owner now runs release\0.2.22-github\full (he started it by
  hand at 12:29), so that folder must stay; 0.2.21-github is the spare now.
- 0.2.23 also: tracked buffs before you are known need your own cooldown start (crowd Marking Shot fix); you are
  named from characters.json after a mid-game restart; meter loop survives a bad frame, EntityTracker maps are
  concurrent. 109 tests. Not seen live yet.
- 0.2.23 also: Theostone alerts in the "Scope" style (owner picked C of 4 mock-ups, scratchpad
  meterpreview/theo-styles.html); boss deaths per pull; details header opaque. 110 tests. Not released.
- v0.2.23 released 2026-11-23 13:13; smoke start clean; GitHub v0.2.22 deleted (only v0.2.23). Local: release\0.2.22-github (owner's install, updates in place) and 0.2.23-github (spare).
- 0.2.24 local, NOT pushed, NOT released: capture no longer sticks on a wrong port (owner switched to a ranger,
  Hawkeye locked onto port 443 at 14:10 and stayed blank). 10 s hit window, 45 s keep-alive proof, 5 min skip;
  resync searches inside the data. 113 tests; replayed raw-copy*.txt recordings (scratchpad syncreplay). The
  owner's install now runs from release\0.2.23-github\full (log 13:14), so 0.2.22-github is the spare. The 0.2.23
  log showed `[DUMMY]` recall working live (entities 31692 and 27610 at 13:14 to 13:15).
- v0.2.24 released 2026-11-23 15:08; smoke start locked 13328 and synced (only the old 0x3804 "Need 1 bytes"
  errors); GitHub v0.2.23 deleted (only v0.2.24). Local: release\0.2.23-github (owner's install, running since
  13:14, updates in place) and 0.2.24-github\full (spare); 0.2.22-github deleted. Awaiting in-game proof of I16.
- 0.2.25 local, NOT pushed: AP found by key 0x37703323 (offset 14), not the per-character number at 6 (Chanter
  farming AP never counted). 114 tests.
- ARENA MODE requested (owner 2026-11-23): opponent CP/GS on load, enemy Defiance cooldown tracker, session
  wins/losses/rating like PvP mode, maybe combat record. Recording scratchpad arena-1123 (1v1 vs a Sorcerer, won
  3-0). Decoded: E319 roster (name, server, rating), E325 kill (killer key, victim key), E326 score per round,
  E328 result + new rating, E32A your rating/wins/losses per arena mode at each zone change; Defiance = skill
  XX26xxxx etc. named "Defiance" (Sorcerer 15240000): 0x3802 cast + 0x382A 5 s buff + 0x382C cleanse.
  WAITING on the owner: CP/GS ground truth, Defiance cooldown, what "combat record" means.
- v0.2.25 released 2026-11-23 15:3x (AP by key); smoke start clean; GitHub v0.2.24 deleted.
- 0.2.26 local, NOT pushed, NOT released: ARENA MODE built (Arena tab in the PvP panel, auto on match load,
  opponent + rating, Defiance countdown from the cast's own cooldown, score, session W/L/rating, stream page).
  Replayed arena-1123 through it (scratchpad arenareplay): right score, Defiance 51.3 s, win +25. Preview:
  scratchpad meterpreview/arena.png. 120 tests. Still waiting on the owner for CP/GS ground truth; "combat
  record" meaning not answered (session W/L/rating built; no saved history yet).
- v0.2.26 released 2026-11-23 16:1x (arena mode); smoke start clean; GitHub v0.2.25 deleted.
- I17: the 0.2.26 cleanup deleted the files of release\0.2.25-github while the owner's Hawkeye ran from it (since
  15:44); restored from the 0.2.26 package the same minute. Local now: 0.2.23-github (old, not running),
  0.2.25-github (owner's install, running), 0.2.26-github\full (spare). Check the newest log before deleting.
- 0.2.27 local, NOT pushed: arena Wins/Losses list (PvP window, Arena mode), enemy Defiance box (draggable,
  Theostone style; render scratchpad defrender/*.png). Owner runs release\0.2.26-github\full since 16:12.
  Recorder running into scratchpad select-1123 (30 min from ~16:25): owner asked to select players whose CP/GS
  questlog shows, to find where CP/GS arrive (not in the arena recording: no message about the opponent holds it).
- v0.2.27 released 2026-11-23 16:2x; smoke start clean; GitHub v0.2.26 deleted. Owner: release and push by default from now on (CLAUDE.md rule 5).
- 0.2.28: arena finds you by key (E343 / result), not name (a 3-2 win read "lost 0-5" after a restart); shugo
  links from each player's server id (EU = ServerMap id + 300); Arena view without Defiance rows. 127 tests.
- 0.2.29: arena profile links only from a known server slug (no Settings fallback); server code shown in the
  list. Unknown on shugo: TAH 1311, PER 1313, BAB 2318 (ask the owner for their shugo.gg server names).
- 0.2.30: one-letter arena names (opponent "X" was missed). 130 tests.
- 0.2.31: 2318 = eu-zikel; server shown by name/code/#id; [ARENA] log has server ids. Recorder arena-1723 running 60 min from 17:2x.
- 0.2.32: arena server = the one after the name (current); only profile-checked servers named; Korean codes dropped (wrong for global).
- 0.2.33: servers 1302 Nezekan, 2304 Lumiel. Unnamed: 1303, 1311, 1313.
