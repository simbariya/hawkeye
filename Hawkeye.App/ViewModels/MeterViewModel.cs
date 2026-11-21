using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using Hawkeye.App.Services;
using Hawkeye.App.Services.Windowing;
using Hawkeye.Engine.Services.History;
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
        private readonly MeterSource source;
        private readonly DetailsState details;
        private readonly Func<Task> redraw;
        private readonly Dictionary<long, PlayerRow> rows = new();
        private readonly CancellationTokenSource cts = new();

        public IReadOnlyList<PlayerRow> Players { get; private set; } = [];
        public string TargetName { get; private set; } = string.Empty;
        public string Duration { get; private set; } = "00:00";
        /// <summary>Ping / FPS / CPU / RAM pieces, whichever are switched on.</summary>
        public IReadOnlyList<StatusItem> StatusItems { get; private set; } = [];
        /// <summary>Rows the meter makes room for when it fits its height to the group: 1 to 5.</summary>
        public int FitRows { get; private set; } = 1;
        public bool AutoHeight => settings.Current.AutoHeight;

        /// <summary>Row height + gap in px; must match .meter-row and .meter-rows in MeterPage.razor.css.</summary>
        public const int RowPx = 20, RowGapPx = 5, MaxFitRows = 5;
        public int RowsHeightPx => FitRows * RowPx + (FitRows - 1) * RowGapPx;

        public bool ShowTotals => settings.Current.ShowTotalDamage;
        public bool ShowDeaths => settings.Current.ShowDeaths;

        /// <summary>The dummy Start/Stop button: shown only in Start/Stop mode.</summary>
        public bool ShowDummyButton => settings.Current.DummyMode == DummyMode.StartStop;
        public bool DummyRunning => sessions.DummyRun is CombatSessionManager.DummyRunState.Armed or CombatSessionManager.DummyRunState.Running;
        public bool DummyWaiting => sessions.DummyRun == CombatSessionManager.DummyRunState.Armed;

        private string stateKey = string.Empty;

        /// <summary>
        /// True when no fight of yours is running and the meter shows your last fight from history instead
        /// (someone else's fight never replaces it; owner 2026-11-21).
        /// </summary>
        public bool ShowingHistory => shown is not null;
        private EncounterSnapshot? shown;

        public MeterViewModel(CombatSessionManager sessions, SystemStatsService stats, ISettingsService settings,
            OverlayManager overlays, MeterSource source, DetailsState details, Func<Task> redraw)
        {
            this.sessions = sessions;
            this.stats = stats;
            this.settings = settings;
            this.overlays = overlays;
            this.source = source;
            this.details = details;
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

            var status = BuildStatusItems(settings.Current, stats);
            var data = source.Read();
            shown = data.Shown;
            string duration = data.Duration.ToString(@"mm\:ss");
            string target = data.Target;
            var current = data.Rows;

            int fitRows = Math.Clamp(Math.Max(current.Count, sessions.PartySize), 1, MaxFitRows);

            var key = string.Join("|", status) + $"|{duration}|{target}|{fitRows}|{AutoHeight}|{ShowTotals}|{ShowDeaths}|{ShowDummyButton}|{sessions.DummyRun}|{ShowingHistory}";
            if (key != stateKey)
            {
                Duration = duration;
                TargetName = target;
                StatusItems = status;
                FitRows = fitRows;
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
                row.Name = p.Name;
                row.TotalDamage = p.TotalDamage;
                row.Total = Compact(p.TotalDamage);
                row.Dps = Compact((long)Math.Round(p.Dps), shortThousands: true);
                row.Deaths = p.Deaths;
                row.Detail = $"{p.TotalDamage.ToString("N0", CultureInfo.InvariantCulture)} damage ({p.Share:F1}%)"
                             + (row.Deaths > 0 ? $", died {row.Deaths}x on this boss" : string.Empty)
                             + ". Click for the skill breakdown";

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

        /// <summary>
        /// Short numbers. Totals: 8,238,521 -> "8.24M", 823,400 -> "823.4K".
        /// DPS (shortThousands, owner 2026-11-21: "10k/s rather than 10,000/s"): 137,985 -> "138k",
        /// 9,840 -> "9.8k", 1,240,000 -> "1.24M", 950 -> "950".
        /// </summary>
        public static string Compact(long value, bool shortThousands = false) => value switch
        {
            >= 1_000_000_000 => (value / 1_000_000_000d).ToString("0.00", CultureInfo.InvariantCulture) + "B",
            >= 1_000_000 => (value / 1_000_000d).ToString("0.00", CultureInfo.InvariantCulture) + "M",
            >= 10_000 when shortThousands => (value / 1_000d).ToString("0", CultureInfo.InvariantCulture) + "k",
            >= 1_000 when shortThousands => (value / 1_000d).ToString("0.0", CultureInfo.InvariantCulture) + "k",
            >= 1_000 => (value / 1_000d).ToString("0.0", CultureInfo.InvariantCulture) + "K",
            _ => value.ToString(CultureInfo.InvariantCulture),
        };

        /// <summary>Sets the meter window's height (from the page's measurement), on the UI thread.</summary>
        public void ApplyMeasuredHeight(double height)
        {
            var window = Application.Current?.MainWindow;
            if (window is null || height < window.MinHeight) return;
            if (Math.Abs(window.Height - height) >= 1) window.Height = height;
        }

        /// <summary>Ping colour: bright green under 30 ms, yellow up to 100 ms, red above.</summary>
        public static string PingCss(int ms) => ms < 30 ? "is-good" : ms <= 100 ? "is-ok" : "is-bad";

        /// <summary>The status line pieces; shared with streamer mode.</summary>
        public static List<StatusItem> BuildStatusItems(HawkeyeSettings s, SystemStatsService stats)
        {
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
                Process.Start(new ProcessStartInfo(Environment.ProcessPath!)
                {
                    UseShellExecute = true,
                    Verb = "runas",
                    WorkingDirectory = AppContext.BaseDirectory, // else an elevated start lands in System32
                });
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

        /// <summary>Opens the combat analysis for this player: live if the fight is running, else the shown fight.</summary>
        public void OpenDetails(long playerId)
        {
            if (shown is not null) details.ShowSaved(shown, playerId);
            else details.ShowLive(sessions.BuildLiveSnapshot(), playerId);
            overlays.OpenDetails();
        }

        public void OpenHistory() => overlays.OpenHistory();
        public void OpenExtras() => overlays.OpenExtras();

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
