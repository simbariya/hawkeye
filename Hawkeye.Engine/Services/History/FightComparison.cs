namespace Hawkeye.Engine.Services.History
{
    /// <summary>
    /// Fights side by side (the Compare window, v0.2.34; owner, 2026-11-25: "which is better at lvl 20 if can have
    /// only one"): your skills across the fights, one row per skill, with its line in each fight.
    /// </summary>
    public static class FightComparison
    {
        /// <summary>The player a fight is compared on: you, or the top player in fights from before Hawkeye knew you.</summary>
        public static EncounterPlayer? PlayerOf(EncounterSnapshot fight) => fight.You ?? fight.Players.FirstOrDefault();

        /// <summary>One row: a skill and its line in each fight (null where it was not used).</summary>
        public sealed record SkillRow(long SkillId, string Name, string? Icon, IReadOnlyList<EncounterSkill?> Cells)
        {
            /// <summary>Used at different levels (fights where the level is not known are left out).</summary>
            public bool LevelDiffers => Cells.Where(c => c is { Level: > 0 }).Select(c => c!.Level).Distinct().Count() > 1;

            /// <summary>The chosen specialisations differ between the fights that used it.</summary>
            public bool SpecDiffers => Cells.Where(c => c is not null).Select(c => SpecText(c!.Spec)).Distinct().Count() > 1;

            public double BestDps => Cells.Max(c => c?.Dps ?? 0);
        }

        /// <summary>Your skills across the fights (matched by skill id), the strongest first.</summary>
        public static List<SkillRow> Rows(IReadOnlyList<EncounterSnapshot> fights)
        {
            var players = fights.Select(PlayerOf).ToList();
            var ids = players.Where(p => p is not null).SelectMany(p => p!.Skills.Select(s => s.SkillId)).Distinct();
            var rows = new List<SkillRow>();
            foreach (var id in ids)
            {
                var cells = players.Select(p => p?.Skills.FirstOrDefault(s => s.SkillId == id)).ToList();
                var first = cells.First(c => c is not null)!;
                rows.Add(new SkillRow(id, first.Name + (first.IsDot ? " (DoT)" : ""), first.Icon, cells));
            }
            return rows.OrderByDescending(r => r.BestDps).ToList();
        }

        /// <summary>The chosen specialisations as "1 4 5" (empty when none).</summary>
        public static string SpecText(bool[] spec) =>
            string.Join(" ", spec.Select((on, i) => on ? (i + 1).ToString() : null).Where(s => s is not null));
    }
}
