using Hawkeye.Engine.Services.Entity;
using Hawkeye.Engine.Settings;

namespace Hawkeye.Engine.Services.Theostone
{
    /// <summary>One Theostone proc to flash on the meter.</summary>
    /// <param name="Yours">True: your Theostone procced. False: someone's Theostone hit you.</param>
    /// <param name="Name">The Theostone without the "Theostone: " prefix, e.g. "Tassin's Command".</param>
    /// <param name="From">Who it came from when it hit you (empty if not known).</param>
    /// <param name="Hits">Hits merged into this alert (a multi-hit proc is one alert).</param>
    public sealed record TheostoneProc(DateTime At, bool Yours, string Name, long Damage, string From, int Hits);

    /// <summary>
    /// Theostone alerts (owner, 2026-11-22: "a subtle pop up when Theostone procs ... and same when it procs on me").
    /// Theostone procs arrive as ordinary damage hits (0x3804) with a Theostone skill code (93 of them in skills.json,
    /// 30000171 to 30012371; Hawkeye already credits them to the player who wears the stone). Fed by the damage
    /// processor; the meter shows <see cref="Showing"/> for <see cref="ShowFor"/> each.
    /// </summary>
    public sealed class TheostoneAlerts
    {
        public static readonly TimeSpan ShowFor = TimeSpan.FromSeconds(3.5);
        public static readonly TimeSpan MergeWithin = TimeSpan.FromSeconds(1.5);
        private const int MaxShown = 3;

        private readonly EntityTracker entities;
        private readonly ISettingsService settings;
        private readonly Lock gate = new();
        private readonly List<TheostoneProc> procs = new();

        public event EventHandler? Changed;

        /// <summary>The time source; tests set their own.</summary>
        public Func<DateTime> Clock { get; init; } = () => DateTime.Now;

        public TheostoneAlerts(EntityTracker entities, ISettingsService settings)
        {
            this.entities = entities;
            this.settings = settings;
        }

        /// <summary>A Theostone hit: alerts only if it is yours or it hit you (and that alert is switched on).</summary>
        public void OnHit(int actorId, int targetId, string skillName, long damage)
        {
            bool yours = entities.IsCurrentPlayer(actorId);
            bool onYou = !yours && entities.IsCurrentPlayer(targetId);
            if (yours ? !settings.Current.TheostoneAlertsYours : !(onYou && settings.Current.TheostoneAlertsOnYou)) return;

            string name = skillName.StartsWith("Theostone: ", StringComparison.Ordinal) ? skillName["Theostone: ".Length..] : skillName;
            string from = yours ? "" : entities.GetPlayerEntity(actorId)?.Name ?? "";
            lock (gate)
            {
                var now = Clock();
                procs.RemoveAll(p => now - p.At >= ShowFor);
                int i = procs.FindLastIndex(p => p.Yours == yours && p.Name == name && now - p.At < MergeWithin);
                if (i >= 0)
                    procs[i] = procs[i] with { At = now, Damage = procs[i].Damage + damage, Hits = procs[i].Hits + 1 };
                else
                {
                    procs.Add(new TheostoneProc(now, yours, name, damage, from, 1));
                    if (procs.Count > MaxShown) procs.RemoveAt(0);
                }
            }
            Changed?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Alerts still on screen, oldest first.</summary>
        public IReadOnlyList<TheostoneProc> Showing
        {
            get
            {
                lock (gate)
                {
                    var now = Clock();
                    return procs.Where(p => now - p.At < ShowFor).ToList();
                }
            }
        }
    }
}
