using Hawkeye.Engine.Services.Pvp;

namespace Hawkeye.App.Services
{
    /// <summary>
    /// What the PvP window shows: the live session or one from the PvP history, and which list (kills, deaths or
    /// assists). The meter and the history window set it; the PvP page follows it.
    /// </summary>
    public sealed class PvpView
    {
        public PvpSession? Saved { get; private set; }
        public bool Live => Saved is null;
        public PvpEventKind Kind { get; private set; } = PvpEventKind.Kill;

        public event Action? Changed;

        public void ShowLive(PvpEventKind kind)
        {
            Saved = null;
            Kind = kind;
            Changed?.Invoke();
        }

        public void ShowSaved(PvpSession session, PvpEventKind kind = PvpEventKind.Kill)
        {
            Saved = session;
            Kind = kind;
            Changed?.Invoke();
        }

        public void Select(PvpEventKind kind)
        {
            Kind = kind;
            Changed?.Invoke();
        }
    }
}
