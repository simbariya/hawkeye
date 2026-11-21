# Hawkeye

A small DPS meter for Aion 2, made by Simba ([twitch.tv/drake22x](https://twitch.tv/drake22x)).

I wanted a meter that stays out of the way. One compact window with everyone's DPS, your ping and FPS
underneath, and your own skill timers sitting wherever you're actually looking instead of down at the
bottom of the screen.

A few other things ended up in there too:

- click anyone on the meter to see their damage broken down skill by skill
- your last 50 boss fights are kept, and you can star the good ones to keep forever
- Odyle for all your characters in one list
- a training dummy mode, timed (1, 2 or 3 minutes) or start/stop
- a streamer mode that puts the meter and your timers into OBS as one browser source

There are three looks to pick from in settings. Monolith is the default.

## Setup

1. Install [Npcap](https://npcap.com) and tick "Install Npcap in WinPcap API-compatible Mode" during setup.
2. Download the latest zip from [Releases](https://github.com/simbariya/hawkeye/releases) and unzip it
   somewhere you can write to (not Program Files).
   The `win-x64` zip has everything in it. The `small` zip is a fraction of the size but needs the free
   [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) installed first.
3. Run Hawkeye.exe and log into the game.

FPS only shows when Hawkeye runs as admin. Click "FPS: run as admin" on the meter and it restarts itself.

Hawkeye only listens to network traffic through Npcap. It doesn't touch the game's memory and never sends
input to the game, but any third-party tool with an online game is at your own risk.

## Building it yourself

You'll need the .NET 10 SDK.

```
dotnet build Hawkeye.slnx
dotnet test --project Hawkeye.Tests/Hawkeye.Tests.csproj
```

## Credits

The network capture and packet decoding started out as [RATmeter](https://github.com/Kuroukihime/AIon2-Dps-Meter)
by Kuroukihime. Like RATmeter, Hawkeye is licensed under GPL-3.0 (see LICENSE).
