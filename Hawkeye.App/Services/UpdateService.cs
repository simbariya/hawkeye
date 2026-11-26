using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using Hawkeye.Engine.Services.Updates;
using Hawkeye.Engine.Settings;
using Microsoft.Extensions.Logging;

namespace Hawkeye.App.Services
{
    /// <summary>
    /// Auto-update from GitHub releases (owner, 2026-11-21). Checks 20 s after start and every 30 minutes while open (6 hours until v0.2.16; owner: "periodically checks for updates"); if a newer release
    /// has the zip matching this copy (full or small), downloads and unpacks it to %LocalAppData%\Hawkeye\updates.
    /// Then "Update x.y.z ready" shows on the meter: clicking it restarts into the new version; closing Hawkeye
    /// installs it for next time. Installing = a small batch file that waits for Hawkeye to exit, copies the new
    /// files over the install folder (robocopy) and optionally starts it again. Settings, history and Odyle live in
    /// %AppData%\Hawkeye and are never touched. Off in Debug builds (they are not release installs).
    /// </summary>
    public sealed class UpdateService : IDisposable
    {
#if SELF_CONTAINED
        public const bool SelfContained = true;
#else
        public const bool SelfContained = false;
#endif
        private static readonly TimeSpan CheckEvery = TimeSpan.FromMinutes(30);   // GitHub allows 60 unsigned checks an hour

        private readonly ISettingsService settings;
        private readonly ILogger<UpdateService> logger;
        private static readonly string updatesDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Hawkeye", "updates");
        private Timer? timer;
        private int busy;
        private bool launched;

        /// <summary>
        /// Set for a minute after Hawkeye starts on a version the updater just installed: the meter shows
        /// "Updated to x.y.z". Detected by the updater's download for THIS version still sitting in the updates
        /// folder (it is cleaned right after), so it also works when coming from 0.2.7, which recorded nothing else.
        /// </summary>
        public Version? JustUpdatedTo => DateTime.UtcNow < justUpdatedUntil ? CurrentVersion : null;
        private DateTime justUpdatedUntil = DateTime.MinValue;

        /// <summary>The version downloaded and ready to install, or null.</summary>
        public Version? ReadyVersion { get; private set; }
        private string? readyFolder;

        public event EventHandler? Changed;

        public UpdateService(ISettingsService settings, ILogger<UpdateService> logger)
        {
            this.settings = settings;
            this.logger = logger;
        }

        public static Version CurrentVersion => ReleaseInfo.ParseVersion(Branding.Version) ?? new Version(0, 0, 0);

        /// <summary>True right after the updater installed this version (its download folder is still there).</summary>
        public static bool JustInstalled => Directory.Exists(Path.Combine(updatesDir, CurrentVersion.ToString(3)));

        public void Start()
        {
            if (JustInstalled)
            {
                justUpdatedUntil = DateTime.UtcNow.AddMinutes(1);
                logger.LogInformation("[UPDATE] running {Version}, just installed by the updater", CurrentVersion);
            }
#if DEBUG
            logger.LogInformation("[UPDATE] Debug build: auto-update off");
            return;
#elif LOCAL_BUILD
            // A build made on one PC with local add-ons: a release would replace it with the public build.
            logger.LogInformation("[UPDATE] Local build: auto-update off");
            return;
#else
            CleanOldDownloads();
            timer = new Timer(_ => _ = CheckAsync(), null, TimeSpan.FromSeconds(20), CheckEvery);
#endif
        }

        public async Task CheckAsync()
        {
            if (!settings.Current.AutoUpdate || launched) return;
            if (Interlocked.Exchange(ref busy, 1) == 1) return;
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
                http.DefaultRequestHeaders.UserAgent.ParseAdd("Hawkeye-Updater");
                http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
                var json = await http.GetStringAsync(ReleaseInfo.LatestUrl);
                var release = ReleaseInfo.FromGitHubJson(json);
                if (release is null || !release.IsUpdateFor(CurrentVersion, SelfContained)) return;
                if (ReadyVersion is { } ready && release.Version <= ready) return;   // already downloaded; a newer one replaces it

                var asset = ReleaseInfo.AssetName(release.Version, SelfContained);
                var target = Path.Combine(updatesDir, release.Version.ToString(3));
                var zip = target + ".zip";
                Directory.CreateDirectory(updatesDir);
                logger.LogInformation("[UPDATE] {Version} found, downloading {Asset}", release.Version, asset);

                await using (var src = await http.GetStreamAsync(release.Assets[asset]))
                await using (var dst = File.Create(zip))
                    await src.CopyToAsync(dst);

                if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
                ZipFile.ExtractToDirectory(zip, target);
                File.Delete(zip);

                // The zip holds a "Hawkeye" folder with Hawkeye.exe in it.
                var folder = Path.Combine(target, "Hawkeye");
                if (!File.Exists(Path.Combine(folder, "Hawkeye.exe")))
                {
                    logger.LogWarning("[UPDATE] {Version}: no Hawkeye.exe in the download; ignoring it", release.Version);
                    return;
                }

                readyFolder = folder;
                ReadyVersion = release.Version;
                logger.LogInformation("[UPDATE] {Version} ready", release.Version);
                Changed?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                logger.LogInformation("[UPDATE] check failed: {Message}", ex.Message); // offline etc.: try again later
            }
            finally
            {
                Interlocked.Exchange(ref busy, 0);
            }
        }

        /// <summary>Starts the installer helper; it waits for this process to exit. Call, then shut Hawkeye down.</summary>
        public bool LaunchInstaller(bool restart)
        {
            if (launched || readyFolder is null) return false;
            try
            {
                var install = AppContext.BaseDirectory.TrimEnd('\\', '/');
                var script = Path.Combine(updatesDir, "install-update.cmd");
                var lines = UpdateInstaller.Script(Environment.ProcessId, readyFolder, install, restart);
                File.WriteAllLines(script, lines);
                Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{script}\"")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WorkingDirectory = updatesDir,
                });
                launched = true;
                logger.LogInformation("[UPDATE] installing {Version} after exit (restart: {Restart})", ReadyVersion, restart);
                return true;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[UPDATE] could not start the installer");
                return false;
            }
        }

        /// <summary>Downloads for this version or older are done with: remove them.</summary>
        private void CleanOldDownloads()
        {
            try
            {
                if (!Directory.Exists(updatesDir)) return;
                foreach (var dir in Directory.GetDirectories(updatesDir))
                {
                    var v = ReleaseInfo.ParseVersion(Path.GetFileName(dir));
                    if (v is not null && v <= CurrentVersion) Directory.Delete(dir, recursive: true);
                }
            }
            catch { /* best effort */ }
        }

        public void Dispose() => timer?.Dispose();
    }
}
