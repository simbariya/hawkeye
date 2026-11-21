using Hawkeye.Engine.Services.History;

namespace Hawkeye.App.Services
{
    /// <summary>
    /// What the details (combat analysis) window shows: the live fight (refreshed every second) or a fight from
    /// history, and which player in it. The meter and the history window set it; the details page follows it.
    /// </summary>
    public sealed class DetailsState
    {
        public EncounterSnapshot? Snapshot { get; private set; }
        public bool Live { get; private set; }
        public long PlayerId { get; private set; }

        public event Action? Changed;

        public void ShowLive(EncounterSnapshot? current, long playerId)
        {
            Snapshot = current;
            Live = true;
            PlayerId = playerId;
            Changed?.Invoke();
        }

        public void ShowSaved(EncounterSnapshot encounter, long? playerId = null)
        {
            Snapshot = encounter;
            Live = false;
            PlayerId = playerId ?? encounter.You?.PlayerId ?? encounter.Players.FirstOrDefault()?.PlayerId ?? 0;
            Changed?.Invoke();
        }

        /// <summary>The live view refreshed itself (keeps the chosen player).</summary>
        public void Refresh(EncounterSnapshot current)
        {
            Snapshot = current;
            Changed?.Invoke();
        }

        public void SelectPlayer(long playerId)
        {
            PlayerId = playerId;
            Changed?.Invoke();
        }
    }
}
