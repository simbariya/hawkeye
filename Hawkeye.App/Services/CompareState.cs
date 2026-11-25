using Hawkeye.Engine.Services.History;

namespace Hawkeye.App.Services
{
    /// <summary>
    /// The fights the Compare window shows side by side, oldest first (A, B, C...). The history window sets them;
    /// the compare page follows. The rows themselves: <see cref="FightComparison"/>.
    /// </summary>
    public sealed class CompareState
    {
        public IReadOnlyList<EncounterSnapshot> Fights { get; private set; } = [];

        public event Action? Changed;

        public void Show(IEnumerable<EncounterSnapshot> fights)
        {
            Fights = fights.DistinctBy(f => f.Id).OrderBy(f => f.StartedAt).ToList();
            Changed?.Invoke();
        }

        public void Remove(Guid id)
        {
            Fights = Fights.Where(f => f.Id != id).ToList();
            Changed?.Invoke();
        }
    }
}
