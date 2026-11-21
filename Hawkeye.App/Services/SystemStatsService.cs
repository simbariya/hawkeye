using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Hawkeye.Engine.Services.Session;
using Hawkeye.Engine.Settings;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;
using Microsoft.Extensions.Logging;

namespace Hawkeye.App.Services
{
    /// <summary>
    /// The numbers on the meter's status line, refreshed once a second:
    ///   ping  - from the game's own server-time messages (decoded by the engine)
    ///   FPS   - frames the game hands to Windows, counted from Windows' DXGI event stream (the same source
    ///           PresentMon uses). Nothing touches the game process. Windows only allows this for programs
    ///           running as administrator, so without that FPS shows "admin".
    ///   CPU   - whole-system CPU use
    ///   RAM   - whole-system memory in use
    /// </summary>
    public sealed class SystemStatsService : IDisposable
    {
        private const string GameProcessName = "AION2";
        private const string EtwSessionName = "Hawkeye-FrameCounter";
        private static readonly Guid DxgiProvider = new("CA11C036-0102-4A2D-A6AD-F03CFED5D3C9");
        private const int DxgiPresentStart = 42;
        private const int DxgiPresentMultiplaneOverlayStart = 55;

        private readonly ISettingsService settings;
        private readonly ILogger<SystemStatsService> logger;
        private readonly Timer timer;
        private readonly ConcurrentDictionary<int, bool> isGamePid = new();

        private TraceEventSession? etw;
        private long presents;
        private long lastIdle, lastKernel, lastUser;

        public int? PingMs { get; private set; }
        public double? Fps { get; private set; }
        public bool FpsNeedsAdmin { get; private set; }
        public double CpuPercent { get; private set; }
        public double RamUsedGb { get; private set; }
        public double RamTotalGb { get; private set; }

        public SystemStatsService(ISettingsService settings, CombatSessionManager sessions, ILogger<SystemStatsService> logger)
        {
            this.settings = settings;
            this.logger = logger;
            sessions.PingUpdated += (_, ms) => PingMs = ms;
            settings.Changed += (_, _) => UpdateFrameCounter();

            SampleCpu(); // prime the deltas
            UpdateFrameCounter();
            timer = new Timer(_ => Tick(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        }

        private void Tick()
        {
            try
            {
                CpuPercent = SampleCpu();

                var mem = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
                if (GlobalMemoryStatusEx(ref mem))
                {
                    RamTotalGb = mem.ullTotalPhys / 1073741824.0;
                    RamUsedGb = (mem.ullTotalPhys - mem.ullAvailPhys) / 1073741824.0;
                }

                if (etw is not null)
                {
                    long count = Interlocked.Exchange(ref presents, 0);
                    Fps = GameIsRunning() ? count : null;
                }
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "System stats sample failed");
            }
        }

        private double SampleCpu()
        {
            if (!GetSystemTimes(out var idleFt, out var kernelFt, out var userFt)) return CpuPercent;
            long idle = idleFt.ToLong(), kernel = kernelFt.ToLong(), user = userFt.ToLong();
            long idleDelta = idle - lastIdle, totalDelta = (kernel - lastKernel) + (user - lastUser); // kernel time includes idle
            lastIdle = idle; lastKernel = kernel; lastUser = user;
            return totalDelta > 0 ? Math.Clamp(100.0 * (totalDelta - idleDelta) / totalDelta, 0, 100) : 0;
        }

        // ------------------------------------------------------------------ FPS

        private void UpdateFrameCounter()
        {
            bool wanted = settings.Current.ShowFps;
            if (wanted && etw is null) StartFrameCounter();
            else if (!wanted && etw is not null) StopFrameCounter();
        }

        private void StartFrameCounter()
        {
            if (TraceEventSession.IsElevated() != true)
            {
                FpsNeedsAdmin = true;
                Fps = null;
                return;
            }

            try
            {
                var session = new TraceEventSession(EtwSessionName) { StopOnDispose = true };
                session.EnableProvider(DxgiProvider, TraceEventLevel.Informational);
                session.Source.AllEvents += OnEtwEvent;
                etw = session;
                FpsNeedsAdmin = false;
                Task.Factory.StartNew(() =>
                {
                    try { session.Source.Process(); }
                    catch (Exception ex) { logger.LogWarning(ex, "FPS counter stopped"); }
                }, TaskCreationOptions.LongRunning);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not start the FPS counter");
                FpsNeedsAdmin = ex is UnauthorizedAccessException;
                etw = null;
            }
        }

        private void StopFrameCounter()
        {
            etw?.Dispose();
            etw = null;
            Fps = null;
        }

        private void OnEtwEvent(TraceEvent e)
        {
            int id = (int)e.ID;
            if (id != DxgiPresentStart && id != DxgiPresentMultiplaneOverlayStart) return;
            if (IsGame(e.ProcessID)) Interlocked.Increment(ref presents);
        }

        private bool IsGame(int pid) =>
            isGamePid.GetOrAdd(pid, p =>
            {
                try { return string.Equals(Process.GetProcessById(p).ProcessName, GameProcessName, StringComparison.OrdinalIgnoreCase); }
                catch { return false; }
            });

        private static bool GameIsRunning() => Process.GetProcessesByName(GameProcessName).Length > 0;

        public void Dispose()
        {
            timer.Dispose();
            StopFrameCounter();
        }

        // ------------------------------------------------------------------ Win32

        [StructLayout(LayoutKind.Sequential)]
        private struct FILETIME
        {
            public uint Low, High;
            public readonly long ToLong() => ((long)High << 32) | Low;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MEMORYSTATUSEX
        {
            public uint dwLength, dwMemoryLoad;
            public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetSystemTimes(out FILETIME idle, out FILETIME kernel, out FILETIME user);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX buffer);
    }
}
