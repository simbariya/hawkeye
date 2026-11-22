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

        // A remembered total (from before Hawkeye started) is trusted only for a jump one PvP kill can explain.
        // Biggest seen in the owner's session: +5,250 AP and +11,000 rank for one kill, -280 rank per death.
        public const long MaxApJumpFromMemory = 12_000;
        public const long MaxRankJumpFromMemory = 25_000;
        public const long MaxRankDropFromMemory = 1_000;

        private readonly EntityTracker entities;
        private readonly PvpTotalsStore? totals;
        private readonly ILogger logger;
        private readonly Lock gate = new();
        private long? lastAp, lastRank;
        private sealed record KillEvent(DateTime At, bool Yours, bool VictimIsPlayer, int Victim);
        private readonly List<PvpEvent> events = new();
        // Server ids learned from death messages (only killers carry one). Victims of your kills and assists are
        // enemies, so they get the last enemy server seen (one enemy server in the whole 2026-11-21 session).
        private int ownServer, enemyServer;
        private readonly List<KillEvent> recentKills = new();   // waiting for the AP they may bring

        // The last few login inventories (0x5611): your AP total at login, read when the first AP change names its key.
        private readonly List<byte[]> inventories = new();
        private const int InventoriesKept = 4;

        // Rankings (2026-11-22): your own line per ranking (0 = overall, other = your class) and the last table of each.
        private readonly Dictionary<int, RankRow> ownRanks = new();
        private readonly Dictionary<int, RankList> rankTables = new();
        private DateTime? nextRankUpdate;
        private RankStanding? standing;      // worked out again only after a change (the meter asks 30 times a second)
        private bool standingStale = true;

        public bool Active { get; private set; }
        public DateTime StartedAt { get; private set; }
        public int Kills { get; private set; }
        public int Deaths { get; private set; }
        public int Assists { get; private set; }
        public long ApGained { get; private set; }
        public long RankGained { get; private set; }

        /// <summary>Who this session's kills, deaths and assists were about, oldest first.</summary>
        public IReadOnlyList<PvpEvent> Events { get { lock (gate) return events.ToList(); } }

        public event EventHandler? Changed;

        /// <summary>The time source; tests set their own.</summary>
        public Func<DateTime> Clock { get; init; } = () => DateTime.Now;
        private DateTime now() => Clock();

        // ONE public constructor on purpose: with two that the app's service setup could both fill, it cannot pick
        // and the meter window fails to build (0.2.9 and 0.2.10 shipped like that, I15).
        public PvpTracker(EntityTracker entities, ILogger<PvpTracker>? logger = null, PvpTotalsStore? totals = null)
        {
            this.entities = entities;
            this.totals = totals;
            this.logger = (ILogger?)logger ?? NullLogger.Instance;
            entities.CurrentUserNamed += OnCharacterNamed;
            LoadRanking(null);   // the last character played, until the game names who you are
        }

        // The ranking remembered for this character (null = the last one played), shown until the game sends a new one.
        private void LoadRanking(string? character)
        {
            var saved = totals?.Get(character);
            lock (gate)
            {
                ownRanks.Clear();
                rankTables.Clear();
                nextRankUpdate = null;
                if (saved?.OwnRanks is { Count: > 0 } ranks)
                {
                    foreach (var (category, row) in ranks) ownRanks[category] = row;
                    if (saved.RankTable is { } table) rankTables[table.Category] = table;
                    nextRankUpdate = saved.RankNextUpdate;
                }
                standingStale = true;
            }
        }

        private void SaveRanking()
        {
            if (totals is null) return;
            Dictionary<int, RankRow> ranks;
            RankList? table;
            DateTime? next;
            lock (gate)
            {
                ranks = new(ownRanks);
                int category = ownRanks.Keys.Where(c => c != 0).DefaultIfEmpty(0).Max();
                table = rankTables.GetValueOrDefault(category);
                next = nextRankUpdate;
            }
            totals.RememberRanking(entities.CurrentUserName, ranks, table, next);
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
                ClearCounts();
            }
            Changed?.Invoke(this, EventArgs.Empty);
        }

        private void ClearCounts()
        {
            Kills = Deaths = Assists = 0;
            ApGained = RankGained = 0;
            StartedAt = now();
            recentKills.Clear();
            events.Clear();
        }

        /// <summary>
        /// Ends the session: returns it for the PvP history (null if nothing happened) and starts a fresh count.
        /// PvP mode stays on.
        /// </summary>
        public PvpSession? EndSession()
        {
            PvpSession? ended = null;
            lock (gate)
            {
                var t = now();
                if (Kills + Deaths + Assists > 0 || ApGained != 0 || RankGained != 0)
                    ended = new PvpSession(Guid.NewGuid(), StartedAt, t, entities.CurrentUserName ?? "",
                        Kills, Deaths, Assists, ApGained, RankGained, events.ToList());
                ClearCounts();
            }
            Changed?.Invoke(this, EventArgs.Empty);
            return ended;
        }

        /// <summary>Your class name for the rank line ("Ranger"), empty if not known yet.</summary>
        public string ClassName => entities.CurrentUserClassName;

        /// <summary>The enemy side's server id as last seen on an enemy killer; 0 until then.</summary>
        public int EnemyServer { get { lock (gate) return enemyServer; } }

        private void LearnServers(PvpParser.Death death)
        {
            if (death.Killer == 0 || death.KillerServer == 0) return;
            int before = enemyServer;
            if (IsMe(death.Killer, death.KillerName)) ownServer = death.KillerServer;
            else if (entities.IsCurrentPlayer(death.Victim)) enemyServer = death.KillerServer;
            else if (ownServer != 0 && death.KillerServer != ownServer) enemyServer = death.KillerServer;

            // Kills and assists from before the enemy server was known: they were on enemies, fill it in.
            if (before == 0 && enemyServer != 0)
                for (int i = 0; i < events.Count; i++)
                    if (events[i].ServerId == 0 && events[i].Kind != PvpEventKind.Death)
                        events[i] = events[i] with { ServerId = enemyServer };
        }

        /// <summary>What Hawkeye knows about a player entity right now.</summary>
        private PvpEvent About(PvpEventKind kind, DateTime at, int entityId, string name = "", string legion = "", int server = 0)
        {
            var p = entities.GetPlayerEntity(entityId);
            return new PvpEvent(at, kind,
                !string.IsNullOrEmpty(name) ? name : p?.Name ?? "",
                legion, server,
                (int)(p?.CharacterClass?.Id ?? 0), p?.CharacterClass?.Name ?? "",
                p?.CombatPower ?? 0);
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
                LearnServers(death);
                if (entities.IsCurrentPlayer(death.Victim))
                {
                    if (Active)
                    {
                        Deaths++;
                        changed = true;
                        events.Add(About(PvpEventKind.Death, t, death.Killer, death.KillerName, death.KillerLegion, death.KillerServer));
                    }
                    logger.LogInformation("[PVP] you died (killer {Killer})", death.KillerName);
                }
                else if (death.Killer != 0)
                {
                    bool yours = IsMe(death.Killer, death.KillerName);
                    bool player = entities.IsIdentifiedPlayer(death.Victim);
                    if (yours && player)
                    {
                        if (Active)
                        {
                            Kills++;
                            changed = true;
                            events.Add(About(PvpEventKind.Kill, t, death.Victim, server: enemyServer));
                        }
                        logger.LogInformation("[PVP] kill (victim entity {Victim})", death.Victim);
                    }
                    recentKills.RemoveAll(k => t - k.At > AssistWindow);
                    recentKills.Add(new KillEvent(t, yours, player, death.Victim));
                }
            }
            if (changed) Changed?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>A login inventory (0x5611). Kept so the first AP change can find the AP total you logged in with.</summary>
        public void OnInventory(byte[] data)
        {
            lock (gate)
            {
                inventories.Add(data);
                if (inventories.Count > InventoriesKept) inventories.RemoveAt(0);
            }
        }

        /// <param name="key">The inventory key AP is filed under (from the same message), to find the login total.</param>
        public void OnApTotal(long total, uint? key = null)
        {
            bool changed = false;
            lock (gate)
            {
                var t = now();
                if (lastAp is null && key is { } k)
                {
                    for (int i = inventories.Count - 1; i >= 0 && lastAp is null; i--)
                        if (PvpParser.FindCount(inventories[i], k) is { } atLogin && atLogin <= total)
                        {
                            lastAp = atLogin;   // exact: what you logged in with
                            logger.LogInformation("[PVP] AP compared with the login total (+{Gain})", total - atLogin);
                        }
                }
                if (lastAp is null && totals?.Get(entities.CurrentUserName)?.Ap is { } remembered
                    && total > remembered && total - remembered <= MaxApJumpFromMemory)
                {
                    lastAp = remembered;   // first AP change since Hawkeye started: compare with the remembered total
                    logger.LogInformation("[PVP] AP compared with the remembered total (+{Gain})", total - remembered);
                }
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
                            if (Active)
                            {
                                Assists++;
                                events.Add(About(PvpEventKind.Assist, cause.At, cause.Victim, server: enemyServer));
                            }
                            logger.LogInformation("[PVP] assist (+{Ap} AP)", total - before);
                        }
                    }
                }
                lastAp = total;
            }
            totals?.Remember(entities.CurrentUserName, ap: total);
            if (changed) Changed?.Invoke(this, EventArgs.Empty);
        }

        public void OnRankTotal(long total)
        {
            bool changed = false;
            lock (gate)
            {
                if (lastRank is null && totals?.Get(entities.CurrentUserName)?.Rank is { } remembered
                    && total != remembered && total - remembered <= MaxRankJumpFromMemory
                    && remembered - total <= MaxRankDropFromMemory)
                {
                    lastRank = remembered;
                    logger.LogInformation("[PVP] rank compared with the remembered total ({Gain:+#;-#})", total - remembered);
                }
                if (lastRank is { } before && total != before && Active)
                {
                    RankGained += total - before;
                    changed = true;
                }
                lastRank = total;
                changed |= ownRanks.Count > 0;   // points to the next rank moved
                standingStale = true;
            }
            totals?.Remember(entities.CurrentUserName, rank: total);
            if (changed) Changed?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Your own ranking line (0x8D57), sent at login and with each hourly update. Its rank points are also your
        /// starting total, so the first kill's rank points count exactly.
        /// </summary>
        public void OnOwnRank(RankList list)
        {
            if (list.Rows.Count == 0) return;
            lock (gate)
            {
                var row = list.Rows[0];
                ownRanks[list.Category] = row;
                standingStale = true;
                if (list.NextUpdate is { } next) nextRankUpdate = next;
                if (lastRank is null)
                {
                    lastRank = row.Points;
                    logger.LogInformation("[PVP] rank points at login {Points:N0}", row.Points);
                }
            }
            SaveRanking();
            Changed?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>A whole ranking table (0x8D55, the top 500), sent at login and, we assume, with each hourly update.</summary>
        public void OnRankTable(RankList list)
        {
            lock (gate)
            {
                rankTables[list.Category] = list;
                standingStale = true;
                if (list.NextUpdate is { } next) nextRankUpdate = next;
            }
            logger.LogInformation("[PVP] ranking table {Category}: {Rows} rows", list.Category, list.Rows.Count);
            SaveRanking();
            Changed?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Your class rank (or the overall one if no class line came), with points to the next rank worked out
        /// against the last table; null before the game sent your ranking line.
        /// </summary>
        public RankStanding? Standing
        {
            get
            {
                lock (gate)
                {
                    if (!standingStale) return standing;
                    standingStale = false;
                    int category = ownRanks.Keys.Where(c => c != 0).DefaultIfEmpty(-1).Max();
                    if (category < 0 && ownRanks.ContainsKey(0)) category = 0;
                    if (category < 0) return standing = null;
                    var own = ownRanks[category];
                    int? overall = category != 0 && ownRanks.TryGetValue(0, out var o) ? o.Rank : null;
                    return standing = RankMath.Standing(own, category, lastRank ?? own.Points,
                        rankTables.GetValueOrDefault(category), overall, nextRankUpdate);
                }
            }
        }

        // A different character: their totals start over (the login messages for them follow) and their remembered
        // ranking shows until the game sends a new one.
        private void OnCharacterNamed(string name)
        {
            lock (gate)
            {
                lastAp = lastRank = null;
            }
            LoadRanking(name);
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
