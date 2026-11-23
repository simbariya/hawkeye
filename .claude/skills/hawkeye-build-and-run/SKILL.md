---
name: hawkeye-build-and-run
description: >
  The Hawkeye dev environment and every command: build, test, run, run as administrator, where
  settings and logs live, publishing a single exe for the owner, git and the public fork, and how
  to stop a test instance safely. Load when setting up, building, running, packaging, or committing.
  Does NOT cover what to verify (hawkeye-validation-and-qa) or whether a change is allowed
  (hawkeye-change-control).
---

# Hawkeye Build and Run

As of v0.1.2, 2026-11-21.

## 1. Environment

- Windows 11, .NET 10 SDK 10.0.404 at `C:\Program Files\dotnet` (installed with
  `winget install Microsoft.DotNet.SDK.10`). Use the full path in Bash if `dotnet` is not on PATH:
  `"/c/Program Files/dotnet/dotnet"`.
- Npcap installed (WinPcap API-compatible Mode). WebView2 runtime (ships with Windows 11).
- Repo: `C:\Users\Bison\Documents\Aion 2 DPS meter\my-meter`, branch `main` (was `skill-tracker` until 2026-11-21).
  Remotes: `origin` = `https://github.com/simbariya/hawkeye` (PUBLIC, standalone, branch `main`),
  `fork` = `https://github.com/simbariya/AIon2-Dps-Meter` (the old GitHub fork, no longer pushed),
  `upstream` = `https://github.com/Kuroukihime/AIon2-Dps-Meter` (RATmeter).
- The owner's previous meter (reference only, do not modify):
  `C:\Users\Bison\Documents\Aion 2 DPS meter\Aion2DpsMeter-v1.11.3.320-win-x64\`.

## 2. Commands

```
dotnet build Hawkeye.slnx
dotnet test --project Hawkeye.Tests/Hawkeye.Tests.csproj
Hawkeye.App\bin\Debug\net10.0-windows10.0.20348.0\Hawkeye.exe
```

- Run as administrator for FPS: right-click `Hawkeye.exe` > Run as administrator.
- Stop a test instance: `Get-Process Hawkeye | Stop-Process -Force`. Never stop other processes by
  name (the game `AION2.exe` and the owner's tools may be running).
- A clean build has no package warnings (the old `NU1903` SQLite advisory went with the history
  removal, checked 2026-11-20). The few remaining compiler warnings are in inherited engine code.

## 3. Where things live at runtime

| What | Where |
|---|---|
| Settings | `%AppData%\Hawkeye\settings.json` |
| Logs | `%AppData%\Hawkeye\logs\hawkeye-YYYYMMDD.txt` (7 kept) |
| Skill icon cache | `IconCache\Skills\` next to the exe |
| Packet recordings | `%AppData%\Hawkeye\PacketLogs\` (only when `RecordPackets` is true) |

## 4. Packaging a zip to share (proven 2026-11-21, v0.1.2)

Output goes OUTSIDE the repo, to `C:\Users\Bison\Documents\Aion 2 DPS meter\release\`.

```
dotnet publish Hawkeye.App/Hawkeye.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -o "<release>\Hawkeye"
```

Then, in `<release>\Hawkeye`:
1. Delete the three `Microsoft.Web.WebView2.*.xml` files (developer docs from the package).
2. Copy the repo `LICENSE` in as `LICENSE.txt` (GPL: every copy handed out carries it).
3. Keep `READ ME FIRST.txt` (setup for a non-technical friend: Npcap, SmartScreen, admin for FPS,
   borderless mode, where the logs are). Update its version line.
4. **Start the packaged `Hawkeye.exe` once before publishing (mandatory since I15).** Wait about 10 s,
   then check its new log file: no "Unhandled UI error" lines. 0.2.9 and 0.2.10 shipped without a
   window because nobody started the built exe; all tests were green. Then delete the `IconCache`
   folder it created and the `Hawkeye.exe.WebView2` folder.
5. Zip the `Hawkeye` folder as `Hawkeye-<version>-win-x64.zip` (about 70 MB; 172 MB unzipped,
   because .NET is bundled so the friend installs nothing but Npcap).

Result: `Hawkeye.exe`, `GameData\`, `wwwroot\`, `Hawkeye.staticwebassets.endpoints.json`,
`LICENSE.txt`, `READ ME FIRST.txt`. Verified by launching it beside the owner's running copy: it
started, locked onto the game traffic and synchronized. Note: a second instance logs to
`hawkeye-YYYYMMDD_001.txt` because the first holds the day's log open.

**Small zip for Discord (20 MB limit), proven 2026-11-21:** same command with
`--self-contained false` (drop `IncludeNativeLibrariesForSelfExtract`), output
`<release>\Hawkeye-small\Hawkeye`, same steps 1 to 5, and the READ ME gets a step to install the
free ".NET 10 Desktop Runtime x64" (https://dotnet.microsoft.com/download/dotnet/10.0). Zip is
11.8 MB. Without the runtime, Windows shows its own "install .NET" message with the link.

**Always publish into a NEW folder** (e.g. `<release>\<version>-share`), never one that already
exists: the owner often runs Hawkeye straight from a release folder. On 2026-11-21 a publish into
the running `0.1.6` folder failed on the locked exe, and the zip step that followed overwrote the
good zip with a broken 2.7 MB one. Check the zip size (about 12 MB) before handing it over.

**Testing a package while another Hawkeye runs:** every copy shares the web view data folder
`%LocalAppData%\Hawkeye.WebView2`; two DIFFERENT builds running at once makes the second fail with
COMException 0x8007139F (window stays blank, logged as "Unhandled UI error"). Launch the test with
`WEBVIEW2_USER_DATA_FOLDER` set to a scratch folder. The test window opens exactly over the owner's
meter (shared settings), so warn the owner first; they may click it.

**Keep only the latest (owner, 2026-11-23: "clean up the directory so it takes no space, delete old versions,
clean up GitHub so just the latest release is available").** The release folder was emptied down to
`0.2.21-github\full\Hawkeye` (the copy the owner runs) and GitHub down to the one latest release. So:
- take `READ ME FIRST.txt` from the repo's `packaging\` (replace `{VERSION}`) and `LICENSE` from the repo
  (as `LICENSE.txt`), not from a previous release folder;
- after a release is verified, delete the previous release folder, except the one Hawkeye is running from
  (check `Get-Process Hawkeye` paths first), and delete the new zips and the `small` folder locally (they are
  on GitHub);
- the owner's install updates IN PLACE (the updater copies the new files over the running folder), so his
  folder keeps its old name (2026-11-23: he runs `release\0.2.21-github\full\Hawkeye`, which becomes 0.2.22 on
  update). Never delete the folder Hawkeye runs from; the freshly published `full` folder is only a spare once
  his install has updated, and can go then;
- delete the previous GitHub release and its tag: `gh release delete vX -R simbariya/hawkeye --cleanup-tag -y`.
  Auto-update only reads `releases/latest`. Download counts go with the deleted releases.

**Release command: always pass `-R simbariya/hawkeye`.** Without it `gh` picked the old fork's upstream
(2026-11-22: "Failed to create release, workflow scope may be required", and `gh release list` showed
RATmeter's v1.11.x releases). Verify after: `gh api repos/simbariya/hawkeye/releases/latest`.

**GPL:** anyone given the zip must be able to get the source. The README points to the public fork,
so the fork must hold the matching code (push, on the owner's word) before the zip is shared widely.
Not code-signed: SmartScreen warns on first run ("More info" > "Run anyway").

## 5. Git

- Commit identity (repo-local config, given by the owner 2026-11-21): name `Simba`, email as set in
  `git config user.email`. It is public in the history once pushed.
- Commit per batch; message says what changed and why; end with the session's attribution lines.
- Before ANY push: `git diff --cached` scanned for names, emails, keys, captures, settings, logs,
  screenshots. The fork is public. Push only on the owner's explicit word.

## CHANGES

- 2026-11-20: written for v0.1.0.
- 2026-11-21: section 4 rewritten with the proven zip recipe; git identity set.
