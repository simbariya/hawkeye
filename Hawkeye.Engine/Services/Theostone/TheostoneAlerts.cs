using Hawkeye.Engine.Services.Entity;
using Hawkeye.Engine.Settings;

namespace Hawkeye.Engine.Services.Theostone
{
    /// <summary>One Theostone alert.</summary>
    /// <param name="Yours">True: your Theostone procced. False: someone's Theostone hit you.</param>
    /// <param name="Name">The Theostone without the "Theostone: " prefix, e.g. "Tassin's Command".</param>
    /// <param name="From">Who it came from when it hit you (empty if not known).</param>
    /// <param name="Hits">Damage hits merged into this alert (a multi-hit proc is one alert).</param>
    /// <param name="Until">A debuff on you: shown until this time or until the game removes it (null = a plain hit).</param>
    /// <param name="Instance">The game's number for that debuff, used by its removal message.</param>
    public sealed record TheostoneProc(DateTime At, bool Yours, string Name, long Damage, string From, int Hits,
        DateTime? Until = null, int Instance = 0, TimeSpan Duration = default)
    {
        public bool IsDebuff => Until is not null;
    }

    /// <summary>
    /// Theostone alerts (owner, 2026-11-22: "a subtle pop up when Theostone procs ... and same when it procs on me";
    /// "godstones that do a negative debuff, like blind for 6 sec, should remain as a popup until the effect is gone,
    /// either naturally or by potting it").
    ///
    /// - Procs are ordinary damage hits (0x3804) with a Theostone skill code: <see cref="OnHit"/>.
    /// - A Theostone debuff on you comes as a buff message (0x382A, type 19 in the owner's recording) with its
    ///   duration and an instance number: <see cref="OnDebuff"/>. It stays until the duration runs out or the game's
    ///   removal message (0x382C) names that instance: <see cref="OnRemoved"/>. Recorded 2026-11-21: a 3 s debuff
    ///   removed at 3.03 s (expired), a 20 s one at 9.4 s and a 4 s one at 1.4 s (cleansed).
    /// </summary>
    public sealed class TheostoneAlerts
    {
        public static readonly TimeSpan MergeWithin = TimeSpan.FromSeconds(1.5);
        private const int MaxShown = 4;

        private readonly EntityTracker entities;
        private readonly ISettingsService settings;
        private readonly Lock gate = new();
        private readonly List<TheostoneProc> procs = new();

        public event EventHandler? Changed;

        /// <summary>The time source; tests set their own.</summary>
        public Func<DateTime> Clock { get; init; } = () => DateTime.Now;

        /// <summary>How long a plain hit stays (Settings, 2 to 6 s).</summary>
        public TimeSpan ShowFor => TimeSpan.FromSeconds(Math.Clamp(settings.Current.TheostoneSeconds, 1, 10));

        public TheostoneAlerts(EntityTracker entities, ISettingsService settings)
        {
            this.entities = entities;
            this.settings = settings;
        }

        private static string Short(string skillName) =>
            skillName.StartsWith("Theostone: ", StringComparison.Ordinal) ? skillName["Theostone: ".Length..] : skillName;

        /// <summary>A Theostone hit: alerts only if it is yours or it hit you (and that alert is switched on).</summary>
        public void OnHit(int actorId, int targetId, string skillName, long damage)
        {
            bool yours = entities.IsCurrentPlayer(actorId);
            bool onYou = !yours && entities.IsCurrentPlayer(targetId);
            if (yours ? !settings.Current.TheostoneAlertsYours : !(onYou && settings.Current.TheostoneAlertsOnYou)) return;

            string name = Short(skillName);
            string from = yours ? "" : entities.GetPlayerEntity(actorId)?.Name ?? "";
            lock (gate)
            {
                var now = Clock();
                Expire(now);
                // Same stone, same direction, just now (a multi-hit), or a debuff from it still on you: one alert.
                int i = procs.FindLastIndex(p => p.Yours == yours && p.Name == name && (p.IsDebuff || now - p.At < MergeWithin));
                if (i >= 0)
                    procs[i] = procs[i] with
                    {
                        At = procs[i].IsDebuff ? procs[i].At : now,
                        Damage = procs[i].Damage + damage,
                        Hits = procs[i].Hits + 1,
                        From = procs[i].From.Length > 0 ? procs[i].From : from,
                    };
                else Add(new TheostoneProc(now, yours, name, damage, from, 1));
            }
            Changed?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>A Theostone debuff landed on someone; alerts if it is you, until it ends.</summary>
        public void OnDebuff(int targetId, int instance, string skillName, uint durationMs, int casterId)
        {
            if (!settings.Current.TheostoneAlertsOnYou || !entities.IsCurrentPlayer(targetId)) return;
            string name = Short(skillName);
            string from = entities.GetPlayerEntity(casterId)?.Name ?? "";
            lock (gate)
            {
                var now = Clock();
                Expire(now);
                var length = TimeSpan.FromMilliseconds(durationMs);
                // The hit that came with it a moment ago turns into the debuff alert.
                int i = procs.FindLastIndex(p => !p.Yours && p.Name == name && (p.IsDebuff || now - p.At < MergeWithin));
                if (i >= 0)
                    procs[i] = procs[i] with { At = now, Until = now + length, Instance = instance, Duration = length,
                        From = procs[i].From.Length > 0 ? procs[i].From : from };
                else Add(new TheostoneProc(now, false, name, 0, from, 0, now + length, instance, length));
            }
            Changed?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// The game's removal message (0x382C) for you: <paramref name="data"/> holds the removed instance numbers as
        /// varints from <paramref name="from"/> on. Any debuff alert whose instance is in there ends now.
        /// </summary>
        public void OnRemoved(int entityId, byte[] data, int from)
        {
            if (!entities.IsCurrentPlayer(entityId)) return;
            bool changed = false;
            lock (gate)
            {
                for (int i = procs.Count - 1; i >= 0; i--)
                {
                    if (!procs[i].IsDebuff || procs[i].Instance == 0 || !ContainsVarint(data, from, procs[i].Instance)) continue;
                    procs.RemoveAt(i);
                    changed = true;
                }
            }
            if (changed) Changed?.Invoke(this, EventArgs.Empty);
        }

        private static bool ContainsVarint(byte[] data, int from, int value)
        {
            Span<byte> v = stackalloc byte[5];
            int n = 0;
            uint x = (uint)value;
            do { byte b = (byte)(x & 0x7F); x >>= 7; if (x != 0) b |= 0x80; v[n++] = b; } while (x != 0);
            var needle = v[..n];
            for (int i = Math.Max(0, from); i + n <= data.Length; i++)
                // Each entry is [reason][instance]: the byte before must be a small reason code (0 ran out, 7 cleansed).
                if (data.AsSpan(i, n).SequenceEqual(needle) && i > 0 && data[i - 1] <= 0x1F) return true;
            return false;
        }

        private void Add(TheostoneProc p)
        {
            procs.Add(p);
            while (procs.Count > MaxShown)
            {
                int plain = procs.FindIndex(x => !x.IsDebuff);   // plain hits make room first
                procs.RemoveAt(plain >= 0 ? plain : 0);
            }
        }

        private void Expire(DateTime now) =>
            procs.RemoveAll(p => p.IsDebuff ? now >= p.Until : now - p.At >= ShowFor);

        /// <summary>Alerts on screen now, oldest first.</summary>
        public IReadOnlyList<TheostoneProc> Showing
        {
            get
            {
                lock (gate)
                {
                    Expire(Clock());
                    return procs.ToList();
                }
            }
        }

        /// <summary>Time left on a debuff alert (zero for a plain hit).</summary>
        public TimeSpan TimeLeft(TheostoneProc p) => p.Until is { } until ? (until - Clock()) is var left && left > TimeSpan.Zero ? left : TimeSpan.Zero : TimeSpan.Zero;
    }
}
