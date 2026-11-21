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
    /// Assists are inferred, not read. The game sends your new AP total right after the death that earned it
    /// (within about 0.1 s in the owner's session), so each AP rise belongs to the latest player kill just before
    /// it: yours means it is that kill's AP; someone else's means you got a share, an assist. Most kills near you
    /// give you nothing and are not assists. Kills and deaths are read straight from the death message.
    /// (v0.2.11 tied any AP within 3 s of your own kill to that kill and missed an assist 0.4 s later.)
    ///
    /// Only players count. Abyss mobs carry the killer's name too and also give AP, so the victim must be a
    /// player the game announced (0x3645, kept in <see cref="EntityTracker"/>). In the owner's session this split
    /// 74 of his kills cleanly: 39 announced players (every kill he called out) and 35 never-announced mobs.
    /// </summary>
    public sealed class PvpTracker
    {
        public static readonly TimeSpan AssistWindow = TimeSpan.FromSeconds(1.5);

        private readonly EntityTracker entities;
        private readonly ILogger logger;
        private readonly Lock gate = new();
        private long? lastAp, lastRank;
        private sealed record KillEvent(DateTime At, bool Yours, bool VictimIsPlayer);
        private readonly List<KillEvent> recentKills = new();   // waiting for the AP they may bring

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
                recentKills.Clear();
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
                else if (death.Killer != 0)
                {
                    bool yours = IsMe(death.Killer, death.KillerName);
                    bool player = entities.IsIdentifiedPlayer(death.Victim);
                    if (yours && player)
                    {
                        if (Active) { Kills++; changed = true; }
                        logger.LogInformation("[PVP] kill (victim entity {Victim})", death.Victim);
                    }
                    recentKills.RemoveAll(k => t - k.At > AssistWindow);
                    recentKills.Add(new KillEvent(t, yours, player));
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
                    }
                    // The latest kill just before this AP is the one that paid it.
                    int idx = recentKills.FindLastIndex(k => t - k.At <= AssistWindow);
                    if (idx >= 0)
                    {
                        var cause = recentKills[idx];
                        recentKills.RemoveAt(idx);
                        if (!cause.Yours && cause.VictimIsPlayer)
                        {
                            if (Active) Assists++;
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
