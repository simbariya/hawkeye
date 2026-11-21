using System.Text.Json.Serialization;

namespace Hawkeye.Engine.Services.History
{
    /// <summary>
    /// One finished fight, frozen: everything the meter, the details window and the history list need,
    /// already calculated. Saved as JSON in %AppData%\Hawkeye\history\ (recent) or \history\saved\ (kept forever).
    /// Holds other players' names: it stays on this PC and never goes in git.
    /// </summary>
    public sealed class EncounterSnapshot
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string TargetName { get; set; } = string.Empty;
        public int MobCode { get; set; }
        public bool IsDummy { get; set; }
        public DateTime StartedAt { get; set; }
        public double DurationSeconds { get; set; }
        /// <summary>Set when the owner kept it in the "saved" folder.</summary>
        public bool IsSaved { get; set; }
        public List<EncounterPlayer> Players { get; set; } = new();

        [JsonIgnore] public long TotalDamage => Players.Sum(p => p.TotalDamage);
        [JsonIgnore] public EncounterPlayer? You => Players.FirstOrDefault(p => p.IsUser);
    }

    public sealed class EncounterPlayer
    {
        public long PlayerId { get; set; }
        public string Name { get; set; } = string.Empty;
        public int ClassId { get; set; }
        public bool IsUser { get; set; }
        public int CombatPower { get; set; }
        public long TotalDamage { get; set; }
        public double Dps { get; set; }
        public double SharePercent { get; set; }
        public int Deaths { get; set; }
        public double ActiveSeconds { get; set; }

        public int Hits { get; set; }
        public int Critical { get; set; }
        public int Perfect { get; set; }
        public int Double { get; set; }
        public int Front { get; set; }
        public int Back { get; set; }
        public int Parry { get; set; }
        public int Multi { get; set; }

        public List<EncounterSkill> Skills { get; set; } = new();
    }

    public sealed class EncounterSkill
    {
        public long SkillId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Icon { get; set; }
        public bool IsDot { get; set; }
        public bool[] Spec { get; set; } = [];
        public int Hits { get; set; }
        public int Critical { get; set; }
        public int Perfect { get; set; }
        public int Double { get; set; }
        public int Front { get; set; }
        public int Back { get; set; }
        public int Parry { get; set; }
        public int Multi { get; set; }
        public long TotalDamage { get; set; }
        public long MinHit { get; set; }
        public long MaxHit { get; set; }
        public double Dps { get; set; }
        public double SharePercent { get; set; }

        [JsonIgnore] public double Average => Hits > 0 ? (double)TotalDamage / Hits : 0;
    }
}
