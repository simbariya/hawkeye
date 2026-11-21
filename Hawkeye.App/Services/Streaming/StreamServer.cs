using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Hawkeye.App.Services.Windowing;
using Hawkeye.App.ViewModels;
using Hawkeye.Core.GameData.Services;
using Hawkeye.Engine.Services.Timed;
using Hawkeye.Engine.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Hawkeye.App.Services.Streaming
{
    /// <summary>
    /// Streamer mode: serves a transparent page at http://localhost:{port}/ for an OBS Browser Source. The page
    /// draws the meter and the skill icons where they are on your screen, in the same style, and polls
    /// /state ten times a second.
    ///
    /// Threading: the state is built on the UI thread (it reads WPF windows) by a 100 ms timer and kept as a
    /// ready JSON string; the HTTP thread only hands that string out. Only listens on localhost.
    /// Page files are embedded in the exe (wwwroot\stream and wwwroot\images), so they work in every build.
    /// </summary>
    public sealed class StreamServer : IDisposable
    {
        private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        private readonly ISettingsService settings;
        private readonly MeterSource meter;
        private readonly SystemStatsService stats;
        private readonly OverlayManager overlays;
        private readonly ITimedEventTracker cooldowns;
        private readonly ITimedEventTracker buffs;
        private readonly ILogger<StreamServer> logger;
        private readonly Hawkeye.Engine.Services.Pvp.PvpTracker pvp;
        private readonly DispatcherTimer timer;
        private readonly DispatcherTimer captureTimer;

        private HttpListener? listener;
        private int runningPort;
        private volatile string stateJson = "{}";

        /// <summary>
        /// Different every time Hawkeye starts. The page reloads itself when it changes, because OBS keeps the page
        /// it first loaded: after an update it would otherwise go on running the old page (owner hit this in 0.1.7).
        /// </summary>
        private static readonly string Instance = Guid.NewGuid().ToString("N");

        /// <summary>Shown in Settings: "Running", or why it is not.</summary>
        public string Status { get; private set; } = "Off";
        public string Url => $"http://localhost:{settings.Current.StreamerPort}/";
        public event EventHandler? StatusChanged;

        public StreamServer(ISettingsService settings, MeterSource meter, SystemStatsService stats, OverlayManager overlays,
            [FromKeyedServices(TimerKeys.Cooldowns)] ITimedEventTracker cooldowns,
            [FromKeyedServices(TimerKeys.Buffs)] ITimedEventTracker buffs,
            Hawkeye.Engine.Services.Pvp.PvpTracker pvp,
            ILogger<StreamServer> logger)
        {
            this.pvp = pvp;
            this.settings = settings;
            this.meter = meter;
            this.stats = stats;
            this.overlays = overlays;
            this.cooldowns = cooldowns;
            this.buffs = buffs;
            this.logger = logger;
            timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            timer.Tick += (_, _) => BuildState();
            captureTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            captureTimer.Tick += (_, _) => CaptureWindows();
            settings.Changed += (_, _) => Application.Current?.Dispatcher.BeginInvoke(Apply);
        }

        /// <summary>Call once on the UI thread after the windows exist.</summary>
        public void Start() => Apply();

        private void Apply()
        {
            var s = settings.Current;
            if (s.StreamerMode && listener is not null && runningPort == s.StreamerPort) return;
            StopListener();
            if (!s.StreamerMode)
            {
                timer.Stop();
                captureTimer.Stop();
                SetStatus("Off");
                return;
            }

            try
            {
                var l = new HttpListener();
                l.Prefixes.Add($"http://localhost:{s.StreamerPort}/");
                l.Prefixes.Add($"http://127.0.0.1:{s.StreamerPort}/");
                l.Start();
                listener = l;
                runningPort = s.StreamerPort;
                _ = Task.Run(() => ServeAsync(l));
                timer.Start();
                captureTimer.Start();
                BuildState();
                SetStatus("Running");
                logger.LogInformation("[STREAM] Streamer mode on at {Url}", Url);
            }
            catch (Exception ex)
            {
                listener = null;
                timer.Stop();
                SetStatus($"Could not start on port {s.StreamerPort}: {ex.Message}");
                logger.LogWarning(ex, "[STREAM] Could not start streamer mode on port {Port}", s.StreamerPort);
            }
        }

        private void SetStatus(string status)
        {
            Status = status;
            StatusChanged?.Invoke(this, EventArgs.Empty);
        }

        private void StopListener()
        {
            var l = listener;
            listener = null;
            if (l is null) return;
            try { l.Stop(); l.Close(); } catch { /* already gone */ }
        }

        // ------------------------------------------------------------------ HTTP

        private async Task ServeAsync(HttpListener l)
        {
            while (l.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = await l.GetContextAsync(); }
                catch { return; } // stopped

                try { Handle(ctx); }
                catch (Exception ex) { logger.LogDebug(ex, "[STREAM] request failed"); }
                finally { try { ctx.Response.Close(); } catch { } }
            }
        }

        private void Handle(HttpListenerContext ctx)
        {
            var path = ctx.Request.Url?.AbsolutePath ?? "/";
            var res = ctx.Response;
            res.Headers["Cache-Control"] = "no-store";

            if (path.StartsWith("/window/") && path.EndsWith(".png"))
            {
                var id = path["/window/".Length..^".png".Length];
                if (shots.TryGetValue(id, out var shot)) Write(res, "image/png", shot.Png);
                else res.StatusCode = 404;
                return;
            }

            if (path == "/state")
            {
                Write(res, "application/json", Encoding.UTF8.GetBytes(stateJson));
                return;
            }

            // "/" is the page; everything else is a file from wwwroot (stream page files, logo, class icons).
            var file = path == "/" ? "stream/index.html" : path.TrimStart('/');
            var bytes = ReadEmbedded(file);
            if (bytes is null)
            {
                res.StatusCode = 404;
                return;
            }
            Write(res, ContentType(file), bytes);
        }

        private static void Write(HttpListenerResponse res, string type, byte[] body)
        {
            res.ContentType = type;
            res.ContentLength64 = body.Length;
            res.OutputStream.Write(body, 0, body.Length);
        }

        private static byte[]? ReadEmbedded(string relativePath)
        {
            if (relativePath.Contains("..")) return null;
            // Embedded as "hawkeye-web/<path with \ separators>" (see Hawkeye.App.csproj).
            var name = "hawkeye-web/" + relativePath.Replace('/', '\\');
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
            if (stream is null) return null;
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            return ms.ToArray();
        }

        private static string ContentType(string file) => Path.GetExtension(file).ToLowerInvariant() switch
        {
            ".html" => "text/html; charset=utf-8",
            ".css" => "text/css; charset=utf-8",
            ".js" => "text/javascript; charset=utf-8",
            ".png" => "image/png",
            ".svg" => "image/svg+xml",
            _ => "application/octet-stream",
        };

        // ------------------------------------------------------------------ state (UI thread)

        private void BuildState()
        {
            try
            {
                var s = settings.Current;
                var main = Application.Current?.MainWindow;
                var data = meter.Read();
                var top = data.Rows.Count > 0 ? data.Rows.Max(r => r.TotalDamage) : 0;

                var state = new
                {
                    screen = new { width = SystemParameters.PrimaryScreenWidth, height = SystemParameters.PrimaryScreenHeight },
                    version = Branding.Version,
                    instance = Instance,
                    meter = !s.StreamShowMeter || main is null ? null : new
                    {
                        left = main.Left,
                        top = main.Top,
                        width = main.ActualWidth,
                        opacity = s.Opacity,
                        look = Looks.ClassFor(s.Look),
                        pvp = !pvp.Active ? null : new
                        {
                            kills = pvp.Kills,
                            deaths = pvp.Deaths,
                            assists = pvp.Assists,
                            ap = (pvp.ApGained > 0 ? "+" : "") + pvp.ApGained.ToString("N0", System.Globalization.CultureInfo.InvariantCulture),
                            rank = (pvp.RankGained > 0 ? "+" : "") + pvp.RankGained.ToString("N0", System.Globalization.CultureInfo.InvariantCulture),
                            time = (DateTime.Now - pvp.StartedAt).ToString(@"mm\:ss"),
                        },
                        target = data.Target,
                        duration = data.Duration.ToString(@"mm\:ss"),
                        fromHistory = data.Shown is not null,
                        showTotals = s.ShowTotalDamage,
                        status = MeterViewModel.BuildStatusItems(s, stats).Select(i => new { text = i.Text, css = i.Css }),
                        rows = data.Rows.Select(r => new
                        {
                            id = r.PlayerId,
                            me = r.IsUser,
                            classId = r.ClassId,
                            name = r.Name,
                            dps = MeterViewModel.Compact((long)Math.Round(r.Dps), shortThousands: true),
                            total = MeterViewModel.Compact(r.TotalDamage),
                            deaths = r.Deaths,
                            bar = top > 0 ? Math.Round(100.0 * r.TotalDamage / top, 2) : 0,
                        }),
                    },
                    windows = BuildWindows(s),
                    icons = !s.StreamShowIcons || !overlays.IconsVisible ? Array.Empty<object>() : overlays.IconWindows.Select(object (w) =>
                    {
                        var tracked = s.TrackedSkills.FirstOrDefault(t => t.SkillId == w.SkillId);
                        var mode = tracked?.Mode ?? TrackMode.Cooldown;
                        var skill = GameDataProvider.Instance.GetSkillOrDefault(w.SkillId);
                        var timerState = (mode == TrackMode.Buff ? buffs : cooldowns).Get(unchecked((uint)w.SkillId));
                        return new
                        {
                            id = w.SkillId,
                            name = skill.Name,
                            icon = skill.Icon,
                            buff = mode == TrackMode.Buff,
                            left = w.IconLeft,
                            top = w.IconTop,
                            size = w.IconSize,
                            leftMs = timerState is null ? 0 : timerState.TimeLeft.TotalMilliseconds,
                            totalMs = timerState is null ? 0 : timerState.Duration.TotalMilliseconds,
                        };
                    }).ToArray(),
                };
                stateJson = JsonSerializer.Serialize(state, Json);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "[STREAM] could not build state");
            }
        }

        // ------------------------------------------------------------------ window pictures (UI thread)

        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (byte[] Png, int Ver)> shots = new();
        private bool capturing;

        /// <summary>
        /// Pictures of every open Hawkeye window (Settings, History, Extras, combat analysis) about 4 times a second,
        /// so the stream shows them exactly as on screen, scroll position included. Unchanged pictures keep their
        /// version, so the page only reloads what changed.
        /// </summary>
        private async void CaptureWindows()
        {
            if (capturing) return;
            capturing = true;
            try
            {
                var open = settings.Current.StreamShowDetails ? overlays.OpenPageWindows : [];
                foreach (var id in shots.Keys.Where(k => open.All(o => o.Id != k)).ToList()) shots.TryRemove(id, out _);
                foreach (var (id, window) in open)
                {
                    var png = await window.CaptureAsync();
                    if (png is null) continue;
                    if (shots.TryGetValue(id, out var old) && old.Png.AsSpan().SequenceEqual(png)) continue;
                    shots[id] = (png, old.Ver + 1);
                }
            }
            finally { capturing = false; }
        }

        private object[] BuildWindows(HawkeyeSettings s)
        {
            if (!s.StreamShowDetails) return [];
            return overlays.OpenPageWindows
                .Where(o => o.Window.IsVisible && shots.ContainsKey(o.Id))
                .Select(o => (object)new
                {
                    id = o.Id,
                    left = o.Window.Left,
                    top = o.Window.Top,
                    width = o.Window.ActualWidth,
                    height = o.Window.ActualHeight,
                    ver = shots[o.Id].Ver,
                })
                .ToArray();
        }

        public void Dispose()
        {
            timer.Stop();
            captureTimer.Stop();
            StopListener();
        }
    }
}
