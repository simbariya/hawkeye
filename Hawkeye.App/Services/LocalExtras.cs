using Serilog;

namespace Hawkeye.App.Services
{
    /// <summary>
    /// A local add-on: code compiled into a build made on one PC only, never part of this repo or a release.
    /// Public builds have none, so <see cref="LocalExtras"/> does nothing there.
    /// </summary>
    public interface ILocalExtra
    {
        /// <summary>Called once on the UI thread after Hawkeye has started.</summary>
        void Start(IServiceProvider services);

        /// <summary>Called as Hawkeye closes.</summary>
        void Stop();

        /// <summary>A Blazor component shown at the bottom of the Extras window, or null.</summary>
        Type? ExtrasSection { get; }
    }

    /// <summary>Finds and runs the <see cref="ILocalExtra"/> classes compiled into this build (none in public builds).</summary>
    public static class LocalExtras
    {
        public static IReadOnlyList<ILocalExtra> All { get; } = Find();

        public static bool IsLocalBuild =>
#if LOCAL_BUILD
            true;
#else
            false;
#endif

        private static List<ILocalExtra> Find()
        {
            var list = new List<ILocalExtra>();
            foreach (var t in typeof(LocalExtras).Assembly.GetTypes())
            {
                if (t.IsAbstract || t.IsInterface || !typeof(ILocalExtra).IsAssignableFrom(t) || t.GetConstructor(Type.EmptyTypes) is null) continue;
                try { list.Add((ILocalExtra)Activator.CreateInstance(t)!); }
                catch (Exception ex) { Log.Error(ex, "Local add-on {Type} could not be made", t.Name); }
            }
            return list;
        }

        public static void Start(IServiceProvider services)
        {
            foreach (var x in All)
            {
                try { x.Start(services); Log.Information("Local add-on {Type} started", x.GetType().Name); }
                catch (Exception ex) { Log.Error(ex, "Local add-on {Type} failed to start", x.GetType().Name); }
            }
        }

        public static void Stop()
        {
            foreach (var x in All)
            {
                try { x.Stop(); } catch (Exception ex) { Log.Warning(ex, "Local add-on {Type} failed to stop", x.GetType().Name); }
            }
        }
    }
}
