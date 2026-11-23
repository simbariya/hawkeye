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
  60 s. Since v0.2.24 a port must show the bytes 5 times within 10 s to win, and a lock must read a real
  game keep-alive (11-byte `0E 00 36` message, in step) within 45 s, or the port is dropped and skipped
  for 5 minutes (a web connection on port 443 won a lock on 2026-11-23 and kept the meter blank).
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
- **Deaths:** counted per boss type (mob code) for players who hit that boss, this pull: a new fight on it (HP
  reset, idle, new entity) starts them again (v0.2.23; before, they added up over every pull), and so does
  a kill. Log line `[DEATH]`.
- **Training dummy:** `DummyMode` Timed (a run lasts `DummyMinutes`, 1 to 3; hits after that are
  dropped; a new run starts on a hit after a 10 s pause) or StartStop (`StartDummyRun` clears dummy
  fights and counts from your next hit, on that dummy only; `StopDummyRun` freezes it; pauses never
  end it). Log line `[DUMMY]`.
- **You, when the login message was missed (v0.2.23):** worked out from casts that start your cooldowns
  (`TryIdentifyCurrentPlayer`), and from tracked buffs that arrive with your own cooldown start
  (`SettleWaitingBuffs`, below). Then named from `characters.json`: the last character you played with that
  class (`EntityTracker.GuessUserName`, set by `OdyleTracker`).
- **Tracked buffs before you are known (v0.2.23):** counted only when your cooldown for that skill starts within
  4 s of the buff and exactly one player got it; others' casts never touch your icon. The buff message's caster
  field is not reliable (reads 162 for everyone), so the buff's target is what matters.
- **Known dummies (v0.2.23):** `EntityTracker.Dummies` (`Services/Entity/KnownDummies`, file
  `%AppData%\Hawkeye\known-dummies.json`, entity id -> mob code). Every dummy that appears is remembered; a mob met
  without its appear message (Hawkeye started next to it) gets its code back if its id is a known dummy, so the
  boss-only rule still opens the fight. Dummies keep their ids across days; bosses and trash do not, so only
  dummies are kept.
- **Settings:** `DummyMode`, `DummyMinutes` (UI); file only: `BossOnlyCapture` (default true),
  `GroupSummonDamage` (true).
- **Files:** `Hawkeye.Engine/Services/Session/` (`CombatSessionManager.cs`, `TargetEntry.cs`,
  `TargetCombatSession.cs`, `DamageStatisticsCalculator.cs`, `ActiveTargetResolver.cs`),
  `Services/Entity/EntityTracker.cs`.

## 4. The meter window

- **Does:** frameless, always on top, drag anywhere on it, resize from invisible strips on the right
  and bottom edges and the bottom-right corner (no grip). The cut-out logo
  (`wwwroot/images/hawkeye-emblem.png`, cut from `branding/hawkeye-logo.png`) sits over the
  panel's top-left corner, the version at the end of the status line, and a slim strip down the
  left side: History, Extras, PvP mode, Clear (v0.1.10, v0.2.7, v0.2.9). On a window too short for
  all four, the ones that do not fit fold behind a small chevron; clicking it grows the window to
  show them (auto height: a CSS min-height the measure pass picks up; manual: `GrowTo`), clicking
  again folds them (v0.2.14; `hawkeyeSideSlots` / `hawkeyeSideNeed` in index.html, `SideSlots`,
  `SideExpanded`, `SideVisible` in MeterViewModel). Header: boss name (blood red, one line), dummy START/STOP (Start/Stop mode only), fight
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
  `TrackedSkills[]` (`SkillId`, `Mode`, `Active`, `Left`, `Top`). `Active` (v0.2.15, default on; the
  tick box in Settings): off = no icon and no timers (`OverlayManager.SyncIcons`,
  `CombatSessionManager.RefreshTrackedIds`), mode and position kept.
- **Files:** `Hawkeye.App/Views/TrackerIconWindow.cs`, `Services/Windowing/OverlayManager.cs`,
  `Services/Windowing/ClickThrough.cs`, `Hawkeye.Engine/Services/Timed/`.
- **Proven in game (2026-11-21):** Deadshot cooldown and the Marking Shot buff (id 14090000). Not yet: every other Ranger skill.

## 6b. History and combat analysis (v0.1.5)

- **History:** `Hawkeye.Engine/Services/History/` (`EncounterSnapshot`, `EncounterBuilder`,
  `EncounterHistory`). A fight is saved when it ends if you took part (or you are not known yet):
  `CombatSessionManager.RecordEncounter`, called by `TargetEntry` on completion, by `StopDummyRun`,
  and by `CompleteActiveSessions` on exit (`TargetCombatSession.Recorded` stops doubles). Files:
  `%AppData%\Hawkeye\history\*.json` (newest 100 since v0.2.16, was 50; older files deleted) and `history\saved\*.json`
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
  GitHub API `releases/latest`, User-Agent "Hawkeye-Updater", no personal data sent. Checked 20 s
  after start, then every 30 minutes while open (v0.2.16; was 6 hours); a newer release replaces a
  downloaded one that is not installed yet. Self-contained
  builds (compile constant SELF_CONTAINED, set by the csproj when `SelfContained` is true) fetch
  `Hawkeye-<ver>-win-x64.zip`, others `Hawkeye-<ver>-small.zip`: NEVER rename release assets.
  Downloads go to %LocalAppData%\Hawkeye\updates (older ones cleaned at start). Install: batch file
  waits for the PID to exit, robocopies the new folder over AppContext.BaseDirectory, optionally
  restarts. Needs a writable install folder (not Program Files). Works only while the repo is public.
- **Settings:** `AutoUpdate` (on).

## 6f. PvP mode (v0.2.9)

- **Does:** `Engine/Services/Pvp/` (`PvpParser`, `PvpTracker`). Kills: a death (0x8D04) whose
  killer is you (entity or name); deaths: you are the victim; AP gained: rises of the AP item count
  (0x561B, key 0x37703323 at offset 14, count at offset 18; until v0.2.25 it used the number at 6, which differs per character); rank points gained: changes of the 0xE273 total
  (offset 4), negative on death. Assists: someone else's kill followed within 3 s by an AP rise for
  you (inferred: each AP rise belongs to the latest kill within 1.5 s before it). Kills need a victim
  announced as a player (0x3645, `EntityTracker.IsIdentifiedPlayer`): Abyss mobs name the killer and
  give AP too. AP and rank totals are followed all the time; the first change after Hawkeye starts
  only sets the starting point (known gap). The bin button resets the count in PvP mode.
- **Shows:** meter panel instead of the rows (`MeterPage.razor`, `.meter-pvp` in `meter.css`); the
  stream page draws the same panel from `/state.meter.pvp`.
- **Sessions (v0.2.13):** End session (`PvpTracker.EndSession`) hands a `PvpSession` with its `PvpEvent`
  list (who each kill/death/assist was about) to `PvpHistory` (`%AppData%\Hawkeye\pvp-sessions.json`,
  temp-file save, flushed on exit). History window has Fights / PvP tabs; the PvP tab's Select (v0.2.16) ticks sessions and deletes them
  with one save (`PvpHistory.Delete(ids)`). `PvpPage` lists the events
  for the live session or a saved one (`PvpView` says which). `ProfileWindow` shows a shugo.gg profile
  in its own web view with its own data folder (`%LocalAppData%\Hawkeye\ProfileBrowser`). Links:
  `Engine/Services/Pvp/ShugoLinks` (known server ids first, else the `PvpEnemyServer` setting turned
  into a slug like `eu-triniel`). Streamer mode mirrors the popup too (`ICapturableWindow`).
- **First kill (v0.2.14):** `PvpTotalsStore` remembers AP and rank totals per character; the first
  change after a start compares with them within `PvpTracker.MaxApJumpFromMemory` /
  `MaxRankJumpFromMemory` / `MaxRankDropFromMemory`.
- **First kill exact (v0.2.16):** `PvpTracker.OnInventory` keeps the last 4 login inventories
  (0x5611); the first AP change carries AP's inventory key (`PvpParser.ParseApKey`), which finds the
  login total there (`FindCount`). Rank points start from your ranking line (0x8D57). A new character
  name (`EntityTracker.CurrentUserNamed`) clears the totals and rankings.
- **Rank line (v0.2.16):** `PvpRankingParser` reads your line (0x8D57) and the class top 500 (0x8D55);
  `PvpTracker.Standing` (cached until something changes) uses `RankMath.Standing`: your live points
  against the last table, ties not passed. `RankWords` makes the text for the meter (`.pvp-rank`) and
  the stream page (`standing`, `toNext` in `/state.meter.pvp`). Category 0 = overall, other = class.
  The game sends rankings only at login and when the in-game ranking window is opened (not at the
  hourly update, checked 2026-11-22), so past `NextUpdate` the line dims (`RankWords.IsOld`, `.is-old`).
  Since v0.2.17 the line is always there: `RankWords.Line` gives Unknown / Live / Old / Reached, the
  last three nudging "open Abyss Rank". The game sends the ranking once per hourly update (reopening the
  page in the same hour sends nothing), so since v0.2.18 the last one is kept per character in
  `pvp-totals.json` (`PvpTotalsStore.RememberRanking`) and loaded at start and on a character switch;
  "reached" shows "official at HH:mm" until the update.
- **Messages:** layouts in the protocol skill, `PvpParser` and `PvpRanking.cs`.

## 6g. Theostone alerts (v0.2.17, own window and debuffs v0.2.19, tiles and words v0.2.21, grades and crits v0.2.22)

- **Does:** `Engine/Services/Theostone/TheostoneAlerts`.
  - Procs: `DamagePacketProcessor` hands every hit with a Theostone skill code to `OnHit` before its
    own player check. Yours (actor is you) or on you (target is you) only; same stone and direction
    within 1.5 s merge; shown `TheostoneSeconds`. A debuff stone's hit does not mean the debuff landed.
  - Debuffs: `BuffPacketProcessor` reads Theostone ids and calls `OnDebuff(target, instance, name, ms, caster, id)`
    (layout: [id x10][u32 ms][5 bytes][u64 time][caster varint]). On you, or cast by you on an enemy. The tile
    stays until its time runs out or `BuffRemovedProcessor` (0x382C) brings a removal for that entity naming its
    instance (`OnRemoved`; each entry is [reason][instance varint], reason 0 ran out, 7 cleansed, a potion too).
  - Poison/Bleed ticks: `DotDamagePacketProcessor` (0x3805) calls `OnTick`, which only adds to a showing tile.
  - At most 4 at once; plain hits make room first.
  - Words: `TheostoneKinds.Label(id, name)`: id / 10 = 300 F G SS (F family 0 damage / 1 debuff, G grade, SS slot).
    Debuff slots 07 Blind, 09 Silence, 10 Root, 11 Stun, 12 Paralyze, 13 Slow, 14 Attack Slow, 15 Poison,
    16 Bleed; damage slots 17 to 24 = "Damage N%" with the proc chance. Others keep the stone's name.
  - Grade (`TheostoneKinds.Grade`, the G digit): colours the word green / blue / gold. Crit and double come from
    the damage message (`IsCritical`, `IsDoubleDamage`) into `OnHit`; `TheostoneProc.Flags` = CRIT / DOUBLE /
    CRIT DOUBLE (any hit of a merged multi-hit).
- **Shows ("Scope" style, v0.2.23):** side by side, 178 px wide at size M: the Hawkeye eye in a ring (cyan yours,
  red someone else's; a debuff's ring runs down), then YOU or the attacker's name (ON YOU if unknown), the word in
  its grade colour, the damage or seconds left with orange CRIT / DOUBLE and the extra (damage so far or xN),
  over a shade that fades to the right. WPF draws the ring with `Arc()`; the meter and stream page with an SVG
  circle and `stroke-dashoffset`. `Views/TheostoneAlertWindow` (plain WPF, topmost,
  click-through; edit mode with Settings open = dashed box, samples, drag, saved to `TheostoneLeft/Top`), created
  by `OverlayManager.Start`. Or `.theo-alerts` over the meter (`MeterPage.razor`) when `TheostonePlace` is Meter.
  Stream page: `/state.theo` (where, position, scale, items: tag, name, amount, sub, bar, fade), drawn by
  `renderTheo`. Texts shared: `TheostoneAlertWindow.TagText / MainText / SubText`, `TheostoneProc.Word`.
- **Settings:** `TheostoneAlertsYours`, `TheostoneAlertsOnYou`, `TheostonePlace` (Screen), `TheostoneScale`
  (1), `TheostoneSeconds` (3.5), in Settings > PvP.

## 7. Settings window

- **Does:** sections LOOK, WINDOW (auto-update, opacity), METER, TRAINING DUMMY, PVP (enemy server for
  shugo.gg links), STATUS LINE, STREAMER MODE, SKILL TRACKER; footer with the Twitch link and
  Support (placeholder). Opens beside the meter, square, drag by its header.
- **Files:** `Hawkeye.App/Pages/SettingsPage.razor`, `Pages/Components/`, styles in
  `wwwroot/css/hawkeye.css`, window in `Services/Windowing/BlazorWindow.xaml(.cs)`.

## 8. Game data

- **Does:** skill, class, mob names and icon ids (`Hawkeye.Core/GameData/Assets/*.json`); icons are
  downloaded from the game's public CDN on first use and cached in `IconCache/Skills/` next to the exe.
- **Files:** `Hawkeye.Core/GameData/`, `Hawkeye.Core/Data/SkillIconCache.cs`.
