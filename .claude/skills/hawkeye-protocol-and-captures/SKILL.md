---
name: hawkeye-protocol-and-captures
description: >
  Aion 2 network message reference for Hawkeye: framing, compression, the opcode table, the known
  byte layouts (damage, damage over time, buffs, cooldowns, casts, server time), what is still
  unknown, and how to record a fight and replay it offline to test a decoder. Load before touching
  anything in Hawkeye.Engine/PacketProcessing, when a number on the meter looks wrong, when adding a
  new decoded field, or when the owner asks "can it show X". Does NOT cover the app's windows
  (hawkeye-architecture-contract) or general debugging (hawkeye-debugging-playbook).
---

# Hawkeye Protocol and Captures

As of v0.1.0, 2026-11-20. All of this was reverse-engineered by RATmeter's author; Hawkeye inherited
it. Everything marked "unknown" in the code is genuinely unknown. Verify, never assume.

## 1. Transport

- Game server to client over TCP. The capture locks on the adapter and SOURCE port where the
  heartbeat bytes `0E 00 36` appear. The game is on 13328; locks on 443 and 80 in the owner's logs were
  web traffic holding the 3 bytes by chance (never read a message), so since v0.2.24 a lock must read a
  real keep-alive (the 11-byte `0E 00 36 ...` message, reached in step, not the one it synced on) within 45 s.
  NOTE: the log line "SYNCHRONIZED on pattern 06-00-36" prints the wrong bytes; the code
  (`CaptureDevice.HeartbeatPattern`, `PacketAccumulator`) uses `0E 00 36`.
- Only server-to-client traffic is captured (`tcp src port N`). Hawkeye sends nothing.
- No encryption seen. Bundles are LZ4-compressed (section 2).

## 2. Framing

- Each message: `varint length`, then `u16 opcode` (little-endian), then the body.
- The length is odd: the message spans `length + varintLength - 4` bytes from its first byte
  (`PacketProcessor.ScanFrames`). Do not "fix" the `- 4`; it is what the game does.
- Varints are 7 bits per byte, low first, high bit = more (`PacketReader.ReadVarInt`, max 5 bytes).
- Opcode `FF FF` = compressed bundle: optional flag byte `Fx` (not `FF`), then `FF FF`, then `u32`
  uncompressed size, then LZ4 block. Inside are more messages, possibly more bundles.
- `00` padding bytes between messages are skipped.

## 3. Opcode table (`Routing/PacketOpcodes.cs`)

| Opcode | Name | Hawkeye uses it for |
|---|---|---|
| `0x3804` | Damage | Every hit: meter rows |
| `0x3805` | DotDamage | Damage over time ticks |
| `0x3802` | SkillCast | Your casts: starts a cooldown timer |
| `0x3847` | SkillCdUpdate | Cooldown list from the server: cooldown timers |
| `0x382A`, `0x382B` | BuffEffectA/B | Buffs and debuffs: buff timers |
| `0x3603` | ServerTime | Ping on the status line |
| `0x3633`, `0x3645` | PlayerInfo, OtherPlayersInfo | Names, classes, who is you |
| `0x3620` | GlobalSessIdLinking | Ties ids together |
| `0x3641` | MobSummon | Summons and pets to owners |
| `0x3649` | PlayerStats | Your stats (unused in the UI since the calculator was removed) |
| `0x8D00` | RemainHp | Target HP |
| `0x8D04` | EntityDeath | Deaths |
| `0x9702` | PartyInfo | Party members |
| `0x5100` | SkillList | Your skills with their levels (v0.2.34) |

## 4. Known layouts (after length + opcode)

**Damage `0x3804`** (`DamagePacketProcessor.Parse`): `varint target`, `varint mask` (low 4 bits =
switch 4..7, `0x20` = has multi-hit list), `varint ?`, `varint actor`, `u32 skillCode`, `u8 ?`,
`varint damageType` (3 = critical), special block (switch 4: 8 bytes; else `u8 flags` (1 back,
2 parry, 4 perfect, 8 double), `u8 ?`, `u8 direction` (1 back, 2 front), then 8 bytes), `varint ?`,
`varint damage`, (switch 4: one more varint), (mask `0x20`: `varint count` then that many varint
hit amounts).
Open problem: ~80,000 of these failed to parse in 17 days of the old meter's logs ("Need N bytes
but only N remain"). Suspect the special block's length or the switch rules. Every failure is a
lost hit. First step: capture a dummy fight and count failures against hits seen in game.

**Cooldowns `0x3847`**: `u8 count`, then per entry `u32 skillId`, `varint msLeft`. `msLeft = 0`
clears the timer (reset).

**Buff `0x382A` (new) / `0x382B` (refreshed)**, checked on every buff message in the recordings 2026-11-26:
`varint entity` (who has it), `u8 ?` (0x382A only), `u8 type` (17 or 19), `varint instance` (0x382C removals name
it), `u32 buffId*10` (divide by 10), `u32 durationMs`, 4 zero bytes, `u64 time` (ms since 1970), `varint caster`.
Until v0.2.38 Hawkeye read the caster 8 bytes early (inside the time: always "161") and the Theostone path one
byte late. With the caster right: your own buffs on you are cast by you; enemies' debuffs on you by them.

**No-damage hits**: a debuff Theostone's proc is a 0x3804 whose mask low bits are 0 (all 96 in the recordings;
damage stones 4 or 6). Read up to the skill code like a damage hit; no damage follows in the same layout.

**Potions**: drinking one is a 0x3802 cast with cooldown field 0 (the varint before it is 10000 for every
potion, not the cooldown). 0x3847 never lists potions. Healing Potion 2020101 / Superior 2020102 (cleanse; shortest
gaps between uses 10.7 to 11.1 s) and Life Potion 2011101 / Superior 2011102 (10 s heal over time; 29.9 to 30.1 s).
Item use also sends 0x5613 (healing) / 0x5616 (life) `[u32 item][u32 key]` and an item count 0x561B: no cooldown.

**Cast `0x3802`**: `varint source`, `u8 mask`, `u32 skillId`, `u8 ?`, optional fields by mask,
target block, `varint ?`, `u8 ?`, optional varint, `varint cooldownMs`, optional autoload fields.

**Skill ids**: packets carry variant ids (`14010001`, `14010030`...). `SkillRepository.NormalizeSkillCode`
maps them to the base id the picker shows (`14010000` Deadshot). Ranger ids start `14`.

### Counters `0x610B` (Odyle), found 2026-11-21

Sent when you log in or switch character, about 4 s BEFORE `0x3633` names you. A list of entries
`[flags][entry no.][key varint][value varint if flags&0x04][extra varint if flags&0x08]`; the list
does not start at a fixed offset. **Entry 1 = Odyle**: value = current, extra = the "(+N)" refill
pool. Recognised by entry 2 following with the same key. Confirmed on four characters (fixtures in
`Hawkeye.Tests/Fixtures/counters-*.hex`). Caps are NOT sent: 840 current, 2000 pool, fixed.
The pool refill (+15) runs on a GLOBAL timer (same for every character); the next tick on
2026-11-21 was about 16:00 local; the interval is not confirmed yet. The character list (`0x3626`,
~10 KB, all names) arrives at the switch too but holds no Odyle. Opening the Change Character
window sends nothing.

**Counter update `0x610C`** (proven 2026-11-21, a double cube loot): `[0x00][one entry, same layout
as 0x610B][0x03]`, sent the moment a counter changes. Odyle = entry 1 WITH Odyle's key (51591 on all
the owner's characters; another counter's update also had entry 1 but key 0). Real example:
`100C61000C01879303A8052803` = 680 (+40).

How it was found: a side recorder saved raw TCP from the first byte of each connection (Hawkeye's
own reader only starts at the first heartbeat, so it misses logins), then the owner read out each
character's numbers and the stream was searched for them.

### PvP messages (found 2026-11-21, owner in live PvP calling out numbers)

- **Death 0x8D04:** `[victim varint][u32 ?][killer varint][u16 killer server id][killer name: len + UTF-8][legion: len + UTF-8]...`;
  global server ids seen: 1304 (owner's side), 2303 (enemies); not in `ServerMap` (Korean ids).
  Player announcements (0x3645) do NOT carry `[server][len][name]` like this (checked 11,579 of them).
  killer 0 and no names for non-player kills. Names can be non-ASCII (UTF-8).
- **Item count 0x561B** (44 bytes): the item's own number u32 at 6 (differs per character: AP is 0x1AC7C1 on
  the owner's main, 0x08E6C5 on his Chanter), the item type's key u32 at 14, new count u32 at 18. AP = key
  0x37703323 (since v0.2.25; the number at 6 missed all AP on the Chanter). The
  same message counts arrows going down while a Ranger shoots.
- **Rank points 0xE273** (12 bytes): total u32 at 4. Sent only on change.
- **Rank total in 0x3633** (candidate, one character only): u32 right after the 4 bytes `26 F4 CB 1F`,
  about 170 bytes from the end; resent at every respawn and zone change. Check on an alt before use.
- **Mob or player:** Abyss mobs killed by you carry your name as killer and give AP. Tell them apart by
  whether the victim was announced by 0x3645 (39 players / 35 mobs in the 2026-11-21 session, no overlap).
- **Your combat power 0x3656** (19 bytes): u64 current at 3, u64 highest at 11 (owner: 234,776 in the PvP
  set, 239,918 highest; current follows the gear you wear). Seen at a zone load, 2026-11-21. Read since v0.2.35.
- **Group members' combat power is in the group list 0x9702** (found 2026-11-26): `PartyProcessor`'s layout
  (inherited) reads each member's u64 `_combat_power`; on 114 real lists the owner's own entry equalled his 0x3656
  current value. Only your group: nobody else's CP was found in any message.
- **Name card 0x4106:** `[entity varint][u8 flag][u16 server id][name: len + UTF-8]` then, when flag is 12,
  more bytes and `[u16 server][legion: len + UTF-8]`. 417 cards in a home area, all server 1304.
- **Target's combat power: NOT FOUND yet** (2026-11-21, owner targeted players for ~3 min, 1.3M messages,
  no ground-truth numbers). Questlog shows it on target; it may come from the official character site
  rather than game traffic. Next try: one target's exact CP and gear score from the owner, then search.
- **Per-kill gains are NOT sent** (2026-11-21: 8 kills and 4 deaths, every message within 1 s searched for
  the AP and rank gained as u16/u32/u64/varint/signed: nothing). Only totals (0x561B, 0xE273). The login
  data probably holds the totals: record a login to find them.
- **Login totals (2026-11-22, recorded login, owner's screen as ground truth):**
  - **Inventory 0x5611** (26 KB at login): records `[..][u32 key][u64 count]..`, no item ids. AP's key
    (0x37703323 on the owner's main, same both days) is also in every AP item-count message at offset 14,
    right before the count. Login AP 362,815 found this way.
  - **Your ranking line 0x8D57:** `[len][op][00 00][u8 type][u8 category][row][u32 ?][u64 next update]`.
    type 1 = PvP (0x15 dungeon, 0x03 boss rankings use the same message with other text); category 0 =
    overall (owner 342), 4 = Ranger (owner 78). Sent ~7 s after loading in and again with the table.
  - **Ranking table 0x8D55** (59 KB): `[len][op][00 00][u8 type][u8 category][u16 ?][row x 500][u64 next
    update][u32 ?]`. Row: `[u32 rank][u32 ?][u16 0][u16 server id][name][legion][10 bytes][u8 class]
    [u64 rank points][len + {"kill_count":N,"death_count":N,"assist_count":N}][8 zero][u32 ?][u32 ?]`.
    Servers 1301 to 1323 in the EU Ranger table. Next update = ms since 1970 UTC (13:28 on the day: the
    hourly recalculation). NOT resent at the hourly update (recorded across 13:28 to 13:56, 2026-11-22:
    nothing). Sent at login and when the ranking window is opened (that window also sends type 0x15 / 0x03).
    Confirmed 14:13 the same day: opening the PvP ranking sent the class table plus both own lines
    (overall 343, Ranger 78, 5,007,520 points, deaths 948 after one death), next update 14:28.
    Owner, later that day: it is sent ONCE per hourly update; opening the page again in the same hour
    sends nothing (the client keeps it).
- **Theostone procs = damage hits (0x3804)** with a Theostone skill code (93 in skills.json, 30000171 to
  30012371; damage packets carry the 7-digit form, e.g. 3000018). Owner's PvP recording 2026-11-21: his own
  procced 10 times, enemies' hit him 16 times. The stones also appear in 0x3645 player announcements and
  some in 0x382B buffs (not used).
- **Theostone debuffs and buff removal (2026-11-22, owner's PvP recording):** a Theostone debuff on you is
  0x382A `[len][op][target varint][u8 1][u8 type 19][instance varint][u32 skill id x10][u32 ms][5 bytes]
  [u64 time ms][caster varint]...`. **0x382C** = buffs ending: `[len][op][entity varint][u8 count]` then per
  entry `[u8 reason][instance varint]...` (reason 0 ran out: 3 s debuff gone at 3.03 s; reason 7 cleansed:
  a 20 s one at 9.4 s, a 4 s one at 1.4 s, with 11 more bytes per entry). A death removed 6 at once.
- **Theostone kinds and ticks (2026-11-23):** skill id / 10 = 300 F G SS: F 0 damage / 1 debuff, G grade
  (0 Rare, 1 Epic, 2 Unique), SS slot (debuff 07 Blind, 08 unknown, 09 Seal, 10 Root, 11 Stun, 12 Paralyze,
  13 Move Slow, 14 Combat Slow, 15 Poison, 16 Bleed; damage 17-20 Fire/Water/Earth/Wind single, 21-24 area).
  A debuff stone sends a 0x3804 hit first; the 0x382A debuff only follows if it lands (3 Bleed and 1 Seal hits on
  the owner never did). Poison/Bleed ticks come as 0x3805 every ~2 s with the 7-digit code x100 (skill / 100).
  The owner's potion: 0x382C reason 7 naming the Poison's instance at 9.4 s of 20, ticks stopped.
- **Theostone crits and doubles (2026-11-23):** Theostone damage hits carry the usual damage type (3 = crit) and
  the double-damage flag: 67 of 147 hits in the owner's PvP recording were crits, 13 doubles, 7 both.
- **Mob types and dummy ids (2026-11-23):** a mob's code is only in its appear message 0x3641
  (`[len][op][entity varint][u16 mask][u8][u32 mob code]...`); no other message carries it (searched all
  recordings for the dummy codes). Training dummies keep their entity ids: all 7 in login-1122 and target-run had
  the same ids on 2026-11-21 and 2026-11-22. Dummy max HP is small (50,540 and 119,700), so HP cannot spot them.
- **AP is never resent unasked:** searched a 30-minute session; the AP total appears only in 0x561B.
- **Arena (2026-11-23, owner's recorded 1v1, won 3-0, 1500 to 1525; scratchpad arena-1123):** a player is an
  8-byte key `[u32 character id][u16 0][u16 server id]`. **0xE319 roster** as the match loads: per player
  `[u8 team][u32 rating]...[key][u8 name length][name][u32 ?][u8 1][u32 level]...` (team 22 bytes before the key,
  rating 21; found by the key + name shape). **0xE325 kill** `[killer key][victim key]`. **0xE326** round score
  per player (not used). **0xE328 result** `[u8][u32 new rating][u8][u8][your key]...`. **0xE32A standings**
  (every zone change) `[u8 count]` then 14 bytes per mode `[u8 mode][u32 rating][u32 ?][u8 wins][u8 losses]
  [u16 ?][u8 ?]` (mode 1: 1500/0/0 to 1525/1/0; the owner's mode 3: 1532/9/7; the last byte 4 there is unknown).
  **0xE343** (as you enter and leave the arena) `[u8 count]` then per mode `[your key][u8 mode][u32 0][u64 time]`:
  your key (used since v0.2.28 to find you in the roster). 0xE31C / E31B / E31D / E33x / E34x also come with
  arena loading, not decoded.
- **Server ids on global (corrected 2026-11-23, v0.2.32):** in the arena roster, each player has TWO server
  ids: one inside the key and one 13 bytes after the name (`[u32 ?][u8 faction 1/2][u32 level][u32 0][u16
  server]`). The one after the name is the CURRENT server (an opponent: key 2318, after-name 2302, shugo.gg
  profile Zikel with matching class); the key's is likely where the character started. The Korean `ServerMap`
  codes (id - 300) do NOT name global servers (2302 is "SIE" there, Zikel on EU), so v0.2.28 to v0.2.31 named
  servers wrongly. Checked on real profiles (name + class): 1302 Nezekan, 1304 Kaisinel, 1305 Yustiel, 2301
  Israphel, 2302 Zikel, 2303 Triniel, 2304 Lumiel, 2311 Ludra. Unknown: 1303, 1311 (game shows "TAH"), 1313 (not
  on 15 EU servers tried; nathara, hithanya, atiel, munin untried). shugo.gg profile pages also show combat
  power and gear score (e.g. "gear score 3,270, combat power 243,206"). shugo.gg has no working search URL and no full server list in its pages.
- **Defiance** (every class's crowd-control breaker; skill name "Defiance", ids per class such as 14260000 Ranger,
  15240000 Sorcerer, 18200000 Chanter): an enemy's use is a **0x3802 cast whose cooldown field is their real
  cooldown** (51,300 ms in the recording; others in the Abyss 36,900 and 28,700 ms), then 0x3804 self-hits, a
  0x382A 5 s buff on themselves and 0x382C removals (the CC it broke).

### Skill list `0x5100` (found 2026-11-25)

Your own character's skills, sent at login, at every zone change / teleport (just before 0x3633 names you), and
when you switch skill setups (together with a 0x5105). `[varint count]`, then per skill
`[u8 flags][u32 id][u8 level][u8 learned][u8 bonus][u8 ?][u8 bonus][u8 bonus][u8 ?]`
`(flags & 2: varint cooldown ms left)[u32 ?](flags & 4: u8)(flags & 8: varint)`. The level is the one the game shows
and always equals learned + the three bonuses outside the arena; in the arena every skill reads 20. Ids carry the
chosen specialisations (14012450 = Deadshot with 2, 4, 5); entries below 10,000,000 are not class skills. All 59
lists in the owner's recordings parse to the last byte (scratchpad harness `skillreplay`). Not yet seen: whether
changing gear with a skill-level bonus re-sends the list without a teleport.

## 5. Record a fight, replay it, test a decoder

1. Set `"RecordPackets": true` in `%AppData%\Hawkeye\settings.json`, start Hawkeye, fight.
   Raw stream (TCP payloads as received, LZ4 bundles still packed) lands in `%AppData%\Hawkeye\PacketLogs\` (since 2026-11-21; was next to the exe). Turn it off after. Captures NEVER go in git.
2. Write down what the game showed (damage numbers, cooldown seconds, buff durations).
3. Replay without the game: register `FilePacketCaptureDevice` (pointed at the file) instead of
   `CaptureDevice` in a harness or test.
4. Before running, WRITE the numbers you expect (project-discipline-and-methodology). A decoder
   change is accepted only if the replay matches the prediction and the in-game numbers.

## When NOT to use this skill

- Window, settings or threading questions: hawkeye-architecture-contract.
- "It shows nothing at all": hawkeye-debugging-playbook first (usually capture, not decoding).

## CHANGES

- 2026-11-20: written for v0.1.0 from the inherited decoder source and the owner's old meter logs.
- 2026-11-25: skill list 0x5100 (levels) added.
- 2026-11-26: group members' CP in 0x9702; 0x3656 read.
- 2026-11-26: buff message layout corrected (caster, 0x382B), no-damage Theostone procs, potions.
