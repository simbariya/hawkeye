using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using Hawkeye.App.Services;
using Hawkeye.App.Services.Windowing;
using Hawkeye.Engine.Services.History;
using Hawkeye.Engine.Services.Pvp;
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

    /// <summary>One enemy in the arena view: who, their rating, and their Defiance.</summary>
    public sealed record ArenaFoeView(string Name, string Rating, bool DefianceDown, string DefianceText, double DefianceBar);

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
        private readonly UpdateService updates;
        private readonly PvpTracker pvp;
        private readonly PvpHistory pvpHistory;
        private readonly PvpView pvpView;
        private readonly Hawkeye.Engine.Services.Theostone.TheostoneAlerts theostones;
        private readonly Hawkeye.Engine.Services.Arena.ArenaTracker arena;

        /// <summary>Theostone procs to flash on the meter (yours, and others' hitting you), oldest first.</summary>
        public IReadOnlyList<Hawkeye.Engine.Services.Theostone.TheostoneProc> Theostones { get; private set; } = [];


        /// <summary>Debuff time left as a percentage for its bar, or -1 for a plain hit.</summary>
        public double ProcBar(Hawkeye.Engine.Services.Theostone.TheostoneProc p) =>
            p.IsDebuff && p.Duration.TotalMilliseconds > 0 ? 100 * theostones.TimeLeft(p).TotalMilliseconds / p.Duration.TotalMilliseconds : -1;

        /// <summary>The tile's big line: seconds left on a debuff, else the damage.</summary>
        public string ProcMain(Hawkeye.Engine.Services.Theostone.TheostoneProc p) =>
            Hawkeye.App.Views.TheostoneAlertWindow.MainText(p, theostones.TimeLeft(p));
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

        public const int MaxFitRows = 5;

        /// <summary>Room for FitRows rows in the current look (row sizes from <see cref="Looks.RowMetrics"/>).</summary>
        public int RowsHeightPx
        {
            get
            {
                var (row, gap, pad) = Looks.RowMetrics(settings.Current.Look);
                return FitRows * row + (FitRows - 1) * gap + pad;
            }
        }

        // ---- PvP mode: kills / deaths / assists / AP / rank points since switched on or cleared ----
        public bool PvpMode => pvp.Active;
        public int Kills => pvp.Kills;
        public int Deaths => pvp.Deaths;
        public int Assists => pvp.Assists;
        public string ApGained => Signed(pvp.ApGained);
        public string RankGained => Signed(pvp.RankGained);
        // Rank line (2026-11-22): always shown in PvP mode. Your class rank and the rank points still needed to pass
        // the one above, or a nudge to open the Abyss Rank page (no ranking yet, ranks recalculated, or rank reached).
        public RankWords.RankLine Rank { get; private set; }
        public string RankClass => Rank.State switch
        {
            RankWords.LineState.Live => "",
            RankWords.LineState.Reached => "is-reached",
            _ => "is-old",
        };

        private string TheostoneKey()
        {
            Theostones = settings.Current.TheostonePlace == TheostonePlace.Meter ? theostones.Showing : [];
            // Debuffs count down: redraw every tenth of a second while one shows.
            return string.Join(",", Theostones.Select(p => $"{p.At.Ticks}{p.Hits}{(p.IsDebuff ? (int)(theostones.TimeLeft(p).TotalSeconds * 10) : 0)}"));
        }

        private string RankKey()
        {
            Rank = RankWords.Line(pvp.Standing, pvp.ClassName, DateTime.Now);
            return Rank.Left + Rank.Right + Rank.Tip;
        }

        private static string Signed(long v) => (v > 0 ? "+" : "") + v.ToString("N0", CultureInfo.InvariantCulture);

        /// <summary>Saves this PvP session to the PvP history (if anything happened) and starts a fresh one.</summary>
        public void EndPvpSession()
        {
            if (pvp.EndSession() is { } ended) pvpHistory.Add(ended);
            stateKey = string.Empty;
        }

        /// <summary>Opens the list of who this session's kills, deaths or assists were about.</summary>
        public void OpenPvpList(PvpEventKind kind)
        {
            pvpView.ShowLive(kind);
            overlays.OpenPvp();
        }

        public void TogglePvp()
        {
            if (pvp.Active || arena.Showing)
            {
                pvp.SetActive(false);
                arena.Showing = false;
            }
            else pvp.SetActive(true);
            stateKey = string.Empty;
        }

        // ---- Arena mode (v0.2.26): the second tab of the PvP panel; a match loading switches it on ----
        public bool ArenaMode => arena.Showing;
        public bool PvpButtonOn => pvp.Active || arena.Showing;
        public bool InArenaMatch => arena.InMatch;
        public bool HasArenaMatch => arena.Match is not null;
        public string ArenaScore => arena.Match is { } m ? $"{m.OurKills} - {m.TheirKills}" : "";
        public string ArenaResult => arena.Match?.Won switch { true => "Won", false => "Lost", _ => "" };
        public int ArenaWins => arena.Wins;
        public int ArenaLosses => arena.Losses;
        public string ArenaRating => arena.Rating?.ToString("N0", CultureInfo.InvariantCulture) ?? "-";
        public string ArenaChange => Signed(arena.RatingChange);
        public IReadOnlyList<ArenaFoeView> ArenaFoes { get; private set; } = [];

        /// <summary>Opens the list of who this session's arena wins or losses were against (Kill = wins, Death = losses).</summary>
        public void OpenArenaList(PvpEventKind kind)
        {
            pvpView.ShowArena(kind);
            overlays.OpenPvp();
        }

        /// <summary>World or Arena tab of the PvP panel.</summary>
        public void ShowArena(bool on)
        {
            arena.Showing = on;
            if (!on) pvp.SetActive(true);
            stateKey = string.Empty;
        }

        private string ArenaKey()
        {
            if (!arena.Showing) { ArenaFoes = []; return ""; }
            var m = arena.Match;
            ArenaFoes = m is null ? [] : m.Enemies.Select(e =>
            {
                var left = arena.DefianceLeft(e);
                bool down = left > TimeSpan.Zero;
                return new ArenaFoeView(e.Name, e.Rating > 0 ? e.Rating.ToString("N0", CultureInfo.InvariantCulture) : "",
                    down, down ? $"{Math.Ceiling(left.TotalSeconds):0}s" : "Ready",
                    down ? 100 * left.TotalMilliseconds / e.DefianceCooldown.TotalMilliseconds : 0);
            }).ToList();
            // Redraw every tenth of a second while a countdown runs.
            return $"{arena.Wins}|{arena.Losses}|{arena.Rating}|{arena.RatingChange}|{ArenaScore}|{ArenaResult}|" +
                   string.Join(",", ArenaFoes.Select(f => f.Name + f.DefianceText + (int)(f.DefianceBar * 10)));
        }

        /// <summary>CSS class for the chosen look.</summary>
        public string LookClass => Looks.ClassFor(settings.Current.Look);

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
            OverlayManager overlays, MeterSource source, DetailsState details, UpdateService updates, PvpTracker pvp, PvpHistory pvpHistory, PvpView pvpView,
            Hawkeye.Engine.Services.Theostone.TheostoneAlerts theostones, Hawkeye.Engine.Services.Arena.ArenaTracker arena, Func<Task> redraw)
        {
            this.sessions = sessions;
            this.stats = stats;
            this.settings = settings;
            this.overlays = overlays;
            this.source = source;
            this.updates = updates;
            this.pvp = pvp;
            this.pvpHistory = pvpHistory;
            this.pvpView = pvpView;
            this.theostones = theostones;
            this.arena = arena;
            this.details = details;
            this.redraw = redraw;
            _ = LoopAsync();
        }

        private DateTime lastLoopError;

        private async Task LoopAsync()
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(33));
            try
            {
                while (await timer.WaitForNextTickAsync(cts.Token))
                {
                    // One bad frame must not end the loop: on 2026-11-23 an error in one refresh stopped it for good
                    // and the meter's numbers froze until Hawkeye was restarted. Skip that frame, keep going.
                    try
                    {
                        if (Refresh()) await redraw();
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        if (DateTime.UtcNow - lastLoopError > TimeSpan.FromMinutes(1))
                        {
                            lastLoopError = DateTime.UtcNow;
                            Serilog.Log.Warning(ex, "[METER] a refresh failed; the meter carries on");
                        }
                    }
                }
            }
            catch (OperationCanceledException) { }
        }

        private bool Refresh()
        {
            bool changed = false;

            var status = BuildStatusItems(settings.Current, stats);
            if (updates.JustUpdatedTo is { } updatedTo)
                status.Add(new StatusItem($"Updated to {updatedTo.ToString(3)}", "status-update", "Hawkeye just installed this version"));
            if (updates.ReadyVersion is { } ready)
                status.Add(new StatusItem($"Update {ready.ToString(3)} ready", "status-update",
                    "Click to restart into the new version now. Or just close Hawkeye: it installs on exit.", Clickable: true));
            var data = source.Read();
            shown = data.Shown;
            string duration = data.Duration.ToString(@"mm\:ss");
            string target = data.Target;
            if (arena.Showing)
            {
                var elapsed = DateTime.Now - (arena.Match is { Won: null } m ? m.At : arena.StartedAt);
                duration = elapsed.TotalHours >= 1 ? elapsed.ToString(@"h\:mm\:ss") : elapsed.ToString(@"mm\:ss");
                target = "Arena";
            }
            else if (pvp.Active)
            {
                var elapsed = DateTime.Now - pvp.StartedAt;
                duration = elapsed.TotalHours >= 1 ? elapsed.ToString(@"h\:mm\:ss") : elapsed.ToString(@"mm\:ss");
                target = "PvP";
            }
            var current = data.Rows;

            int fitRows = Math.Clamp(Math.Max(current.Count, sessions.PartySize), 1, MaxFitRows);

            var key = string.Join("|", status) + $"|{duration}|{target}|{fitRows}|{AutoHeight}|{ShowTotals}|{ShowDeaths}|{ShowDummyButton}|{sessions.DummyRun}|{ShowingHistory}|{settings.Current.Look}|{pvp.Active}|{pvp.Kills}|{pvp.Deaths}|{pvp.Assists}|{pvp.ApGained}|{pvp.RankGained}|{(pvp.Active ? RankKey() : "")}|{TheostoneKey()}|{arena.Showing}|{ArenaKey()}";
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
        // ---- side strip folding (owner, 2026-11-21: on a short window the PvP button did not fit) ----
        public const int SideButtonCount = 4;

        /// <summary>How many strip slots fit right now (measured after each render).</summary>
        public int SideSlots { get; set; } = 99;

        /// <summary>The chevron was clicked: show every strip button, growing the window if needed.</summary>
        public bool SideExpanded { get; private set; }

        /// <summary>Folded: some buttons hidden behind the chevron.</summary>
        public bool SideFolded => !SideExpanded && SideSlots < SideButtonCount;

        /// <summary>How many buttons show: all, or what fits beside the chevron.</summary>
        public int SideVisible => SideFolded ? Math.Max(0, SideSlots - 1) : SideButtonCount;

        public void ToggleSide()
        {
            SideExpanded = !SideExpanded;
            stateKey = string.Empty;
        }

        /// <summary>Makes the window at least this tall (never shrinks it).</summary>
        public void GrowTo(double height)
        {
            var window = Application.Current?.MainWindow;
            if (window is not null && height > window.Height + 0.5) window.Height = height;
        }

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
            if (s.ShowRam)
            {
                double pct = stats.RamTotalGb > 0 ? 100.0 * stats.RamUsedGb / stats.RamTotalGb : 0;
                parts.Add(new StatusItem($"RAM {pct:0}%", "status-ram", $"Whole-system memory in use: {stats.RamUsedGb:0.0} of {stats.RamTotalGb:0} GB"));
            }
            return parts;
        }

        public void OnStatusClick(StatusItem item)
        {
            if (item.Clickable && item.Css.Contains("needs-admin")) RestartAsAdmin();
            if (item.Clickable && item.Css.Contains("status-update") && updates.LaunchInstaller(restart: true)) Close();
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

        /// <summary>Clear button: blank the meter until your next fight (fights are saved to History first).</summary>
        public void Clear()
        {
            if (arena.Showing)
            {
                arena.Reset();   // in the arena view the bin starts the session's wins and losses again
                stateKey = string.Empty;
                return;
            }
            if (pvp.Active)
            {
                pvp.Reset();   // in PvP mode the bin starts the PvP count again
                stateKey = string.Empty;
                return;
            }
            source.Clear();
            rows.Clear();
            stateKey = string.Empty;
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
