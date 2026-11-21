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

    /// <summary>
    /// shugo.gg profile links: https://shugo.gg/character/global/{region}-{server}/{name} (format read off the site,
    /// 2026-11-21). The game sends numeric server ids (e.g. 1304, 2303) that Hawkeye cannot name yet, so a link is
    /// only made for ids listed here. Fill in from a real profile link of a player on that server.
    /// </summary>
    public static class ShugoLinks
    {
        public static readonly Dictionary<int, string> ServerSlugs = new()
        {
            // [1304] = "eu-xxxx",   // fill in once confirmed
        };

        public static string? For(string name, int serverId) =>
            string.IsNullOrWhiteSpace(name) || !ServerSlugs.TryGetValue(serverId, out var slug)
                ? null
                : $"https://shugo.gg/character/global/{slug}/{Uri.EscapeDataString(name)}";
    }
}
