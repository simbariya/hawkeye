using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using Hawkeye.App.Services;
using Hawkeye.App.Services.Windowing;
using Hawkeye.Engine.Services.Session;
using Hawkeye.Engine.Settings;
using Serilog;

namespace Hawkeye.App.ViewModels
{
    /// <summary>One row on the meter.</summary>
    public sealed class PlayerRow
    {
        public long PlayerId { get; init; }
        public bool IsUser { get; set; }
        public int ClassId { get; set; }
        public string Name { get; set; } = string.Empty;
        public long TotalDamage { get; set; }
        public string Total { get; set; } = string.Empty;
        public string Dps { get; set; } = "0";
        public int Deaths { get; set; }
        public string Detail { get; set; } = string.Empty;

        /// <summary>Bar width in percent of the top player, eased so bars glide instead of jumping.</summary>
        public double BarPercent { get; set; }
    }

    /// <summary>One piece of the status line. Css picks its colour; Clickable items run an action.</summary>
    public sealed record StatusItem(string Text, string Css, string Title = "", bool Clickable = false);

    /// <summary>
    /// Feeds the meter page. Polls the engine ~30 times a second and asks the page to redraw when anything changed.
    /// </summary>
    public sealed class MeterViewModel : IDisposable
    {
        private readonly CombatSessionManager sessions;
        private readonly SystemStatsService stats;
        private readonly ISettingsService settings;
        private readonly OverlayManager overlays;
        private readonly Func<Task> redraw;
        private readonly Dictionary<long, PlayerRow> rows = new();
        private readonly CancellationTokenSource cts = new();

        public IReadOnlyList<PlayerRow> Players { get; private set; } = [];
        public string TargetName { get; private set; } = string.Empty;
        public string Duration { get; private set; } = "00:00";
        /// <summary>Ping / FPS / CPU / RAM pieces, whichever are switched on.</summary>
        public IReadOnlyList<StatusItem> StatusItems { get; private set; } = [];
        /// <summary>Whole-fight total and deaths, for the summary line (empty when switched off).</summary>
        public string FightTotal { get; private set; } = string.Empty;
        public int FightDeaths { get; private set; }

        public bool ShowTotals => settings.Current.ShowTotalDamage;
        public bool ShowDeaths => settings.Current.ShowDeaths;

        /// <summary>The dummy Start/Stop button: shown only in Start/Stop mode.</summary>
        public bool ShowDummyButton => settings.Current.DummyMode == DummyMode.StartStop;
        public bool DummyRunning => sessions.DummyRun is CombatSessionManager.DummyRunState.Armed or CombatSessionManager.DummyRunState.Running;
        public bool DummyWaiting => sessions.DummyRun == CombatSessionManager.DummyRunState.Armed;

        private string stateKey = string.Empty;

        public MeterViewModel(CombatSessionManager sessions, SystemStatsService stats, ISettingsService settings,
            OverlayManager overlays, Func<Task> redraw)
        {
            this.sessions = sessions;
            this.stats = stats;
            this.settings = settings;
            this.overlays = overlays;
            this.redraw = redraw;
            _ = LoopAsync();
        }

        private async Task LoopAsync()
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(33));
            try
            {
                while (await timer.WaitForNextTickAsync(cts.Token))
                {
                    if (Refresh()) await redraw();
                }
            }
            catch (OperationCanceledException) { }
        }

        private bool Refresh()
        {
            bool changed = false;

            var duration = sessions.GetCombatDuration().ToString(@"mm\:ss");
            var target = sessions.GetActiveTargetInfo()?.Name ?? string.Empty;
            var status = BuildStatusItems();
            var deaths = ShowDeaths ? sessions.GetActiveBossDeaths() : new Dictionary<int, int>();

            var current = sessions.PlayerStats.Where(p => p.IsIdentified || p.DamagePercentage > 1).ToList();
            long fightTotal = current.Sum(p => p.TotalDamage);
            string total = ShowTotals && fightTotal > 0 ? Compact(fightTotal) : string.Empty;
            int fightDeaths = deaths.Values.Sum();

            var key = string.Join("|", status) + $"|{duration}|{target}|{total}|{fightDeaths}|{ShowTotals}|{ShowDeaths}|{ShowDummyButton}|{sessions.DummyRun}";
            if (key != stateKey)
            {
                Duration = duration;
                TargetName = target;
                StatusItems = status;
                FightTotal = total;
                FightDeaths = fightDeaths;
                stateKey = key;
                changed = true;
            }

            long top = current.Count > 0 ? current.Max(p => p.TotalDamage) : 0;
            var seen = new HashSet<long>();

            foreach (var p in current)
            {
                seen.Add(p.PlayerId);
                if (!rows.TryGetValue(p.PlayerId, out var row))
                {
                    row = new PlayerRow { PlayerId = p.PlayerId };
                    rows[p.PlayerId] = row;
                }

                row.IsUser = p.IsUser;
                row.ClassId = p.ClassId;
                row.Name = p.PlayerName;
                row.TotalDamage = p.TotalDamage;
                row.Total = Compact(p.TotalDamage);
                row.Dps = Math.Round(p.DamagePerSecond).ToString("N0", CultureInfo.InvariantCulture);
                row.Deaths = deaths.GetValueOrDefault((int)p.PlayerId);
                row.Detail = $"{p.TotalDamage.ToString("N0", CultureInfo.InvariantCulture)} damage ({p.DamagePercentage:F1}%)"
                             + (row.Deaths > 0 ? $", died {row.Deaths}x on this boss" : string.Empty);

                double targetPercent = top > 0 ? 100.0 * p.TotalDamage / top : 0;
                double diff = targetPercent - row.BarPercent;
                row.BarPercent = Math.Abs(diff) < 0.05 ? targetPercent : row.BarPercent + diff * 0.25;
                changed = true;
            }

            foreach (var gone in rows.Keys.Where(k => !seen.Contains(k)).ToList())
            {
                rows.Remove(gone);
                changed = true;
            }

            if (changed) Players = rows.Values.OrderByDescending(r => r.TotalDamage).ToList();
            return changed;
        }

        /// <summary>8,238,521 -> "8.24M", 823,400 -> "823.4K".</summary>
        public static string Compact(long value) => value switch
        {
            >= 1_000_000_000 => (value / 1_000_000_000d).ToString("0.00", CultureInfo.InvariantCulture) + "B",
            >= 1_000_000 => (value / 1_000_000d).ToString("0.00", CultureInfo.InvariantCulture) + "M",
            >= 1_000 => (value / 1_000d).ToString("0.0", CultureInfo.InvariantCulture) + "K",
            _ => value.ToString(CultureInfo.InvariantCulture),
        };

        /// <summary>Ping colour: bright green under 30 ms, yellow up to 100 ms, red above.</summary>
        public static string PingCss(int ms) => ms < 30 ? "is-good" : ms <= 100 ? "is-ok" : "is-bad";

        private List<StatusItem> BuildStatusItems()
        {
            var s = settings.Current;
            var parts = new List<StatusItem>(4);
            if (s.ShowPing)
                parts.Add(stats.PingMs is int ping
                    ? new StatusItem($"{ping} ms", "status-ping " + PingCss(ping), "Latency to the game server")
                    : new StatusItem("-- ms", "status-ping", "Waiting for the game server"));
            if (s.ShowFps)
                parts.Add(stats.FpsNeedsAdmin
                    ? new StatusItem("FPS: run as admin", "status-fps needs-admin",
                        "Windows only shares the game's frame rate with programs run as administrator. Click to restart Hawkeye as administrator.",
                        Clickable: true)
                    : stats.Fps is double fps
                        ? new StatusItem($"{fps:0} FPS", "status-fps", "Game frame rate")
                        : new StatusItem("-- FPS", "status-fps", "Game not found, or no frames yet"));
            if (s.ShowCpu) parts.Add(new StatusItem($"CPU {stats.CpuPercent:0}%", "status-cpu", "Whole-system CPU use"));
            if (s.ShowRam) parts.Add(new StatusItem($"RAM {stats.RamUsedGb:0.0}/{stats.RamTotalGb:0} GB", "status-ram", "Whole-system memory in use"));
            return parts;
        }

        public void OnStatusClick(StatusItem item)
        {
            if (item.Clickable && item.Css.Contains("needs-admin")) RestartAsAdmin();
        }

        /// <summary>Starts an elevated copy of Hawkeye (one Windows prompt), then closes this one.</summary>
        public void RestartAsAdmin()
        {
            try
            {
                Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas" });
                Close();
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                // The owner said No at the Windows prompt: keep running as we are.
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Could not restart as administrator");
            }
        }

        public void ToggleDummyRun()
        {
            if (DummyRunning) sessions.StopDummyRun();
            else sessions.StartDummyRun();
        }

        public void BeginDrag() => OverlayManager.TryDragMove(Application.Current.MainWindow);
        public void BeginResize(string edge) => OverlayManager.TryResize(Application.Current.MainWindow, edge);
        public void OpenSettings() => overlays.OpenSettings();
        /// <summary>Quit after the current click has finished drawing (same reason as OverlayManager.CloseSettings).</summary>
        public void Close() =>
            Application.Current.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background,
                () => Application.Current.Shutdown());

        public void Dispose() => cts.Cancel();
    }
}
