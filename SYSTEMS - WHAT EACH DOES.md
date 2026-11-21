# SYSTEMS - WHAT EACH DOES

Every live system in Hawkeye: what it does, which settings drive it, which files own it.
As of v0.1.2, 2026-11-21. Update a system's section IN THE SAME COMMIT as any change to how it works.

Settings live in `%AppData%\Hawkeye\settings.json`, shape defined in
`Hawkeye.Engine/Settings/HawkeyeSettings.cs`. Change them in code only through
`ISettingsService.Update(...)`, which saves and raises `Changed`.

## 1. Capture (finding and reading the game's traffic)

- **Does:** opens every network adapter through Npcap, watches for the game's regular heartbeat
  bytes, picks the adapter and port where they appear (prefers loopback, so VPNs and ping reducers
  work), then captures only server-to-you TCP on that port. Re-detects if the heartbeat stops for
  60 s.
- **Settings:** none in the UI. `RecordPackets` (file only) also saves the raw stream to `%AppData%\Hawkeye\PacketLogs\`.
- **Files:** `Hawkeye.Engine/PacketCapture/CaptureDevice.cs`, `TcpStreamBuffer.cs`,
  `PacketAccumulator.cs`, `FilePacketWriter.cs`; replay: `FilePacketCaptureDevice.cs`.
- **Known weakness (inherited):** heavy fights log many `[TCP GAP]` warnings (missed segments),
  which can drop hits. Not yet measured in Hawkeye.

## 2. Decoding (bytes to game events)

- **Does:** splits the stream into messages (varint length, 2-byte opcode), unpacks LZ4 bundles
  (opcode `FF FF`), and routes each opcode to its processor. Processor list and message layouts:
  `hawkeye-protocol-and-captures`.
- **Files:** `Hawkeye.Engine/PacketProcessing/` (`PacketProcessor.cs`, `Routing/PacketOpcodes.cs`,
  `Processors/*.cs`, `Shared/PacketReader.cs`).
- **Known weakness (inherited):** the old meter logged ~80,000 "Need N bytes but only N remain"
  failures on the damage message (`0x3804`) over 17 days of the owner's logs. Every one is a hit the
  meter never counted. Root cause not yet found; RATmeter v1.11.7 may already have fixed part of it.

## 3. Combat sessions (who did how much to what)

- **Does:** keeps one fight per target, picks the active target, attributes pet and summon damage
  to the owner, counts deaths, computes per-player damage, DPS and share.
- **Which fights count:** only mobs flagged as a boss (1,344) or a training dummy (164) in
  `mobs.json`. Trash mobs never open a fight. A boss added by a game patch and missing from
  `mobs.json` is NOT counted until the file is updated.
- **Which fight is shown:** the one you hit most recently, for as long as it is open (sticky, v0.1.2).
  Only when you have hit nothing open does it show the busiest fight.
- **Deaths:** counted per boss type (mob code) over every pull, wipes included, for players who hit
  that boss; reset when the boss is killed. Log line `[DEATH]`.
- **Training dummy:** `DummyMode` Timed (a run lasts `DummyMinutes`, 1 to 3; hits after that are
  dropped; a new run starts on a hit after a 10 s pause) or StartStop (`StartDummyRun` clears dummy
  fights and counts from your next hit, on that dummy only; `StopDummyRun` freezes it; pauses never
  end it). Log line `[DUMMY]`.
- **Settings:** `DummyMode`, `DummyMinutes` (UI); file only: `BossOnlyCapture` (default true),
  `GroupSummonDamage` (true).
- **Files:** `Hawkeye.Engine/Services/Session/` (`CombatSessionManager.cs`, `TargetEntry.cs`,
  `TargetCombatSession.cs`, `DamageStatisticsCalculator.cs`, `ActiveTargetResolver.cs`),
  `Services/Entity/EntityTracker.cs`.

## 4. The meter window

- **Does:** frameless, always on top, drag anywhere on it, resize from invisible strips on the right
  and bottom edges and the bottom-right corner (no grip). The cut-out logo
  (`wwwroot/images/hawkeye-emblem.png`, cut from `branding/hawkeye-logo.png`) sits over the
  panel's top-left corner, the version just under it, and History / Extras buttons in a slim strip
  down the left side (v0.1.10). Header: boss name (blood red, one line), dummy START/STOP (Start/Stop mode only), fight
  timer ("ready" while waiting for your first hit), Settings, Close. Optional status line AT THE
  BOTTOM (v0.1.3). (The fight total / deaths summary line was removed in v0.1.4.) One row per player, sorted by damage, bar relative to the top
  player, eased so it glides; optional skull + deaths and total damage. Hover a row for exact damage,
  share and deaths.
- **Fit height to group** (`AutoHeight`, on): rows for max(group size from the party message
  `0x9702`, rows on the meter), 1 to 5; the page measures itself (`hawkeyeMeasure` in index.html)
  and sets the window height. Only the right edge resizes then. DPS shown short ("138k/s").
- **Settings:** `Opacity`, `AutoHeight` (on), `ShowTotalDamage` (off), `ShowDeaths` (on),
  `WindowLeft/Top/Width/Height` (saved 1 s after you stop moving it).
- **Files:** `Hawkeye.App/Views/MainWindow.xaml(.cs)`, `Pages/MeterPage.razor` + `.razor.css`
  (all colours at the top of the css), `ViewModels/MeterViewModel.cs`.

## 4b. Looks (v0.2.0)

- **Does:** `Settings.Look` (Monolith default, Aurora, Graphite) sets a `theme-*` class on the meter
  (`.shell`), the analysis, Settings, History and Extras roots, and on the stream meter.
  `Services/Looks.cs` maps look to class, name and meter row metrics (row height / gap, which
  "fit height to group" needs). Meter styles: `wwwroot/css/meter.css` (shared by the meter window and
  the stream page). Window palettes: `theme-*` blocks in `wwwroot/css/hawkeye.css` (variables
  `--w-*`, which each window's own colours fall back from). Per-look touches of the analysis window
  at the end of `Pages/DetailsPage.razor.css`.
- **Design principles** (owner, from the Korathia combat-card rounds, local only:
  the Korathia project's combat-card lessons file (kept outside this repo)):
  layered not flat (frames within frames, recessed wells with a lip); MATTE (flat velvety fills, no
  gloss lines, no hard 1px white highlights); soft rims (one soft border plus a fading halo, never
  hard double rings); emphasis as a seep glow, not a harder outline; a soft light source and a
  little texture rather than bare black. Monolith follows them since v0.2.3.

## 5. Status line (ping, FPS, CPU, RAM)

- **Does:** refreshes once a second.
  - Ping: from the game's server-time message (`0x3603`). Bigger and coloured: green under 30 ms,
    yellow up to 100 ms, red above (`MeterViewModel.PingCss`).
  - FPS: counts the frames `AION2.exe` hands to Windows (DXGI events, the same source PresentMon
    uses). Shows "FPS: run as admin" unless Hawkeye runs as administrator; clicking it restarts
    Hawkeye elevated (one Windows prompt). "-- FPS" when the game is closed.
  - CPU and RAM: whole system.
- **Settings:** `ShowPing`, `ShowFps`, `ShowCpu`, `ShowRam`. Turning FPS off stops the frame counter.
- **Files:** `Hawkeye.App/Services/SystemStatsService.cs`.

## 6. Skill tracker

- **Does:** one window per tracked skill (max 10). Cooldown or Buff mode per skill (states in the
  changelog v0.1.0). Edit mode while Settings is open: dashed outline, CD/BUFF tag, draggable,
  raised above everything. Otherwise click-through and never takes focus.
- **Where the timers come from:**
  - Cooldowns: the game's cooldown message (`0x3847`, a list of skill id + ms left) and your own
    skill casts (`0x3802`). 0 ms left clears the timer (cooldown reset).
  - Buffs: the buff messages (`0x382A`, `0x382B`) where you are the target OR the caster.
  - Both timer sets are `TimedEventTracker` instances, keyed `TimerKeys.Cooldowns` and `.Buffs`.
- **Knowing who you are** (v0.1.2): from the login/zone message, which is final. Only if Hawkeye
  missed it: a guess from two of your casts that each START a cooldown within 2 s (the cooldown
  list is also re-sent regularly, so a plain update proves nothing: I10). Until known, buffs for
  tracked skills are accepted from anyone. Log lines: `[ME]`, `[TRACK]`, `[SEEN]`.
- **Recast fallback** (v0.1.3, I11): your cast of a skill whose buff was seen on you restarts the
  buff timer at its last seen length; the real buff message corrects it. Log `[TRACK] buff ...
  restarted from your cast`, `[BUFF]`, `[BUFF-SKIP]`.
- **Countdown:** whole seconds, one decimal in the last 2 s, white with a black outline
  (`Views/OutlinedText.cs`), yellow when ending.
- **Cooldown updates** (`TimedEventTracker.UpdateTimeLeft`, v0.1.4): a running cooldown keeps its
  full length and only the time left moves (cuts make the sweep jump forward). An update within
  1.5 s of the start sets the full length (the real cooldown; the cast only knows the base one).
- **Buff mode** reads only buffs ON you (the game's caster field is a fixed 161 for these, so "by
  you" never matches). Your Marking Shot buff is 10 s; other rangers' are 15 s.
- **When the icons show:** by default only during a boss or dummy fight
  (`CombatSessionManager.IsInBossFight`: the shown fight is open, not a stopped dummy run, and was
  hit in the last 15 s), plus always while Settings is open. `OverlayManager` checks 4 times a second
  and hides or shows the icon windows (never taking focus). The timers keep running while hidden.
- **Settings:** `TrackerEnabled`, `TrackerAlwaysVisible` (false), `TrackerIconSize` (24 to 96),
  `TrackedSkills[]` (`SkillId`, `Mode`, `Left`, `Top`).
- **Files:** `Hawkeye.App/Views/TrackerIconWindow.cs`, `Services/Windowing/OverlayManager.cs`,
  `Services/Windowing/ClickThrough.cs`, `Hawkeye.Engine/Services/Timed/`.
- **Proven in game (2026-11-21):** Deadshot cooldown and the Marking Shot buff (id 14090000). Not yet: every other Ranger skill.

## 6b. History and combat analysis (v0.1.5)

- **History:** `Hawkeye.Engine/Services/History/` (`EncounterSnapshot`, `EncounterBuilder`,
  `EncounterHistory`). A fight is saved when it ends if you took part (or you are not known yet):
  `CombatSessionManager.RecordEncounter`, called by `TargetEntry` on completion, by `StopDummyRun`,
  and by `CompleteActiveSessions` on exit (`TargetCombatSession.Recorded` stops doubles). Files:
  `%AppData%\Hawkeye\history\*.json` (newest 50; older files deleted) and `history\saved\*.json`
  (kept forever). They hold other players' names: never in git.
- **Which fight the meter shows:** your open fight; else, once you are known, your last fight from
  history (`IEncounterHistory.LastOwn`); someone else's fight never. Only before Hawkeye knows you
  does it fall back to the busiest fight.
- **Combat analysis:** `Pages/DetailsPage.razor` (+ `.css`), fed by `Services/DetailsState.cs`
  (live fight via `CombatSessionManager.BuildLiveSnapshot`, refreshed each second, or a history
  fight). Opened by clicking a meter row or a history entry. `Pages/HistoryPage.razor` lists kept
  and recent fights. Windows: `OverlayManager.OpenDetails/OpenHistory`.

## 6c. Streamer mode (v0.1.6)

- **Does:** with `StreamerMode` on, `Services/Streaming/StreamServer.cs` runs an `HttpListener` on
  `http://localhost:{StreamerPort}/` (localhost only; no admin needed). `/` is the page
  (`wwwroot/stream/index.html`, `stream.css`, `stream.js`), `/state` is JSON, anything else a file
  from `wwwroot` (logo, class icons). Page files are EMBEDDED in the exe (`hawkeye-web/...`
  resources, see the csproj), so Debug, publish and single-file builds all serve them.
- **State:** built on the UI thread every 100 ms (it reads the meter window and the icon windows)
  from the same `MeterSource` as the meter, plus the status line, icon positions (`IconLeft/Top`,
  WPF units) and timers; the HTTP thread hands out the last JSON. The page scales everything by
  `innerWidth / screen.width`, so the Browser Source must be the screen's aspect (resolution).
- **Look:** `stream.css` copies the meter CSS and the icon colours; keep it in step with
  `MeterPage.razor.css` and `TrackerIconWindow.cs`.
- **Hawkeye's windows** (v0.1.8): every open `BlazorWindow` (Settings, History, Extras, analysis;
  `OverlayManager.OpenPageWindows`) is captured as a PNG every 250 ms (`BlazorWindow.CaptureAsync`,
  WebView2 `CapturePreviewAsync`), served at `/window/<id>.png`; `/state` lists them with position,
  size and a version that only changes when the picture does. The page swaps pictures without flicker.
- **Self-reload:** `/state.instance` is new per Hawkeye run; the page reloads when it changes, because
  OBS keeps the first page it loaded.
- **Settings:** `StreamerMode` (off), `StreamerPort` (7777, file only), `StreamShowMeter`,
  `StreamShowIcons`, `StreamShowDetails` (= "Hawkeye windows on stream"; all on).

## 6d. Odyle and Extras (v0.1.7)

- **Does:** `PacketProcessing/Processors/CountersProcessor.cs` reads `0x610B` with
  `Services/Odyle/OdyleParser.cs` (layout: protocol skill section 4). `OdyleTracker` holds the
  reading until `EntityTracker.CurrentUserNamed` fires (the name comes ~4 s later; 20 s window);
  a reading with no switch after it goes to the character you are on. Saved per character to
  `%AppData%\Hawkeye\characters.json` (name, class, current, pool, verified time).
- **Shows:** `Pages/ExtrasPage.razor` (button on the meter, `OverlayManager.OpenExtras`).
- **Live changes** (v0.1.12): `0x610C` updates (cube loots and other spends) go straight to the
  character you are on (`OdyleTracker.OnUpdate`), only when the entry has Odyle's key.
- **Caps:** 840 / 2000 constants in `OdyleParser`.
- **Refill estimate** (v0.2.5): `Services/Odyle/OdyleRefill.cs`: +15 to current every 3 h on a
  global timer anchored at 2026-11-21 16:00 UTC, capped at 840. Extras shows estimates as "~N"
  until a reading confirms them. Whether the pool feeds the refill is NOT known (pool shown as read).

## 6e. Clear and auto-update (v0.2.7)

- **Clear:** `MeterSource.Clear` completes all open fights (they go to History) and hides any fight
  started before the clear; the meter stays blank until your next fight.
- **Auto-update:** `Services/UpdateService.cs` (app) + `Engine/Services/Updates/` (`ReleaseInfo`:
  latest-release JSON, version compare, asset choice; `UpdateInstaller`: the install batch script).
  GitHub API `releases/latest`, User-Agent "Hawkeye-Updater", no personal data sent. Self-contained
  builds (compile constant SELF_CONTAINED, set by the csproj when `SelfContained` is true) fetch
  `Hawkeye-<ver>-win-x64.zip`, others `Hawkeye-<ver>-small.zip`: NEVER rename release assets.
  Downloads go to %LocalAppData%\Hawkeye\updates (older ones cleaned at start). Install: batch file
  waits for the PID to exit, robocopies the new folder over AppContext.BaseDirectory, optionally
  restarts. Needs a writable install folder (not Program Files). Works only while the repo is public.
- **Settings:** `AutoUpdate` (on).

## 7. Settings window

- **Does:** opacity, the four status switches, the tracker. Opens beside the meter, square, drag
  by its header.
- **Files:** `Hawkeye.App/Pages/SettingsPage.razor`, `Pages/Components/`, styles in
  `wwwroot/css/hawkeye.css`, window in `Services/Windowing/BlazorWindow.xaml(.cs)`.

## 8. Game data

- **Does:** skill, class, mob names and icon ids (`Hawkeye.Core/GameData/Assets/*.json`); icons are
  downloaded from the game's public CDN on first use and cached in `IconCache/Skills/` next to the exe.
- **Files:** `Hawkeye.Core/GameData/`, `Hawkeye.Core/Data/SkillIconCache.cs`.
