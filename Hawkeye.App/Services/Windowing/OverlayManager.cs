using System.Windows;
using System.Windows.Threading;
using Hawkeye.App.Pages;
using Hawkeye.App.Views;
using Hawkeye.Core.GameData.Services;
using Hawkeye.Engine.Services.Session;
using Hawkeye.Engine.Services.Timed;
using Hawkeye.Engine.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace Hawkeye.App.Services.Windowing
{
    /// <summary>
    /// Owns every window Hawkeye shows besides the meter: the Settings window and one small icon window per
    /// tracked skill. "Edit mode" = Settings is open: icons can be dragged; otherwise they're click-through.
    /// All methods must be called on the UI thread (Blazor event handlers already run there).
    /// </summary>
    public sealed class OverlayManager
    {
        private readonly IServiceProvider services;
        private readonly ISettingsService settings;
        private readonly ITimedEventTracker cooldowns;
        private readonly ITimedEventTracker buffs;
        private readonly Dictionary<int, TrackerIconWindow> icons = new();
        private Window? settingsWindow;

        public bool IsEditMode => settingsWindow is not null;

        private readonly CombatSessionManager sessions;
        private readonly DispatcherTimer visibilityTimer;
        private bool iconsShown = true;

        public OverlayManager(
            IServiceProvider services,
            ISettingsService settings,
            CombatSessionManager sessions,
            [FromKeyedServices(TimerKeys.Cooldowns)] ITimedEventTracker cooldowns,
            [FromKeyedServices(TimerKeys.Buffs)] ITimedEventTracker buffs)
        {
            this.services = services;
            this.settings = settings;
            this.sessions = sessions;
            this.cooldowns = cooldowns;
            this.buffs = buffs;
            settings.Changed += (_, _) => RunOnUi(SyncIcons);
            visibilityTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            visibilityTimer.Tick += (_, _) => UpdateIconVisibility();
        }

        public void Start()
        {
            SyncIcons();
            visibilityTimer.Start();
        }

        /// <summary>
        /// By default the icons only show during a boss or dummy fight (owner, 2026-11-21). They always show
        /// while Settings is open (to place them) and when "Always show" is on.
        /// </summary>
        private void UpdateIconVisibility()
        {
            bool show = IsEditMode || settings.Current.TrackerAlwaysVisible || sessions.IsInBossFight(DateTime.Now);
            if (show == iconsShown) return;
            iconsShown = show;
            foreach (var icon in icons.Values)
            {
                if (show) { icon.Show(); icon.BringToFront(); }
                else icon.Hide();
            }
        }

        // ------------------------------------------------------------------ settings window

        public void OpenSettings()
        {
            if (settingsWindow is not null)
            {
                settingsWindow.Activate();
                return;
            }

            var main = Application.Current.MainWindow;
            settingsWindow = new BlazorWindow(services, typeof(SettingsPage)) { Width = 420, Height = 640 };
            if (main is not null)
            {
                settingsWindow.Owner = main;
                PlaceBeside(settingsWindow, main);
            }
            // Clicking into Settings raises it; push the icons back above it so they stay grabbable.
            settingsWindow.Activated += (_, _) =>
            {
                foreach (var icon in icons.Values) icon.BringToFront();
            };
            settingsWindow.Closed += (_, _) =>
            {
                settingsWindow = null;
                SetIconsEditMode(false);
            };
            settingsWindow.Show();

            // Icons go above the Settings window so none of them end up hidden behind it.
            UpdateIconVisibility();
            SetIconsEditMode(true);
        }

        /// <summary>
        /// Closes Settings AFTER the current click has finished. The ✕ button lives inside the window's own
        /// web view; closing synchronously destroyed the web view mid-click, the page's follow-up redraw then
        /// hit a disposed control and took the whole app down (hawkeye-failure-archaeology I8).
        /// </summary>
        public void CloseSettings()
        {
            var window = settingsWindow;
            window?.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, () => window.Close());
        }

        public void DragSettings() => TryDragMove(settingsWindow);

        // ------------------------------------------------------------------ tracker icons

        /// <summary>Fakes a timer on every tracked skill (4s, 7s, 10s, ...) to check the look without fighting.</summary>
        public void PreviewTimers()
        {
            var tracked = settings.Current.TrackedSkills;
            for (int i = 0; i < tracked.Count; i++)
            {
                var skill = GameDataProvider.Instance.GetSkillOrDefault(tracked[i].SkillId);
                var timers = tracked[i].Mode == TrackMode.Buff ? buffs : cooldowns;
                timers.Register(new TimedEvent
                {
                    Id = unchecked((uint)tracked[i].SkillId),
                    Name = skill.Name,
                    IconUrl = skill.Icon ?? string.Empty,
                    Duration = TimeSpan.FromSeconds(4 + i * 3),
                });
            }
        }

        /// <summary>Opens icons for newly tracked skills and closes the ones no longer tracked.</summary>
        private void SyncIcons()
        {
            var current = settings.Current;
            var wanted = current.TrackerEnabled
                ? current.TrackedSkills.Select(t => t.SkillId).Distinct().ToList()
                : new List<int>();

            foreach (var id in icons.Keys.Where(id => !wanted.Contains(id)).ToList())
            {
                var stale = icons[id];
                icons.Remove(id);
                stale.Close();
            }

            for (int slot = 0; slot < wanted.Count; slot++)
            {
                var id = wanted[slot];
                if (icons.ContainsKey(id)) continue;

                var icon = new TrackerIconWindow(id, slot, settings, cooldowns, buffs);
                icons[id] = icon;
                icon.Show();
                icon.SetEditMode(IsEditMode);
                if (!iconsShown) icon.Hide();
            }
        }

        private void SetIconsEditMode(bool editMode)
        {
            foreach (var icon in icons.Values) icon.SetEditMode(editMode);
        }

        // ------------------------------------------------------------------ helpers

        public static void TryDragMove(Window? window)
        {
            if (window is null) return;
            try { window.DragMove(); }
            catch (InvalidOperationException) { /* mouse already released */ }
        }

        /// <summary>
        /// Starts a Windows resize from an invisible edge on the page ("right", "bottom" or "corner"),
        /// the same way DragMove starts a move. Replaces the visible resize grip.
        /// </summary>
        public static void TryResize(Window? window, string edge)
        {
            if (window is null) return;
            int code = edge switch { "right" => 2, "bottom" => 6, _ => 8 }; // WMSZ_RIGHT, WMSZ_BOTTOM, WMSZ_BOTTOMRIGHT
            var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;
            NativeResize.ReleaseCapture();
            NativeResize.SendMessage(hwnd, 0x0112 /* WM_SYSCOMMAND */, (IntPtr)(0xF000 /* SC_SIZE */ + code), IntPtr.Zero);
        }

        private static class NativeResize
        {
            [System.Runtime.InteropServices.DllImport("user32.dll")]
            public static extern bool ReleaseCapture();

            [System.Runtime.InteropServices.DllImport("user32.dll")]
            public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
        }

        private static void PlaceBeside(Window window, Window anchor)
        {
            var area = SystemParameters.WorkArea;
            double left = anchor.Left + anchor.ActualWidth + 8;
            if (left + window.Width > area.Right) left = Math.Max(area.Left, anchor.Left - window.Width - 8);
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = left;
            window.Top = Math.Clamp(anchor.Top, area.Top, Math.Max(area.Top, area.Bottom - window.Height));
        }

        private static void RunOnUi(Action action)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is null) return;
            if (dispatcher.CheckAccess()) action();
            else dispatcher.BeginInvoke(action);
        }
    }
}
