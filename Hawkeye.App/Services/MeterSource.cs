using Hawkeye.Engine.Services.History;
using Hawkeye.Engine.Services.Session;
using Hawkeye.Engine.Settings;

namespace Hawkeye.App.Services
{
    /// <summary>One player on the meter: from the running fight or from a fight in history.</summary>
    /// <summary>CombatPower: 0 when the game has not sent it (it does for you and your group only).</summary>
    public readonly record struct MeterRowData(long PlayerId, bool IsUser, int ClassId, string Name, long TotalDamage, double Dps, double Share, int Deaths,
        int CombatPower = 0);

    /// <summary>What the meter shows right now. <see cref="Shown"/> is set when it is your last fight from history.</summary>
    public sealed record MeterData(string Target, TimeSpan Duration, EncounterSnapshot? Shown, IReadOnlyList<MeterRowData> Rows);

    /// <summary>
    /// The single answer to "what is on the meter": your running fight, else (once you are known) your last fight
    /// from history. Used by the meter window and by streamer mode, so the two can never disagree.
    /// </summary>
    public sealed class MeterSource(CombatSessionManager sessions, IEncounterHistory history, ISettingsService settings)
    {
        private DateTime clearedAt = DateTime.MinValue;

        /// <summary>
        /// The Clear button: closes any fight in progress (it is saved to History first) and blanks the meter until
        /// your next fight. Fights from before the clear are no longer shown as "your last fight".
        /// </summary>
        public void Clear()
        {
            sessions.CompleteActiveSessions();
            clearedAt = DateTime.Now;
        }

        public MeterData Read()
        {
            var live = sessions.PlayerStats.Where(p => p.IsIdentified || p.DamagePercentage > 1).ToList();
            bool showDeaths = settings.Current.ShowDeaths;

            if (live.Count == 0 && history.LastOwn is { } shown && shown.StartedAt > clearedAt)
            {
                return new MeterData(
                    shown.TargetName,
                    TimeSpan.FromSeconds(shown.DurationSeconds),
                    shown,
                    shown.Players.Select(p => new MeterRowData(p.PlayerId, p.IsUser, p.ClassId, p.Name, p.TotalDamage, p.Dps, p.SharePercent,
                        showDeaths ? p.Deaths : 0, p.CombatPower)).ToList());
            }

            var deaths = showDeaths ? sessions.GetActiveBossDeaths() : new Dictionary<int, int>();
            return new MeterData(
                sessions.GetActiveTargetInfo()?.Name ?? string.Empty,
                sessions.GetCombatDuration(),
                null,
                live.Select(p => new MeterRowData(p.PlayerId, p.IsUser, p.ClassId, p.PlayerName, p.TotalDamage, p.DamagePerSecond,
                    p.DamagePercentage, deaths.GetValueOrDefault((int)p.PlayerId), p.CombatPower)).ToList());
        }
    }
}
