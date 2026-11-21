using Hawkeye.Engine.Services.Entity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hawkeye.Engine.Services.Pvp
{
    /// <summary>
    /// PvP mode (owner, 2026-11-21): kills, deaths, assists, AP gained and rank points gained since you switched it
    /// on (or last cleared it). The AP and rank totals are followed all the time, so the first change after
    /// switching on already counts.
    ///
    /// Assists are inferred, not read: an enemy dies to someone else's hand and your AP goes up within
    /// <see cref="AssistWindow"/> (you got a share). Kills and deaths are read straight from the death message.
    /// </summary>
    public sealed class PvpTracker
    {
        public static readonly TimeSpan AssistWindow = TimeSpan.FromSeconds(3);

        private readonly EntityTracker entities;
        private readonly ILogger logger;
        private readonly Lock gate = new();
        private long? lastAp, lastRank;
        private DateTime lastOwnKill = DateTime.MinValue;
        private readonly List<DateTime> othersKills = new();

        public bool Active { get; private set; }
        public DateTime StartedAt { get; private set; }
        public int Kills { get; private set; }
        public int Deaths { get; private set; }
        public int Assists { get; private set; }
        public long ApGained { get; private set; }
        public long RankGained { get; private set; }

        public event EventHandler? Changed;

        /// <summary>The time source; tests set their own.</summary>
        public Func<DateTime> Clock { get; init; } = () => DateTime.Now;
        private DateTime now() => Clock();

        // ONE public constructor on purpose: with two that the app's service setup could both fill, it cannot pick
        // and the meter window fails to build (0.2.9 and 0.2.10 shipped like that, I15).
        public PvpTracker(EntityTracker entities, ILogger<PvpTracker>? logger = null)
        {
            this.entities = entities;
            this.logger = (ILogger?)logger ?? NullLogger.Instance;
        }

        /// <summary>Switch PvP mode on or off. The numbers start from zero the first time and on <see cref="Reset"/>.</summary>
        public void SetActive(bool on)
        {
            lock (gate)
            {
                if (on && StartedAt == default) StartedAt = now();
                Active = on;
            }
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public void Reset()
        {
            lock (gate)
            {
                Kills = Deaths = Assists = 0;
                ApGained = RankGained = 0;
                StartedAt = now();
                othersKills.Clear();
            }
            Changed?.Invoke(this, EventArgs.Empty);
        }

        private bool IsMe(int entityId, string name) =>
            (entityId != 0 && entities.IsCurrentPlayer(entityId))
            || (!string.IsNullOrEmpty(name) && name == entities.CurrentUserName);

        public void OnDeath(PvpParser.Death death)
        {
            bool changed = false;
            lock (gate)
            {
                var t = now();
                if (entities.IsCurrentPlayer(death.Victim))
                {
                    if (Active) { Deaths++; changed = true; }
                    logger.LogInformation("[PVP] you died (killer {Killer})", death.KillerName);
                }
                else if (death.Killer != 0 && IsMe(death.Killer, death.KillerName))
                {
                    lastOwnKill = t;
                    if (Active) { Kills++; changed = true; }
                    logger.LogInformation("[PVP] kill (victim entity {Victim})", death.Victim);
                }
                else if (death.Killer != 0)
                {
                    othersKills.Add(t);                // an assist if your AP rises right after
                    othersKills.RemoveAll(x => t - x > AssistWindow);
                }
            }
            if (changed) Changed?.Invoke(this, EventArgs.Empty);
        }

        public void OnApTotal(long total)
        {
            bool changed = false;
            lock (gate)
            {
                var t = now();
                if (lastAp is { } before && total > before)
                {
                    if (Active)
                    {
                        ApGained += total - before;
                        changed = true;
                        // Not your kill, but someone else's kill a moment ago: you helped.
                        bool ownKillJustNow = t - lastOwnKill <= AssistWindow;
                        int idx = othersKills.FindIndex(x => t - x <= AssistWindow);
                        if (!ownKillJustNow && idx >= 0)
                        {
                            othersKills.RemoveAt(idx);
                            Assists++;
                            logger.LogInformation("[PVP] assist (+{Ap} AP)", total - before);
                        }
                    }
                }
                lastAp = total;
            }
            if (changed) Changed?.Invoke(this, EventArgs.Empty);
        }

        public void OnRankTotal(long total)
        {
            bool changed = false;
            lock (gate)
            {
                if (lastRank is { } before && total != before && Active)
                {
                    RankGained += total - before;
                    changed = true;
                }
                lastRank = total;
            }
            if (changed) Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
