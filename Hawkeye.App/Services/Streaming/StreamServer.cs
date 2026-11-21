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
        private readonly DetailsState details;
        private readonly DispatcherTimer timer;

        private HttpListener? listener;
        private int runningPort;
        private volatile string stateJson = "{}";

        /// <summary>Shown in Settings: "Running", or why it is not.</summary>
        public string Status { get; private set; } = "Off";
        public string Url => $"http://localhost:{settings.Current.StreamerPort}/";
        public event EventHandler? StatusChanged;

        public StreamServer(ISettingsService settings, MeterSource meter, SystemStatsService stats, OverlayManager overlays,
            [FromKeyedServices(TimerKeys.Cooldowns)] ITimedEventTracker cooldowns,
            [FromKeyedServices(TimerKeys.Buffs)] ITimedEventTracker buffs,
            DetailsState details,
            ILogger<StreamServer> logger)
        {
            this.details = details;
            this.settings = settings;
            this.meter = meter;
            this.stats = stats;
            this.overlays = overlays;
            this.cooldowns = cooldowns;
            this.buffs = buffs;
            this.logger = logger;
            timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            timer.Tick += (_, _) => BuildState();
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
                    meter = !s.StreamShowMeter || main is null ? null : new
                    {
                        left = main.Left,
                        top = main.Top,
                        width = main.ActualWidth,
                        opacity = s.Opacity,
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
                    details = BuildDetails(s),
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

        /// <summary>The combat analysis window as data, while it is open on screen; else null.</summary>
        private object? BuildDetails(HawkeyeSettings s)
        {
            if (!s.StreamShowDetails) return null;
            var window = overlays.GetPageWindow(typeof(Hawkeye.App.Pages.DetailsPage));
            if (window is null || !window.IsVisible || details.Snapshot is not { } enc) return null;
            var player = enc.Players.FirstOrDefault(p => p.PlayerId == details.PlayerId) ?? enc.Players.FirstOrDefault();
            if (player is null) return null;
            long topSkill = player.Skills.Count > 0 ? player.Skills.Max(k => k.TotalDamage) : 1;

            static string Rate(int part, int whole) => whole > 0 ? $"{Math.Round(100.0 * part / whole):0}%" : "-";
            static string N(double v) => Math.Round(v).ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
            static string Prop(int part, int whole) => $"{N(part)} ({Rate(part, whole)})";
            static string Short(double v) => MeterViewModel.Compact((long)Math.Round(v), shortThousands: true);

            return new
            {
                left = window.Left,
                top = window.Top,
                width = window.ActualWidth,
                height = window.ActualHeight,
                sub = $"{enc.TargetName} · {enc.StartedAt.ToString("dd MMM HH:mm", System.Globalization.CultureInfo.InvariantCulture)}" + (details.Live ? " · live" : ""),
                players = enc.Players.Select(p => new { name = p.Name, classId = p.ClassId, dps = Short(p.Dps), me = p.IsUser, on = p.PlayerId == player.PlayerId }),
                duration = TimeSpan.FromSeconds(enc.DurationSeconds).ToString(@"mm\:ss"),
                dps = N(player.Dps),
                total = N(player.TotalDamage),
                summary = new[]
                {
                    new[] { "Total Hits", N(player.Hits) },
                    new[] { "Share of the fight", player.SharePercent.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "%" },
                    new[] { "Critical Hits (Proportion)", Prop(player.Critical, player.Hits) },
                    new[] { "Perfect Hits (Proportion)", Prop(player.Perfect, player.Hits) },
                    new[] { "Double Hits (Proportion)", Prop(player.Double, player.Hits) },
                    new[] { "Multi-Hit (Proportion)", Prop(player.Multi, player.Hits) },
                    new[] { "Front Attack Hits (Proportion)", Prop(player.Front, player.Hits) },
                    new[] { "Back Attack Hits (Proportion)", Prop(player.Back, player.Hits) },
                    new[] { "Parry (Proportion)", Prop(player.Parry, player.Hits) },
                    new[] { "Deaths", player.Deaths.ToString(System.Globalization.CultureInfo.InvariantCulture) },
                },
                skills = player.Skills.Select(k => new
                {
                    name = k.Name + (k.IsDot ? " (DoT)" : ""),
                    icon = k.Icon,
                    bar = topSkill > 0 ? Math.Round(100.0 * k.TotalDamage / topSkill, 2) : 0,
                    cells = new[]
                    {
                        k.Hits.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        Rate(k.Critical, k.Hits), Rate(k.Perfect, k.Hits), Rate(k.Double, k.Hits), Rate(k.Front, k.Hits),
                        Rate(k.Back, k.Hits), Rate(k.Multi, k.Hits), Rate(k.Parry, k.Hits),
                    },
                    dps = Short(k.Dps),
                    avg = Short(k.Average),
                    max = Short(k.MaxHit),
                    total = $"{N(k.TotalDamage)} ({k.SharePercent.ToString("0", System.Globalization.CultureInfo.InvariantCulture)}%)",
                }),
            };
        }

        public void Dispose()
        {
            timer.Stop();
            StopListener();
        }
    }
}
