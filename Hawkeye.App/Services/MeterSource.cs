using Hawkeye.Engine.Services.History;
using Hawkeye.Engine.Services.Session;
using Hawkeye.Engine.Settings;

namespace Hawkeye.App.Services
{
    /// <summary>One player on the meter: from the running fight or from a fight in history.</summary>
    public readonly record struct MeterRowData(long PlayerId, bool IsUser, int ClassId, string Name, long TotalDamage, double Dps, double Share, int Deaths);

    /// <summary>What the meter shows right now. <see cref="Shown"/> is set when it is your last fight from history.</summary>
    public sealed record MeterData(string Target, TimeSpan Duration, EncounterSnapshot? Shown, IReadOnlyList<MeterRowData> Rows);

    /// <summary>
    /// The single answer to "what is on the meter": your running fight, else (once you are known) your last fight
    /// from history. Used by the meter window and by streamer mode, so the two can never disagree.
    /// </summary>
    public sealed class MeterSource(CombatSessionManager sessions, IEncounterHistory history, ISettingsService settings)
    {
        public MeterData Read()
        {
            var live = sessions.PlayerStats.Where(p => p.IsIdentified || p.DamagePercentage > 1).ToList();
            bool showDeaths = settings.Current.ShowDeaths;

            if (live.Count == 0 && history.LastOwn is { } shown)
            {
                return new MeterData(
                    shown.TargetName,
                    TimeSpan.FromSeconds(shown.DurationSeconds),
                    shown,
                    shown.Players.Select(p => new MeterRowData(p.PlayerId, p.IsUser, p.ClassId, p.Name, p.TotalDamage, p.Dps, p.SharePercent,
                        showDeaths ? p.Deaths : 0)).ToList());
            }

            var deaths = showDeaths ? sessions.GetActiveBossDeaths() : new Dictionary<int, int>();
            return new MeterData(
                sessions.GetActiveTargetInfo()?.Name ?? string.Empty,
                sessions.GetCombatDuration(),
                null,
                live.Select(p => new MeterRowData(p.PlayerId, p.IsUser, p.ClassId, p.PlayerName, p.TotalDamage, p.DamagePerSecond,
                    p.DamagePercentage, deaths.GetValueOrDefault((int)p.PlayerId))).ToList());
        }
    }
}
