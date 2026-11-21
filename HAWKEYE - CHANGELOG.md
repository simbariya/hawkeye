# HAWKEYE - CHANGELOG

Add a new top entry for every batch of changes. Do not edit past entries except to correct a factual
error. Format and rules: `hawkeye-docs-and-writing` section 3.

## v0.1.4 (2026-11-21)

Fixed:
- **Deadshot's clock sweep went wrong when another skill cut its cooldown.** Every cooldown update
  from the game restarted the timer from full with the new time left, so a 1 s cut made the sweep
  jump back to the start of a shorter cooldown. Now a running cooldown keeps its full length and
  only the time left moves, so the sweep jumps forward by the second that was cut. The full length
  is taken from the game's first update after the cast (the cast message only knows the base
  cooldown: 16.75 s for Deadshot, about 15.7 s in practice with gear). If that update arrives before
  the cast message, the cast no longer overwrites it.
- **Countdown hard to read on bright icons** (Marking Shot: white number on a white icon). The
  number now has a solid black outline like the game's own timers, not a soft shadow.

Changed:
- **DPS as "138k/s"** instead of "137,985/s" ("9.8k" under 10k, "1.24M" from a million).
- **Fight total and total deaths line removed** (took space). Each player's total damage and
  skull/deaths stay, behind their switches.
- **Meter fits your group** (new switch "Fit height to group", on by default): the window makes
  room for everyone in your group, up to 5 rows, and follows it by itself (group size from the
  party message, or the rows on the meter if more). Only the width is dragged by hand then; switch
  it off to size the window yourself.
- 3 new tests (26 total).

## v0.1.3 (2026-11-21)

Fixed:
- **Marking Shot sometimes did not renew on recast (I11).** The log showed 8 recasts in a row
  (00:48:58 to 00:50:01) where the game sent no buff message Hawkeye could read, then they came
  back. Every other recast was followed by a 15 s buff. Root cause of the gap not yet known: buff
  messages that do not fit the expected layout are dropped silently. Fix now: when you cast a
  skill whose buff Hawkeye has seen on you, the buff icon restarts at that buff's last length; the
  real buff message, when it comes, restarts it with the exact value. Plus `[BUFF-SKIP]` log lines
  (raw bytes) for any dropped buff message holding a tracked skill id, and `[BUFF]` lines with the
  message type for tracked buffs, so the next session shows the variant.

Changed:
- **Countdown shows whole seconds** ("8", "7"...) and one decimal only in the last 2 seconds.
- **Status line moved to the bottom** of the meter.
- **Meter a little smaller:** rows 20 px (were 22), names and DPS 13.5 px (were 15), smaller
  header, buttons, summary and logo (40 px, was 46).
- **No scrollbar on the meter** (the rows still scroll with the mouse wheel). Settings has a thin
  dark scrollbar instead of the Windows one.
- 1 new test (23 total).

## v0.1.2 (2026-11-21)

The owner's second session: buffs and the crash fix confirmed working in game ("those work just
fine"). New report: the meter jumped between fight timers and showed another ranger as "you".

Fixed:
- **Another ranger became "you", and the timer jumped between fights (I10).** Caused by v0.1.1's
  "work out who you are" rule. The log showed Hawkeye knew you correctly from the login message,
  then switched "you" between you and two other rangers 12 times. The rule assumed the server only
  sends your cooldown list after you cast. In fact it re-sends the whole list regularly (with
  "0 ms left" for ready skills), so another ranger casting a skill you also have looked like proof.
  Because the meter follows "your" fight, it jumped to whatever that ranger was hitting. The same
  bug fed their 15 s Marking Shot buffs into your icon (yours lasts 10 s), so the icon restarted on
  their casts and looked like a cooldown. Fix:
  - what the server says about who you are is final; a guess never overrides it;
  - a guess only happens when the server has not said, and needs two of your casts that each START
    a cooldown (ready to counting), with no rival close behind;
  - when the server names you later, nobody else stays marked as you.
- **The meter sticks to your fight.** It follows the target you hit most recently for as long as
  that fight is open. Before, 5 s without a hit from you handed it to the busiest fight nearby.
- **Timed dummy runs:** hits after the time limit no longer count (one stray hit used to slip in),
  and the result stays on screen while you keep hitting. A new run starts when you hit again after
  a 10 s pause. Before, the result was wiped 10 s after the limit even if you never paused.

Added:
- **Ping stands out**, bigger and coloured: bright green under 30 ms, yellow up to 100 ms, red above.
- **FPS "run as admin" is clickable**: it restarts Hawkeye as administrator (one Windows prompt).
- **Total damage** switch (Settings > Meter, off by default): each player's total beside their DPS,
  plus the fight's total.
- **Deaths** switch (on by default): a red skull and count on each player who died on this boss,
  added up over every pull (wipes included) until the boss is killed; the fight's total deaths too.
- **Training dummy** section in Settings:
  - **Timed** mode with a 1, 2 or 3 minute run (replaces the fixed one minute);
  - **Start/Stop** mode: a START button on the meter clears the dummy numbers and counts from your
    next hit; STOP freezes the numbers. Only your hit starts a run; pauses never end it.
- **New frame**: the logo is cut out of its square and sits over the panel's top-left corner, as in
  the owner's reference; slight top shading and a drop shadow; status line spread across the width.
- **No resize grip.** Resize from the right edge, the bottom edge or the bottom-right corner.
- **Skill tracker icons only show in a boss or dummy fight** (new default): they appear on the first
  hit and hide 15 s after the last one, and after a dummy run is stopped. They always show while
  Settings is open. New switch Settings > Skill tracker > "Always show" (`TrackerAlwaysVisible`).
- **Credits footer in Settings**: "Hawkeye by Simba", a Twitch button (twitch.tv/drake22x) and a
  Support button placeholder. Fill `Branding.SupportUrl` with the Ko-fi link to switch it on.
  Also in the README.
- **Shareable zip** (`Hawkeye-0.1.2-win-x64.zip`, outside the repo in `..\release\`): .NET bundled,
  with a plain "READ ME FIRST.txt" for a friend and the GPL licence. Recipe in
  `hawkeye-build-and-run` section 4.
- 13 new tests (22 total): the I10 bug, deaths across pulls, the sticky meter, both dummy modes,
  when a boss fight counts as on.

Not yet proven in the real app: resizing from the edges, the restart as administrator, deaths in a
real wipe.

## v0.1.1 (2026-11-20)

The owner's first real session: two crashes, and Marking Shot's buff icon never lit up.

Fixed:
- **Hawkeye crashed (twice in a session).** Root cause: the Settings close button lives inside the
  window's own web view. Closing the window in the click destroyed the web view while the page was
  still handling that click; the redraw that follows then hit the destroyed control and the error
  killed the process. Same risk on the meter's close button. Fix: both now close AFTER the click
  finishes. Plus a safety net: any unexpected UI error is logged at ERR and Hawkeye keeps running.
  Reproduced in a harness: old close = 3 x "CoreWebView2 members cannot be accessed after the
  WebView2 control is disposed"; new close = 0 errors (I8).
- **Marking Shot (Buff mode) never counted down.** Root cause (most likely, not yet confirmed in
  game): Hawkeye only learns which character is you from the login/zone message. Started
  mid-session it never saw it, so every buff looked like someone else's and was dropped. Your casts
  were dropped the same way. Fix: Hawkeye now works out who you are from your own casts (a cast
  followed within 2 s by the server's cooldown update for that skill can only be yours), and until
  it knows, a buff for a TRACKED skill is shown from anyone rather than ignored (I9).

Added:
- **Tracker evidence in the log** (`%AppData%\Hawkeye\logs`): `[ME]` when you are identified,
  `[TRACK]` for every cast, cooldown update and buff of a tracked skill (with who, on whom, how long),
  `[SEEN]` for every buff you cast, with its id. If an icon still does nothing, these lines show why.
- 5 new tests for the identification and buff rules (10 total).

## v0.1.0 (2026-11-20)

First version. Forked from RATmeter v1.11.7 (GPL-3.0), renamed Hawkeye, cut down to the meter and
a skill tracker, and restyled to the owner's reference screenshot.

Added:
- **Skill tracker.** Each tracked skill is its own small, square, always-on-top icon. While
  Settings is open you drag the icons anywhere; when Settings closes they lock and become
  click-through. Each skill is tracked as either:
  - **Cooldown**: bright when ready, dark clock sweep and countdown while cooling, yellow for the
    last 2 s, short orange flash when ready again.
  - **Buff**: bright with a green frame, countdown and a time-left bar while up, yellow for the last
    3 s, dimmed with a pulsing red frame when it has run out (time to reapply). Counts buffs on you
    and effects you put on others (e.g. a mark on the boss).
  "Test timers" in Settings fakes a timer on every icon to check the look without fighting.
  Lives in `Hawkeye.App/Views/TrackerIconWindow.cs` and `Services/Windowing/OverlayManager.cs`.
- **Status line** under the meter header: ping (from the game's own server-time message), game FPS
  (Windows' frame events for `AION2.exe`, needs administrator), system CPU and RAM. Each one has
  its own switch in Settings. `Hawkeye.App/Services/SystemStatsService.cs`.
- **New settings file** `%AppData%\Hawkeye\settings.json` (`Hawkeye.Engine/Settings/`). Saved
  through a temp file so a crash mid-save cannot corrupt it; a corrupt file falls back to defaults.
- **Hawkeye logo and app icon**: option 2 (crosshair eye) of six gpt-image-2 concepts generated in
  one image ($0.165). Master at `branding/hawkeye-logo.png`.

Changed:
- **The meter look** now follows the owner's screenshot: square panel `#21232E` with a tan border,
  logo, grey bold target name and timer, 22 px bars with 6 px gaps, orange `#DE893A` bars with a
  lighter edge, teal `#3FB292` for you, class icon inside the bar, outlined white name, exact DPS
  in outlined green `#58FF77` ("137,985/s"). Bars are relative to the top player.
- **Settings** cut to: opacity, the four status-line switches, and the tracker (on/off, icon size,
  tracked skills with a Cooldown/Buff switch each, add by name, Test timers).
- Logs moved to `%AppData%\Hawkeye\logs`, kept 7 days.
- All names, namespaces, window titles and the .exe are Hawkeye now. `LICENSE` keeps the original
  copyright line and the README credits RATmeter, as GPL-3.0 requires.

Removed (see `PARKED IDEAS.md`):
- Combat history and its SQLite database, player details window, stat efficiency calculator,
  auto-updater and update checker (it pointed at the upstream releases), the What's New window,
  the old buff overlay bar, the old Standard and Compact layouts and the layout picker, the
  show/hide hotkey, nickname hiding, class colours, row size.

Fixed:
- **An icon that existed before Settings opened could not be dragged** (the owner reported it on
  Deadshot). Root cause: icons and Settings are all always-on-top windows, and among those the most
  recently activated one is drawn on top. Deadshot's default spot was under where Settings opens,
  so Settings covered it. Icons added while Settings was already open were created later and sat on
  top, which is why only the older one failed. Fix: entering edit mode lifts every icon above all
  other windows without taking focus, and clicking into Settings lifts them again. Proven in the
  harness: before, "icon above settings = False"; after, "True" (`hawkeye-failure-archaeology` I1).
- **Adding a cooldown skill closed the wrong picker** (upstream bug): it reset the buff picker's
  flag instead of the cooldown picker's. Gone with the settings rewrite.
- **Status line items ran together** ("18 ms FPS admin CPU 34%"). Root cause: the items were joined
  with runs of spaces, which HTML collapses to one. Fix: each item is its own element with a 12 px gap.
