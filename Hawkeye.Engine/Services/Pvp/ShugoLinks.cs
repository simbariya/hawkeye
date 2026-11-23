using System.Text.RegularExpressions;

namespace Hawkeye.Engine.Services.Pvp
{
    /// <summary>
    /// shugo.gg profile links: https://shugo.gg/character/global/{region}-{server}/{name} (format read off the site's
    /// own links, 2026-11-21; regions eu, nae, naw, la, as). The game sends numeric server ids, so a link comes from:
    /// 1. ids confirmed on shugo.gg (<see cref="KnownServers"/>; ids are the same for every player), else
    /// 2. the "Enemy server" setting: everyone in a PvP list is an enemy (kills, assists, and whoever killed you).
    /// </summary>
    public static partial class ShugoLinks
    {
        /// <summary>Confirmed 2026-11-21: ids from the owner's Abyss session, slugs from shugo.gg's leaderboards.</summary>
        public static readonly IReadOnlyDictionary<int, string> KnownServers = new Dictionary<int, string>
        {
            [1304] = "eu-kaisinel",
            [2303] = "eu-triniel",
            // Checked on real shugo.gg profiles of arena opponents, 2026-11-23 (name and class both match): the
            // CURRENT server, from the arena roster (the one after the name, not the one in the key).
            [1302] = "eu-nezekan",
            [1305] = "eu-yustiel",
            [2301] = "eu-israphel",
            [2302] = "eu-zikel",
            [2304] = "eu-lumiel",
            [2311] = "eu-ludra",
        };

        private static readonly (string Phrase, string Code)[] Regions =
        [
            ("north america east", "nae"), ("na east", "nae"), ("nae", "nae"),
            ("north america west", "naw"), ("na west", "naw"), ("naw", "naw"),
            ("south america", "la"), ("latin america", "la"), ("latam", "la"), ("sa", "la"), ("la", "la"),
            ("europe", "eu"), ("eu", "eu"),
            ("asia", "as"), ("sea", "as"), ("as", "as"),
        ];

        /// <summary>
        /// "Triniel EU", "Siel (NA East)", "Israphel Asia" or a slug such as "eu-triniel" to a shugo.gg server slug;
        /// null if no region is recognised.
        /// </summary>
        public static string? Slug(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var t = NonLetters().Replace(text.Trim().ToLowerInvariant(), " ").Trim();
            var slugForm = SlugForm().Match(text.Trim().ToLowerInvariant());
            if (slugForm.Success && Regions.Any(r => r.Code == slugForm.Groups[1].Value)) return slugForm.Value;

            foreach (var (phrase, code) in Regions)
            {
                if (t.EndsWith(" " + phrase)) return Make(code, t[..^phrase.Length]);
                if (t.StartsWith(phrase + " ")) return Make(code, t[phrase.Length..]);
            }
            return null;

            static string? Make(string code, string server)
            {
                var name = server.Trim().Replace(' ', '-');
                return name.Length == 0 ? null : $"{code}-{name}";
            }
        }

        /// <summary>
        /// A global server id to a shugo.gg slug, only for ids checked on real profiles (<see cref="KnownServers"/>).
        /// The Korean short codes (ServerMap, id - 300) do not name global servers: 2302 is "SIE" there but Zikel on EU.
        /// Unknown: 1303, 1311 (the game showed "TAH"), 1313 and the rest; they get no link rather than a wrong one.
        /// </summary>
        public static string? SlugFor(int serverId) => KnownServers.TryGetValue(serverId, out var known) ? known : null;

        /// <summary>
        /// The server's name for the arena list: shugo.gg's name when known ("Kaisinel"), else its number ("#1311").
        /// Not the Korean short codes: global servers are named differently (2302 is "SIE" there but Zikel on EU, and
        /// the owner saw "SIE" next to Zikel players in 0.2.31).
        /// </summary>
        public static string Code(int serverId)
        {
            if (serverId <= 0) return "";
            if (SlugFor(serverId) is { } slug) { var n = slug[(slug.IndexOf('-') + 1)..]; return char.ToUpperInvariant(n[0]) + n[1..]; }
            return "#" + serverId;
        }

        /// <summary>An arena opponent's profile: their own server, read from the game; no guess from Settings.</summary>
        public static string? ForArena(string name, int serverId) =>
            string.IsNullOrWhiteSpace(name) || SlugFor(serverId) is not { } slug ? null
            : $"https://shugo.gg/character/global/{slug}/{Uri.EscapeDataString(name)}";

        public static string? For(PvpEvent e, string? enemyServerSetting)
        {
            if (string.IsNullOrWhiteSpace(e.Name)) return null;
            var slug = SlugFor(e.ServerId) ?? Slug(enemyServerSetting);
            return slug is null ? null : $"https://shugo.gg/character/global/{slug}/{Uri.EscapeDataString(e.Name)}";
        }

        [GeneratedRegex("[^a-z]+")]
        private static partial Regex NonLetters();

        [GeneratedRegex("^([a-z]+)-[a-z]+(?:-[a-z]+)*$")]
        private static partial Regex SlugForm();
    }
}
