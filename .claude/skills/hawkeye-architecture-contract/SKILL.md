---
name: hawkeye-architecture-contract
description: >
  How Hawkeye is put together: the data pipeline from network card to screen, the threading
  model, the settings contract, the window model (meter, settings, tracker icons, click-through,
  edit mode), and a file-by-file map. Load before editing any code, adding a setting, adding a
  window, or moving a file. Does NOT cover message byte layouts (hawkeye-protocol-and-captures),
  build commands (hawkeye-build-and-run), or change rules (hawkeye-change-control).
---

# Hawkeye Architecture Contract

As of v0.1.0, 2026-11-20. Root: `C:\Users\Bison\Documents\Aion 2 DPS meter\my-meter`.

## 1. Projects

| Project | Kind | Holds |
|---|---|---|
| `Hawkeye.Core` | net10.0 library | Game data (skills, classes, mobs JSON + repositories), models, skill icon cache |
| `Hawkeye.Engine` | net10.0 library | Capture, stream rebuild, decoders, combat sessions, timers, settings |
| `Hawkeye.App` | WPF + Blazor WebView exe, assembly `Hawkeye` | Windows, pages, system stats, overlay management |
| `Hawkeye.Tests` | xUnit v3 | Engine-level tests (no UI) |

The Engine was RATmeter's `AionDpsMeter.Services`, renamed. The App was rewritten on 2026-11-20.

## 2. The pipeline

```
Npcap -> CaptureDevice (adapter detection, port lock, TCP order)
      -> TcpStreamBuffer / PacketAccumulator (sync on heartbeat, split by varint length)
      -> PacketProcessor (LZ4 bundles "FF FF" unpacked)
      -> PacketDispatchService -> OpcodeProcessorRegistry -> one IOpcodeProcessor per opcode
      -> CombatSessionManager (fights, players, damage)      -> MeterViewModel -> MeterPage
                             -> TimedEventTracker x2 (Cooldowns, Buffs) -> TrackerIconWindow
                             -> PingUpdated event                       -> SystemStatsService
```

Processors are discovered by reflection: any class in the processors namespace with
`[PacketOpcode(...)]` is registered (`Engine/Extensions/ServiceCollectionExtensions.cs`).

## 3. Threading (the contract that keeps it from crashing)

- Packets are decoded on the capture's own processing thread. Everything the engine writes is
  guarded: `CombatSessionManager` uses `lockObject`; `TimedEventTracker` has its own lock.
- The UI never subscribes to per-packet events. It POLLS: `MeterViewModel` every 33 ms (on the
  Blazor renderer's context), each `TrackerIconWindow` every 50 ms (WPF `DispatcherTimer`),
  `SystemStatsService` every 1 s (thread-pool timer, plain field writes).
- `ISettingsService.Changed` can fire on any thread. Every handler that touches a window marshals to
  the UI thread (`Dispatcher.BeginInvoke` / `OverlayManager.RunOnUi`). Keep it that way.
- `OverlayManager` methods assume the UI thread (Blazor click handlers already run there).

## 4. Settings contract

- One object, `HawkeyeSettings` (`Engine/Settings/HawkeyeSettings.cs`), loaded once into
  `SettingsService.Current`, saved to `%AppData%\Hawkeye\settings.json`.
- Read `settings.Current.X` anywhere. Change ONLY with `settings.Update(s => ...)`, which saves
  (temp file then move) and raises `Changed`. Never mutate `Current` outside `Update`.
- New setting: add a property with a sensible default (old files simply lack it and get the
  default), use it, add it to `SYSTEMS - WHAT EACH DOES.md`. Engine-behaviour settings with no UI
  are allowed (they are listed at the bottom of the class).
- `TrackMode` serialises as a string ("Cooldown"/"Buff").

## 5. Window model

| Window | Class | Notes |
|---|---|---|
| Meter | `Views/MainWindow` hosting `Pages/MeterPage.razor` | The app's main window; closing it quits. Opacity from settings; bounds saved 1 s after moving |
| Settings | `Services/Windowing/BlazorWindow` hosting `Pages/SettingsPage.razor` | Owned by the meter, always on top. Its existence IS edit mode |
| Tracker icon | `Views/TrackerIconWindow` (plain WPF, no web view) | One per tracked skill; `ShowActivated=false`; click-through outside edit mode |

- **Edit mode** = the Settings window is open (`OverlayManager.IsEditMode`). Entering it: each icon
  drops click-through and is raised above every window (`BringToFront`, `SetWindowPos` with
  `HWND_TOPMOST` and no activation). Clicking into Settings raises the icons again. Closing Settings
  turns click-through back on. Why: hawkeye-failure-archaeology I1.
- **Click-through** = `WS_EX_TRANSPARENT | WS_EX_NOACTIVATE` toggled by `ClickThrough.Set`.
- **Dragging** = `Window.DragMove()` from a mouse-down (pages call `OverlayManager.TryDragMove`).
- Icons are kept in step with settings by `OverlayManager.SyncIcons` on every `Changed`: new
  tracked skill = new window, removed = window closed. Position is saved per skill on drag release.

## 6. Dependency injection (`App.xaml.cs`)

Singletons: `ISettingsService`, the engine chain, two keyed `ITimedEventTracker`
(`TimerKeys.Cooldowns`, `TimerKeys.Buffs`), `CombatSessionManager`, `SystemStatsService`,
`OverlayManager`, `MainWindow`. Pages get services with `@inject`. Startup order: host start ->
packet capture start (Npcap missing = message box and exit) -> meter shown -> icons synced.

## 7. File map (App)

```
Hawkeye.App/
  App.xaml(.cs)                     start-up, DI, logging, shutdown
  Views/MainWindow.xaml(.cs)        meter window
  Views/TrackerIconWindow.cs        one tracked skill icon (all its visual states)
  Pages/MeterPage.razor(.css)       meter page; ALL meter colours at the top of the css
  Pages/SettingsPage.razor          settings page
  Pages/Components/                 SettingRow, ToggleSwitch
  ViewModels/MeterViewModel.cs      rows, timer, target, status line
  Services/SystemStatsService.cs    ping, FPS (ETW/DXGI), CPU, RAM
  Services/Windowing/               OverlayManager, ClickThrough, BlazorWindow
  wwwroot/css/hawkeye.css           shared + settings styles, square-corner rule
  wwwroot/images/                   hawkeye.png, classes/<id>.png
  hawkeye.ico                       app icon (from branding/hawkeye-logo.png)
```

## When NOT to use this skill

- Byte layouts, opcodes, captures: hawkeye-protocol-and-captures.
- Building, running, publishing: hawkeye-build-and-run.

## CHANGES

- 2026-11-20: written for v0.1.0 against the code as built that day.
