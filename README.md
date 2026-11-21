# Hawkeye

Made by **Simba** · [twitch.tv/drake22x](https://twitch.tv/drake22x) · Support: *Ko-fi link coming soon*

A damage meter overlay for Aion 2 (global servers): a small always-on-top meter with exact DPS per
player, plus draggable skill and buff timer icons you can put wherever your eyes already are.

Hawkeye reads the game's network traffic with Npcap. It never reads or changes the game's memory
and never sends input to the game. Using any third-party tool with an online game is at your own
risk under that game's terms.

## What it shows

- **Meter**: one bar per player, exact DPS ("137,985/s"), fight timer, you highlighted in teal.
- **Status line** (each part can be switched off): ping, game FPS, system CPU and RAM.
- **Skill tracker**: up to 10 skills, each its own icon. Per skill, follow either its **cooldown**
  (dark sweep and countdown until ready) or its **buff** (countdown while up, red pulsing frame when
  it runs out so you know to reapply). Open Settings to drag the icons anywhere.

## Install and run

1. Install [Npcap](https://npcap.com) with **"Install Npcap in WinPcap API-compatible Mode"** ticked.
2. Unzip `Hawkeye-<version>-win-x64.zip` somewhere you can write to (not Program Files). No .NET
   install needed; it is bundled.
3. Run `Hawkeye.exe`, then start or log into the game. It finds the game traffic by itself.
4. Optional: click "FPS: run as admin" on the meter to see the game's FPS.

Settings are saved in `%AppData%\Hawkeye\settings.json`, logs in `%AppData%\Hawkeye\logs`.

## Build from source

Needs the .NET 10 SDK on Windows.

```
dotnet build Hawkeye.slnx
dotnet test --project Hawkeye.Tests/Hawkeye.Tests.csproj
```

The app is `Hawkeye.App` (outputs `Hawkeye.exe`), the packet engine is `Hawkeye.Engine`, game data
is `Hawkeye.Core`.

## Credits and licence

Hawkeye is based on [RATmeter](https://github.com/Kuroukihime/AIon2-Dps-Meter) by Kuroukihime,
whose packet capture and decoding engine it builds on. Like RATmeter, Hawkeye is licensed under the
GNU General Public License v3.0; see `LICENSE`.
