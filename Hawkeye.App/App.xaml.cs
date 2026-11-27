using System.IO;
using System.Windows;
using Hawkeye.App.Services;
using Hawkeye.App.Services.Windowing;
using Hawkeye.App.Views;
using Hawkeye.Engine.Extensions;
using Hawkeye.Engine.Models;
using Hawkeye.Engine.PacketCapture;
using Hawkeye.Engine.Services;
using Hawkeye.Engine.Services.Entity;
using Hawkeye.Engine.Services.History;
using Hawkeye.Engine.Services.Session;
using Hawkeye.Engine.Services.Timed;
using Hawkeye.Engine.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace Hawkeye.App
{
    /// <summary>
    /// Start-up: wire everything together, start listening to the game's traffic, show the meter.
    /// Logs go to %AppData%\Hawkeye\logs, settings to %AppData%\Hawkeye\settings.json.
    /// </summary>
    public partial class App : Application
    {
        public static IHost AppHost { get; private set; } = null!;

        public App()
        {
            // Keep the overlay web views rendering even when the game window covers them.
            Environment.SetEnvironmentVariable("WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS",
                "--disable-features=CalculateNativeWinOcclusion,IntensiveWakeUpThrottling --disable-backgrounding-occluded-windows --disable-renderer-backgrounding");

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .WriteTo.File(Path.Combine(SettingsService.DataDirectory, "logs", "hawkeye-.txt"),
                    rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {SourceContext}: {Message}{NewLine}{Exception}")
                .CreateLogger();

            AppHost = Host.CreateDefaultBuilder()
                .UseSerilog()
                .ConfigureServices(services =>
                {
                    services.AddSingleton<ISettingsService, SettingsService>();

                    // Engine: capture -> stream rebuild -> decode -> combat sessions
                    services.AddSingleton<FilePacketWriter>();
                    services.AddSingleton<TcpStreamBuffer>();
                    services.AddSingleton<IPacketCaptureDevice, CaptureDevice>();
                    services.AddSingleton(sp => new EntityTracker
                    {
                        Dummies = new KnownDummies(Path.Combine(Hawkeye.Engine.Settings.SettingsService.DataDirectory, "known-dummies.json"),
                            sp.GetRequiredService<Microsoft.Extensions.Logging.ILoggerFactory>().CreateLogger("Hawkeye.Dummies")),
                    });
                    services.AddKeyedSingleton<ITimedEventTracker, TimedEventTracker>(TimerKeys.Cooldowns);
                    services.AddKeyedSingleton<ITimedEventTracker, TimedEventTracker>(TimerKeys.Buffs);
                    services.AddSingleton<IEncounterHistory, EncounterHistory>();
                    services.AddSingleton<CombatSessionManager>();
                    services.AddSingleton<Hawkeye.Engine.Services.Odyle.OdyleTracker>();
                    services.AddSingleton<Hawkeye.Engine.Services.Pvp.PvpTracker>();
                    services.AddSingleton<Hawkeye.Engine.Services.Arena.ArenaTracker>();
                    services.AddSingleton(sp => new Hawkeye.Engine.Services.Skills.SkillLevels(
                        Hawkeye.Engine.Services.Skills.SkillLevels.DefaultFilePath,
                        sp.GetRequiredService<Microsoft.Extensions.Logging.ILoggerFactory>().CreateLogger("Hawkeye.Skills")));
                    services.AddSingleton<Hawkeye.Engine.Services.Theostone.TheostoneAlerts>();
                    services.AddSingleton<Hawkeye.Engine.Services.Pvp.PvpHistory>();
                    services.AddSingleton<Hawkeye.Engine.Services.Arena.ArenaHistory>();
                    services.AddSingleton<Hawkeye.Engine.Services.Pvp.PvpTotalsStore>();
                    services.AddSingleton<PvpView>();
                    services.AddPacketProcessingRouting();
                    services.AddSingleton<IPacketService, PacketPipelineService>();

                    // App
                    services.AddSingleton<SystemStatsService>();
                    services.AddSingleton<DetailsState>();
                    services.AddSingleton<CompareState>();
                    services.AddSingleton<MeterSource>();
                    services.AddSingleton<UpdateService>();
                    services.AddSingleton<Hawkeye.App.Services.Streaming.StreamServer>();
                    services.AddSingleton<OverlayManager>();
                    services.AddSingleton<MainWindow>();
                    services.AddWpfBlazorWebView();
                })
                .Build();
        }

        protected override async void OnStartup(StartupEventArgs e)
        {
            // Safety net: log anything unexpected on the UI thread and keep running rather than vanish
            // mid-fight. Errors still land in the log at ERR level so they get fixed, not hidden.
            DispatcherUnhandledException += (_, args) =>
            {
                Log.Error(args.Exception, "Unhandled UI error (kept running)");
                args.Handled = true;
            };
            AppDomain.CurrentDomain.UnhandledException += (_, args) =>
                Log.Fatal(args.ExceptionObject as Exception, "Unhandled error, Hawkeye is closing");
            TaskScheduler.UnobservedTaskException += (_, args) =>
            {
                Log.Error(args.Exception, "Unobserved background task error");
                args.SetObserved();
            };

            base.OnStartup(e);
            // Just restarted by the updater: the old copy's WebView2 helpers may still hold the WebView2 data
            // folder for a moment, and a window made now comes up empty (I14). Older install scripts did not wait.
            if (UpdateService.JustInstalled) await Task.Delay(TimeSpan.FromSeconds(3));
            await AppHost.StartAsync();

            var services = AppHost.Services;
            // Every finished arena match is saved as it ends (v0.2.42), before the capture starts.
            var arenaHistory = services.GetRequiredService<Hawkeye.Engine.Services.Arena.ArenaHistory>();
            var you = services.GetRequiredService<Hawkeye.Engine.Services.Entity.EntityTracker>();
            services.GetRequiredService<Hawkeye.Engine.Services.Arena.ArenaTracker>().MatchEnded +=
                (_, m) => arenaHistory.Add(Hawkeye.Engine.Services.Arena.ArenaRecord.From(m, you.CurrentUserName));
            if (!TryStartCapture(services.GetRequiredService<IPacketService>()))
            {
                Shutdown();
                return;
            }

            var main = services.GetRequiredService<MainWindow>();
            MainWindow = main;
            main.Show();

            services.GetRequiredService<OverlayManager>().Start();
            services.GetRequiredService<Hawkeye.App.Services.Streaming.StreamServer>().Start();
            services.GetRequiredService<UpdateService>().Start();
            LocalExtras.Start(services);   // none in public builds
        }

        private static bool TryStartCapture(IPacketService packets)
        {
            try
            {
                packets.Start();
                return true;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Packet capture failed to start");
                MessageBox.Show(
                    "Hawkeye couldn't start listening to game traffic.\n\n" +
                    "It needs Npcap (https://npcap.com). Install it with\n" +
                    "\"Install Npcap in WinPcap API-compatible Mode\" ticked, then start Hawkeye again.",
                    "Hawkeye", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        protected override async void OnExit(ExitEventArgs e)
        {
            try
            {
                var services = AppHost.Services;
                LocalExtras.Stop();
                services.GetRequiredService<CombatSessionManager>().CompleteActiveSessions();
                // The fight just closed is written to History in the background: wait for it (and for the
                // Odyle list) before exiting, or it can be lost (found by CI 2026-11-21).
                Task.WaitAll([
                    services.GetRequiredService<Hawkeye.Engine.Services.History.IEncounterHistory>().FlushAsync(),
                    services.GetRequiredService<Hawkeye.Engine.Services.Odyle.OdyleTracker>().FlushAsync(),
                    services.GetRequiredService<Hawkeye.Engine.Services.Pvp.PvpHistory>().FlushAsync(),
                    services.GetRequiredService<Hawkeye.Engine.Services.Arena.ArenaHistory>().FlushAsync(),
                    services.GetRequiredService<Hawkeye.Engine.Services.Pvp.PvpTotalsStore>().FlushAsync(),
                ], TimeSpan.FromSeconds(3));
                services.GetRequiredService<IPacketService>().Stop();
                services.GetRequiredService<SystemStatsService>().Dispose();
                // A downloaded update installs as Hawkeye closes (no-op if a click already launched it).
                var updates = services.GetRequiredService<UpdateService>();
                if (updates.ReadyVersion is not null) updates.LaunchInstaller(restart: false);
                updates.Dispose();
                services.GetRequiredService<Hawkeye.App.Services.Streaming.StreamServer>().Dispose();
                await AppHost.StopAsync(TimeSpan.FromSeconds(2));
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Error during shutdown");
            }
            finally
            {
                await Log.CloseAndFlushAsync();
                base.OnExit(e);
            }
        }
    }
}
