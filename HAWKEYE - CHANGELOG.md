# HAWKEYE - CHANGELOG

Add a new top entry for every batch of changes. Do not edit past entries except to correct a factual
error. Format and rules: `hawkeye-docs-and-writing` section 3.

## v0.2.25 (2026-11-23)

Fixed (owner: "farming mobs on chanter now in abyss in pvp mode - it doesn't count the kills which is right, but
it should count the AP gain from mobs"):
- **AP on every character.** PvP mode found AP by the number at offset 6 of the item-count message (0x1AC7C1 on
  the owner's main). That is the item's own number and differs per character: on his Chanter it was 0x08E6C5, so
  no AP counted there at all (8,192 to 22,672 AP in 10 minutes of farming, none counted; not PvP kills either).
  AP is now found by its key at offset 14 (0x37703323), the same on both characters in all three recordings.
114 tests (1 new).

## v0.2.24 (2026-11-23)

Fixed (owner: "something is off, I logged onto a ranger, and damage training dummy nothing registers"):
- **The meter went blank for good after a character switch.** At 14:06 the game connection went quiet (the
  switch); Hawkeye went back to looking for it, and 4.5 minutes later a web connection (port 443) had shown the
  game's 3 detection bytes 5 times by chance, so Hawkeye locked onto it. Any traffic on the locked port kept the
  watchdog happy, so it never looked again, while the game was back on its usual port 13328. In every one of the
  owner's logs, the 26 locks on 13328 were real and the 2 on 443 / 80 were false.
  Now a port must show the bytes 5 times within 10 seconds (the game gives 40+ in 2), and a lock must read a real
  game keep-alive within 45 s, or Hawkeye drops it, skips that port for 5 minutes and looks again (log
  `[CAPTURE] port N sent no game message ... not the game`).
- **Reading the stream from the right place (inherited from RATmeter):** when it lost step, the reader searched
  for the game's sync bytes past the end of the data instead of inside it, so it re-synced on whatever chunk came
  next. It worked because the game's chunks mostly start on a message. Replaying 4 raw recordings: same messages
  after the fix, plus the 6 to 45 at the start that the old search threw away.
113 tests (3 new).

## v0.2.23 (2026-11-23)

Fixed (owner: "why is the meter not recording when I am dpsing the dummy anymore ... it used to capture dummies
before, but doesn't now", and three more found on the way):
- **Dummy fights after a restart next to the dummy.** A mob's type only comes in the message that makes it appear.
  Since the updater restarts Hawkeye mid-game (several times on 2026-11-23), Hawkeye came back next to a dummy it
  never saw appear, took it for trash, and the boss-only rule dropped every hit (last dummy fight saved
  2026-11-22 15:23; a boss fight at 12:14 on the 23rd was fine). Dummies are fixed NPCs that keep their entity id
  (all 7 in the owner's recordings had the same ids on the 21st and the 22nd), so Hawkeye now remembers every
  dummy it sees in `%AppData%\Hawkeye\known-dummies.json` and recognises it after a restart (log `[DUMMY] entity
  N is a dummy seen before`). Only dummies; an id that belongs to a known player is never taken for one.
  The owner's file was seeded with the 10 dummies from his recordings.
- Until then (or for a dummy never seen): walk away until it leaves your screen and come back, or change zone.
- **Several dummies:** the same cause made only the dummy that came into view after the restart count; hits on
  the others were dropped, so switching dummy did not move the number (owner: "it only records one and the
  number doesn't change if I choose a different one").
- **Other rangers' Marking Shot reset your tracker icon** (owner: "on the boss other rangers attack it with
  Marking Shot, it kept resetting the buff as if I got it ... it should count only my buffs"). Until Hawkeye
  knows which character is you it took a tracked buff from anyone, and in a crowd it never found out (several
  rangers cast the same skills at once, so no cast was clear evidence). Now, while you are unknown, a tracked
  buff counts only if YOUR cooldown for that skill starts within 4 s of it (the cooldown list holds only your
  skills) and only one player got it; each such match is also evidence of who you are, so a crowd no longer
  keeps you unknown. Once known, only buffs on you count, as before. (The buff message's caster field reads
  162 for everyone, so it cannot tell whose buff it is.)
- **Your name after a restart mid-game:** the meter said "Player_9377". Hawkeye now names you from your
  remembered characters: the one you played last with that class (log `[ME] named you ...`).
- **The meter could freeze:** at 11:49 one refresh failed ("Collection was modified": the player list was read
  while the game data changed it) and that stopped the meter's update loop for good. The lists are now safe to
  read from both sides, and a failed refresh skips one frame instead of ending the loop.
- **Boss deaths count per pull:** a wipe and re-pull now starts the deaths from zero (owner: "when a team wipes
  and restarts the boss, the deaths count continues instead of resetting"). They used to add up over every pull
  until the boss died.
- **Skill breakdown header:** scrolling the skill list in the fight details no longer shows the numbers through
  the Crit / Perf header row (its background was see-through in Monolith and Aurora).

Changed (owner: "can godstone alerts be a bit more stylish, not just a box but something minimalistic and uneven,
maybe our Hawkeye logo somewhere ... make a few options and let me choose"; he chose C "Scope" of four mock-ups):
- **Theostone alerts in the "Scope" style:** no box; the Hawkeye eye sits in a ring (cyan for yours, red for
  someone else's), and for a debuff the ring runs down like a clock; the name, the word in its rarity colour and
  the number trail off to the right over a fading shade. Same on screen, on the meter and on the stream page.
110 tests (9 new).

## v0.2.22 (2026-11-23)

Changed (owner: "instead of yours can it just say You, and can the name Damage/Silence etc be of a colour of
rarity? Green/Blue/Gold? Also if damage godstone crits or doubles, can that be represented also, i.e.
crit/crit double/double tags? something small"):
- Tiles say **YOU** instead of YOURS, and **the attacker's name** instead of ON YOU (owner: "instead of ON YOU
  make the person's name in there and save space on the other bit"); the bottom line no longer repeats it.
  ON YOU stays only when the name is not known.
- **The word is coloured by the stone's grade:** green Rare, blue Epic, gold Unique (from the skill id).
- **CRIT, DOUBLE or CRIT DOUBLE** in small orange letters on the tile's bottom line when the hit was one (a
  multi-hit keeps it if any hit was). From the damage message's own flags; in the owner's recording 67 of 147
  Theostone hits were crits, 13 doubles, 7 both, his own Traufnir's Courage included.
102 tests (2 new).

## v0.2.21 (2026-11-23)

Changed (owner: "instead of those big names, just call it Silence/Blind/Paralyze etc. Damage ones should be
Damage 1%, Damage 2% depending on the type. Instead of line message, a box that takes less space but taller,
like a button that appears. When using a pot it should disappear, so it should match the status effects"):
- **Theostone alerts are tiles now**, side by side: YOURS or ON YOU, what it does in a word, then the damage, or
  for a debuff its seconds left with a bar running down, and who it came from. Same on the meter and on stream.
- **Words instead of stone names:** Blind, Silence, Root, Stun, Paralyze, Slow, Attack Slow, Poison, Bleed.
  Damage stones say "Damage N%", N being their proc chance, which is what tells them apart (Fire 1%, Water 2%,
  Earth 5%, Wind 10%; their area versions 2%, 4%, 10%, 20%). Worked out from the skill id; checked against all
  51 stones in the public tables and the 4 debuffs in the owner's recording (Paralyze 3 s, Poison 20 s,
  Silence 4 s and 3 s, all matching).
- **Poison and Bleed** ticks add their damage to the tile; a potion ends it the moment the game says so
  (recording: the owner's potion cleansed a 20 s Poison at 9.4 s).
- **Your own debuff on an enemy** (if your stone is a Blind, Silence...) counts down on its tile until it ends on
  that enemy.
- Note from the recording: a debuff stone's hit comes first and does not mean the debuff landed (three Bleed hits
  and one Silence hit on the owner never became a debuff), so a hit alone stays a short tile.
100 tests (6 new: every published stone gets its word, Poison ticks and a real potion cleanse, your debuff on an
enemy, ticks never make their own tile, someone else's debuff stays hidden).

## v0.2.20 (2026-11-23)

Added (owner: "fetch my kofi link from salsa manga project and insert in the meter"):
- **Support button works:** the "Support (soon)" button at the bottom of Settings is now "Support"
  and opens the owner's Ko-fi page (https://ko-fi.com/X3N527KYCV) in the browser.

## v0.2.19 (2026-11-23)

Added (owner: "make Theostone pop out customizable and draggable ... make sure all of that is
capturable by the overlay"; "Theostones that do a negative debuff, like blind for 6 sec, should remain
as a popup until the effect is gone, either naturally or by potting it"):
- **Theostone alerts in their own box, anywhere on screen** (default). While Settings is open it
  shows a dashed outline with two sample alerts: drag it, the spot is saved. Click-through
  otherwise. Settings > PvP: where (Anywhere / On meter), size (S, M, L, XL), how long a proc
  alert stays (2, 3.5, 5 s).
- **Debuffs on you stay until they end:** a Theostone debuff (silence, blind...) shows its time
  left and a red bar running down, and goes the moment the game says it ended: ran out or cleansed
  (a potion). The hit that brought it merges into the same alert.
- **On stream:** streamer mode draws the alerts at the same spot (or over the meter), same look,
  same countdown.

How it was found (owner's PvP recording, 2026-11-21): a Theostone debuff on you is a buff message
(0x382A, type 19) with its duration and an instance number; the removal message 0x382C names that
instance: a 3 s debuff removed at 3.03 s (reason 0, ran out), a 20 s one at 9.4 s and a 4 s one
at 1.4 s (reason 7, cleansed). Hawkeye's buff reader used to drop these (not tracker skills).
94 tests (3 new, with the real messages: no names in them).

## v0.2.18 (2026-11-22)

Fixed (owner: "game sends updated rank details only once it has updated every hour, doesn't send it
after that when you open again"):
- **Rank remembered across restarts.** The game sends your ranking once per hourly update, so a
  Hawkeye restarted mid-hour (an update install, say) showed "Rank unknown" and opening Abyss Rank
  could not help. The last ranking is now kept per character (`pvp-totals.json`) and shown at start
  (the last character played until the game names you).
- **"Rank reached" says when it becomes official** ("official at 15:28") instead of asking you to
  open Abyss Rank, which sends nothing until the next update. After that time it asks again.
- 91 tests (1 new: the ranking survives a restart).

## v0.2.17 (2026-11-22)

Added (owner: "a subtle pop up when Theostone procs ... and same when it procs on me"):
- **Theostone alerts:** a small strip over the top of the meter for 3.5 s. "THEOSTONE Tassin's
  Command 12.4K" when yours procs; a red "HIT BY Mahisha's Wrath from Bravo 8.1K" when someone's hits
  you. A multi-hit proc is one alert ("x2"); at most 3 at once. Settings > PvP: two switches, both on.
  Procs are ordinary damage hits with a Theostone skill code; in the owner's recorded PvP session his
  own procced 10 times and enemies' hit him 16 times (12 different stones). Damage-free Theostones
  (pure buffs, if any) do not alert.

Changed (owner: "have the line always there"):
- **Rank line always shown in PvP mode.** No ranking yet: "Rank unknown | open Abyss Rank". After
  the hourly recalculation: "Ranger rank 78 | open Abyss Rank" (points to the next rank move to the
  hover). Your points passed the one above since the ranking came: "Ranger rank 77 reached | open
  Abyss Rank", in the accent colour.
- 90 tests (4 new). The update installer test failed once more straight after a build (copy slower
  than its wait) and passed on the rerun.

## v0.2.16 (2026-11-22)

Added (owner: "xx rank points until next rank, display current rank"):
- **Rank line in the PvP panel:** your class rank and the rank points still needed to pass the
  player above, e.g. "Ranger rank 78" and "21 RP to 77". It moves with every kill and death. When
  your live points already beat the one above it says "(next 77)" and aims at the next one. Hover
  for overall rank, lifetime kills / deaths / assists and when ranks update next. Also on the
  stream page.
- **First kill exact:** the AP you logged in with is read from the login inventory, the rank
  points from your ranking line, so the first kill after logging in counts exactly. The remembered
  totals from v0.2.14 stay as the fallback (Hawkeye started after logging in).
- A different character starts its totals and ranking over.
- **Fight history keeps 100 fights** (was 50; owner asked). Kept-forever fights are unchanged.
- **Updates found while Hawkeye is open:** it now checks every 30 minutes (was every 6 hours), and
  a newer release replaces one already downloaded but not installed yet (owner asked).
- **History > PvP > Select:** tick sessions (or Tick all) and delete them together, for clearing
  test sessions. The ✕ on each row still deletes one.

How it was found (recorded login, 2026-11-22, checked against the owner's screen): the game sends
your ranking line (0x8D57: rank, rank points, lifetime K/D/A as text, server, next update time) for
the overall and the class ranking, and the whole class top 500 (0x8D55) about 30 s after loading
in. Every row and the owner's own numbers matched (row 78 = him, 5,008,920 points, 1459 / 947 /
1730). The login inventory (0x5611) files AP under a key, not an item id; the AP change message
carries the same key right before the count, so the first AP change finds the login total.
Checked across the 13:28 update (recording to 13:56): the game does NOT resend the ranking by itself.
It comes at login and when you open the ranking window (confirmed: opening the PvP ranking sent the
class top 500 and both of your lines, with your new rank and points). So once the hourly update time passes, the
rank line dims and its hover says to open the PvP ranking in game to refresh it. Dungeon and boss rankings use the same messages and are skipped.
86 tests (9 new, made-up names).

## v0.2.15 (2026-11-21)

Added:
- Skill tracker: a tick box per tracked skill. Unticked skills keep their place, mode and icon
  position but show no icon and run no timers (for playing another character without deleting them).
  Older settings files load with every skill ticked. 77 tests.

Checked: the game does not send the AP or rank gained per kill (8 kills, 4 deaths searched within a
second: no gain amount in any message). It sends only new totals; the game's own "+X AP" is worked out
from the total it got at login. Next: record a login to read those totals.

## v0.2.14 (2026-11-21)

Fixed / added (owner, in PvP):
- **First kill after starting Hawkeye now counts.** Your AP and rank totals are remembered per character
  (`%AppData%\Hawkeye\pvp-totals.json`); the first change compares with them. A remembered total is only
  trusted for a jump one kill can explain (up to 12,000 AP, -1,000 to +25,000 rank); anything bigger was
  earned while Hawkeye was closed and is not counted.
- **Profile links for every kill:** a kill made before Hawkeye had seen the enemy server got no link
  (a player called Ullr). Earlier rows now get the server once it is learned; else the Enemy server
  setting is used.
- **Profile popup on stream:** streamer mode now mirrors it too (the page, without its header bar).
- **Side strip on a short meter:** buttons that do not fit fold behind a small chevron; clicking it
  grows the window to show them all, clicking again folds them.
- 76 tests.

## v0.2.13 (2026-11-21)

Added (PvP mode, owner's asks):
- **End session** button under the PvP numbers: saves the session (kills, deaths, assists, AP, rank
  points, and who each one was about) to the new **PvP** tab in History, then starts a fresh count.
  Saved in `%AppData%\Hawkeye\pvp-sessions.json`, kept until you delete a session.
- Kills, deaths and assists are clickable, live and in saved sessions: a PvP window lists who it was
  (time, class icon when they used a skill in view, name, legion for killers, combat power when known).
- **Profile** opens a player's shugo.gg page in a popup inside the overlay, with Open in browser.
  The game sends server ids, not names. Built in, confirmed on shugo.gg: 1304 = Kaisinel EU,
  2303 = Triniel EU (same ids for every player). For anyone else: Settings > PvP > Enemy server
  ("Triniel EU", "Siel NA East", ...); everyone in a PvP list is an enemy, so that one is enough.
- Death messages now keep the killer's server id; victims of your kills and assists get the enemy
  server learned from enemy killers.

Not yet: other players' combat power (their announcements are laid out differently; yours reads
right, it follows the gear you wear). 63 tests.

## v0.2.12 (2026-11-21)

Fixed (PvP mode, from the owner's live session):
- Mob kills no longer count as kills. Abyss mobs name you as killer and give AP too; a kill now needs
  a victim the game announced as a player. In the session: 39 player kills, 35 mobs, cleanly split.
- Assists: an AP rise now belongs to the latest kill just before it (the game sends AP about 0.1 s
  after the death). The old rule hid an assist that came 0.4 s after the owner's own kill.
- Replay of 19:41 to 19:49: 13 kills, 4 deaths, 2 assists, 19 mob kills ignored.

Known gap: the first AP and rank change after starting Hawkeye mid-session is not counted (the game
sends totals only when they change). Your rank total is also in the 0x3633 "you" message, sent at every
respawn; not used yet until checked on another character.

## v0.2.11 (2026-11-21)

Fixed:
- 0.2.9 and 0.2.10 started with no meter window, every time (I15): the new PvP tracker had two
  constructors and the app could not pick one. Now one, with a test that builds it the way the app
  does. Checked by starting the packaged exe before release.

## v0.2.10 (2026-11-21)

- Added a 3 s wait before an updated copy restarts, against a suspected WebView2 timing race. That
  diagnosis was wrong (I14); the real fault was I15. The wait is harmless and stays.

## v0.2.9 (2026-11-21)

Added:
- **PvP mode** (crossed swords on the left strip): kills, deaths and assists, AP gained and rank points
  gained since you switched it on; the bin starts the count again. Shows on stream too.
  Found live with the owner in PvP calling out his numbers, and every number matched to the point:
  kills and deaths come from the death message (0x8D04 carries victim and killer), AP is an item
  count (0x561B, item 0x1AC7C1), rank points a total (0xE273). Two kills (+5600/+2200 RP,
  +3780/+2100 AP), three deaths (-280 RP each) and a +8800 RP / +5250 AP kill all replayed exactly.
- Assists are inferred: an enemy killed by someone else and your AP rising within 3 s. Not yet
  confirmed against a called-out assist.
- 4 new tests (57 total), built from the session's real messages with names replaced.

## v0.2.8 (2026-11-21)

- After the updater installs a new version, the bottom line says "Updated to x.y.z" for a minute.
  (Released together with 0.2.7 as the owner's first end-to-end update test: 0.2.7 -> 0.2.8.)

## v0.2.7 (2026-11-21)

Added:
- **Clear button** (third button on the left strip): closes any fight in progress (saved to History
  first) and blanks the meter until your next boss or dummy fight.
- **Auto-update from GitHub.** At start and every 6 hours Hawkeye checks the latest release; if it is
  newer it downloads the zip that matches this copy (full or small) and unpacks it in
  %LocalAppData%\Hawkeye\updates. "Update x.y.z ready" then shows on the bottom line: click it to
  restart into the new version, or close Hawkeye and it installs on exit. Settings, History and Odyle
  (in %AppData%) are never touched. Switch: Settings > Auto-update. Off in Debug builds.
- 8 new tests (53 total), including one that runs the real install script against temporary folders.

Fixed before release:
- The install script's "wait for Hawkeye to exit" used plain `find`, which resolved to Git's find.exe
  in testing, so it copied without waiting. All Windows tools are now called by their System32 path
  (I13).

## v0.2.6 (2026-11-21)

First public release on GitHub (Releases page): a full zip with .NET included and a small one.
- Fix: Odyle list could freeze (deadlock between saving and storing a reading); found when the
  test run hung before any release.
- README and the zip's READ ME FIRST rewritten in plain words; the read-me now lives in
  `packaging/` and every package uses it.

## v0.2.5 (2026-11-21)

- **Odyle refills counted automatically.** The game adds +15 to current Odyle (up to 840) on a
  global timer every 3 hours (anchor 16:00 UTC, from the owner's countdowns; confirmed by AltB
  250 -> 265 across the 16:00 refill). Extras now adds those refills to each character since its
  last reading, shown as "~265" (dimmed) until a login confirms it; verified numbers show plain.
  "next +15 in ..." at the top of the list.
- Extras note shortened to: "Log in with each alt to get its latest verified Odyle. Current values
  may not be fully accurate."
- 3 new tests (45 total).
- **Fix: a fight could be lost from History when closing Hawkeye.** History and the Odyle list are
  written in the background; Hawkeye now waits for those writes before exiting. Found by GitHub's
  test run (slower machine: 23 of 50 history files written when the test looked).

## v0.2.4 (2026-11-21)

- **Monolith tighter** (owner: "taking a bit too much space ... the goal is minimalism"): rows
  21 px (were 26), thinner well, tighter header and bottom line; about a fifth shorter.
- **Monolith bars:** the hairline under each name is gone; each row now has a soft tone-on-tone
  band that grows with damage, yours a faint matte cyan with a soft edge.

## v0.2.3 (2026-11-21)

- **Monolith made matte and layered** (owner: "more matte ... layering to make it more human", with
  the Korathia combat-card lessons as the reference): warm charcoal with a faint grain instead of
  cold glass, a soft light from the logo corner, the rows sunk into a recessed well (a frame within
  the frame), a soft rim and fading halo instead of hard hairline boxes, warm whites, a matte cyan,
  and your line glowing gently instead of neon. Settings, History, Extras and the breakdown share
  the same surface in Monolith.
- **Class icons in Extras:** each character's class is filled in as soon as Hawkeye knows it.

## v0.2.2 (2026-11-21)

- Timer a little smaller in every look and never italic (a fight from history is just dimmed).
- Graphite & Vermilion: total damage brighter still.

## v0.2.1 (2026-11-21)

- Total damage easier to read in all three looks (was too faint in Monolith).
- Class icons keep their colours in every look (Monolith and Graphite had greyed them).

## v0.2.0 (2026-11-21)

New look, chosen by the owner from two rounds of mock-ups (11 designs):
- **Three looks, switchable in Settings > Look:**
  - **Monolith** (default): a black glass slab, bars reduced to a hairline under each name (yours
    glows cyan), big thin numerals; the breakdown opens with one huge DPS figure.
  - **Aurora**: deep glass, bars fading in to a glowing edge, a slow light drifting along the top.
  - **Graphite & Vermilion**: charcoal, hairline rules, a vermilion accent, serif numerals.
  The look covers the meter, the breakdown, Settings, History and Extras, and the stream page.
- **Death marker** is a plain skull (no crossbones).
- **Version** now sits at the end of the bottom line, always inside the frame.
- **RAM** shown as a percentage (exact GB on hover).
- Built so the meter and the stream share one stylesheet (`wwwroot/css/meter.css`); the old
  "Classic" style is gone.

## v0.1.12 (2026-11-21)

Added:
- **Odyle follows spending live.** Looting a cube (40 single, 80 double) makes the game send a
  one-counter update (`0x610C`); Hawkeye now reads it, so the Extras list stays right while you
  play, not only at login. Proven on the owner's double loot: cleric 760 (+40) became 680 (+40),
  exactly what the update carried. Other counters use the same update, so it only counts when the
  entry carries Odyle's own key (learned from the login message).
- 4 new tests (42 total) with the real update.

## v0.1.11 (2026-11-21)

Changed (owner: "more stylish and elegant"):
- **Boss name** in Palatino small caps, soft shadow instead of the hard outline (option A of three
  shown; B Constantia and C thin spaced capitals are one CSS block away).
- **DPS** calmer: no backing box, no heavy outline, medium weight, smaller "/s"; pale mint on your
  own green bar so it stays readable. Same on the stream page.

## v0.1.10 (2026-11-21)

Changed (owner: "buttons a bit cramped at the heading"):
- **History and Extras** moved out of the header into a slim vertical strip down the left side,
  under the logo; the version label sits just under the logo. Header: boss name, timer, Settings,
  Close (and START/STOP in dummy Start/Stop mode).
- **Boss name** bigger (15 px, bold) in blood red, one line ("..." if too long; full name on hover).
- **Total damage** set apart from the DPS.
- **DPS** on a dark backing with a heavy black outline, readable on the green "you" bar.
- Same look on the stream page.

## v0.1.9 (2026-11-21)

Fixed:
- **History and Extras never showed on stream; closed windows kept running (I12).** Closing a
  Hawkeye window left the page inside it running (closed analysis windows refreshed themselves
  every second into nothing: about 2,000 logged errors). A window closing while its picture was
  being taken stalled streamer mode's picture loop for good, so windows opened later never reached
  the stream. Pages now shut down with their window, and each picture has a 1 s limit. Proven in a
  test harness (0 errors after closing).

## v0.1.8 (2026-11-21)

Fixed:
- **Stream showed none of Hawkeye's windows, and still no breakdown after updating.** Two causes:
  OBS keeps the page it first loaded, so after an update it kept running the old stream page; and
  only the combat analysis had ever been rebuilt for the stream. Now:
  - the stream page reloads itself whenever Hawkeye restarts or updates (a per-run id in `/state`);
  - every open Hawkeye window (Settings, History, Extras, combat analysis) is shown on stream as a
    live picture taken about 4 times a second, exactly as on screen, scroll position included.
    Replaces the hand-built analysis panel. Switch: "Hawkeye windows on stream" (on).
  - Layering as on screen: windows above the meter, skill icons above everything.
- Window pictures proven with an off-screen test window; not yet seen in OBS.

## v0.1.7 (2026-11-21)

Added:
- **Odyle on every character.** Hawkeye reads Odyle from the game when you log into or switch to a
  character (message `0x610B`, found and confirmed on four of the owner's characters: 760 (+40),
  250 (+345), 480 (+295), 60 (+360), all exact) and remembers it per character in
  `%AppData%\Hawkeye\characters.json`.
- **Extras window** (new four-squares button on the meter): each character's Odyle as
  "current (+pool) / 840" with a bar and "verified ... ago", the one you are on highlighted.
- Not possible: showing it on the game's Change Character window itself; opening that window sends
  nothing over the network, so Hawkeye cannot know it is open.
- Next: estimate alts' pool between logins from the global refill timer once its interval is known.
- 5 new tests (37 total) using the real messages.

## v0.1.6 (2026-11-21)

Added:
- **Streamer mode** (Settings > Streamer mode, off by default). Hawkeye serves a page at
  `http://localhost:7777/` (this PC only) for an OBS **Browser Source**: the meter and the skill
  icons, drawn where they are on your screen, in the same style, with real transparency, live
  countdowns and icons that come and go with fights. One source instead of a Window Capture per
  window, and no black corners. Settings shows the link (Copy button), whether it is running, the
  three OBS steps, and switches for meter / icons on stream. Port in settings.json
  (`StreamerPort`) if 7777 is taken.
- **Combat analysis on stream** (v0.1.6, owner request): while the breakdown window is open on your
  screen, the stream shows it too, same place and look, live; it goes when you close it. Switch
  "Breakdown on stream" (on).
- The stream page was checked in a headless browser against test data (transparent background,
  meter, cooldown sweep, buff up/down, ending colours). Not yet seen inside OBS.

## v0.1.5 (2026-11-21)

Added:
- **Combat analysis window.** Click any player on the meter. Laid out like the game's own Combat
  Analysis: battle time, DPS, total damage; hits, critical, perfect, double, multi-hit, front,
  back, parry (count and %), share of the fight, deaths; then every skill with HIT, CRI, PERF,
  DOUB, FRNT, BACK, MULT, PARR, DPS, AVG, MAX and total damage (%), with a bar behind the name.
  Tabs switch between the players in the fight. A running fight refreshes every second.
  Not shown, because Hawkeye cannot read them yet: accuracy, block, invincible, endurance,
  regeneration, heal.
- **History.** The last 50 fights you took part in are saved when they end (idle, new pull, STOP on
  a dummy run, or closing Hawkeye), with their full breakdown, in `%AppData%\Hawkeye\history\`.
  The clock button on the meter opens the list; click a fight to open its analysis. The star keeps a
  fight forever (`history\saved\`, never deleted by Hawkeye), for comparing a great run later.

Changed:
- **Someone else's fight never replaces yours.** Once Hawkeye knows who you are, the meter only
  switches to fights you are in. Between fights it shows your last fight from history (timer in
  grey italics). Fixes "someone else started on the dummy and my number got overwritten".
- Clicking a row opens its breakdown; holding and moving still drags the meter.
- 5 new tests (31 total).

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
- **Version shown** small beside the logo on the meter, and in the Settings title and footer (read
  from the build, so it is always right).
- **Own GitHub repo:** https://github.com/simbariya/hawkeye (public, standalone; branch `main`).

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
