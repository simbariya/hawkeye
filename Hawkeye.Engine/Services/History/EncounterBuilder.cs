using Hawkeye.Engine.Services.Session;

namespace Hawkeye.Engine.Services.History
{
    /// <summary>Turns a fight (live or finished) into an <see cref="EncounterSnapshot"/>.</summary>
    public static class EncounterBuilder
    {
        public static EncounterSnapshot Build(TargetCombatSession session, IReadOnlyDictionary<int, int> deaths)
        {
            var mob = session.TargetInfo;
            var snapshot = new EncounterSnapshot
            {
                TargetName = mob.Name,
                MobCode = mob.MobCode,
                IsDummy = mob.IsDummy,
                StartedAt = session.SessionStart,
                DurationSeconds = session.GetCombatDuration().TotalSeconds,
            };

            foreach (var p in session.GetPlayerStats())
            {
                var skills = session.GetSkillStats(p.PlayerId)
                    .Select(s => new EncounterSkill
                    {
                        SkillId = s.SkillId,
                        Name = s.SkillName,
                        Icon = s.SkillIcon,
                        IsDot = s.IsDot,
                        Spec = s.SpecializationFlags,
                        Hits = s.HitCount,
                        Critical = s.CriticalHits,
                        Perfect = s.PerfectHits,
                        Double = s.DoubleDamageHits,
                        Front = s.FrontAttacks,
                        Back = s.BackAttacks,
                        Parry = s.ParryHits,
                        Multi = s.MultihitHits,
                        TotalDamage = s.TotalDamage,
                        MinHit = s.SafeMinHit,
                        MaxHit = s.MaxHit,
                        Dps = s.DamagePerSecond,
                        SharePercent = s.DamagePercentage,
                    })
                    .OrderByDescending(s => s.TotalDamage)
                    .ToList();

                snapshot.Players.Add(new EncounterPlayer
                {
                    PlayerId = p.PlayerId,
                    Name = p.PlayerName,
                    ClassId = p.ClassId,
                    IsUser = p.IsUser,
                    CombatPower = p.CombatPower,
                    TotalDamage = p.TotalDamage,
                    Dps = p.DamagePerSecond,
                    SharePercent = p.DamagePercentage,
                    Deaths = deaths.GetValueOrDefault((int)p.PlayerId),
                    ActiveSeconds = p.CombatDuration.TotalSeconds,
                    Hits = p.HitCount,
                    Critical = p.CriticalHits,
                    Perfect = p.PerfectHits,
                    Double = p.DoubleDamageHits,
                    Front = p.FrontAttacks,
                    Back = p.BackAttacks,
                    Parry = p.ParryHits,
                    Multi = skills.Sum(s => s.Multi),
                    Skills = skills,
                });
            }

            snapshot.Players = snapshot.Players.OrderByDescending(p => p.TotalDamage).ToList();
            return snapshot;
        }
    }
}
