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
            // Checked on real shugo.gg profiles of arena opponents, 2026-11-23. Global names can differ from the
            // Korean code: 2318 is "BAB" in ServerMap but Zikel on EU.
            [1305] = "eu-yustiel",
            [2301] = "eu-israphel",
            [2311] = "eu-ludra",
            [2318] = "eu-zikel",
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

        // Server names by their short code in ServerMap (the Korean ids): only codes whose name shugo.gg uses for EU
        // characters (its leaderboard and profiles, 2026-11-23). Confirmed on real profiles: 1304 Kaisinel, 1305
        // Yustiel, 2301 Israphel, 2311 Ludra, 2318 Zikel. NOT known: TAH (1311; not eu-tahabata, zikel, zemurru,
        // nathara, atiel or agnita), PER (1313; nor those five), and the rest; those get no link rather than a wrong one.
        private static readonly Dictionary<string, string> NameByCode = new()
        {
            ["ISR"] = "israphel", ["SIE"] = "siel", ["TRI"] = "triniel", ["LUM"] = "lumiel", ["MAR"] = "marchutan",
            ["AZP"] = "azphel", ["ERE"] = "ereshkigal", ["LUD"] = "ludra", ["ODA"] = "odar", ["KRO"] = "kromede",
            ["NEZ"] = "nezekan", ["KAI"] = "kaisinel", ["YUS"] = "yustiel", ["ARI"] = "ariel", ["NAN"] = "nania",
            ["LUT"] = "luteros", ["BAK"] = "bakarma", ["HIT"] = "hithanya", ["FRE"] = "fregion", ["MUN"] = "munin",
        };

        /// <summary>
        /// A global server id to a shugo.gg slug. EU ids are the Korean ids (ServerMap) plus 300: 1304 Kaisinel (the
        /// owner's), 2303 Triniel, 1311 Tahabata (an arena opponent the game showed as TAH), 2301 Israphel. Other
        /// regions' ranges are not known yet: null.
        /// </summary>
        public static string? SlugFor(int serverId)
        {
            if (KnownServers.TryGetValue(serverId, out var known)) return known;
            if (serverId is (>= 1300 and < 1400) or (>= 2300 and < 2400)
                && Hawkeye.Core.Data.ServerMap.Servers.TryGetValue(serverId - 300, out var code)
                && NameByCode.TryGetValue(code, out var name))
                return "eu-" + name;
            return null;
        }

        /// <summary>
        /// The server's name for the arena list: shugo.gg's name when known ("Kaisinel"), else the game's short code
        /// ("TAH" for 1311), else its number ("#1322"), so an unnamed server is still visible.
        /// </summary>
        public static string Code(int serverId)
        {
            if (serverId <= 0) return "";
            if (SlugFor(serverId) is { } slug) { var n = slug[(slug.IndexOf('-') + 1)..]; return char.ToUpperInvariant(n[0]) + n[1..]; }
            var code = serverId is (>= 1300 and < 1400) or (>= 2300 and < 2400) ? Hawkeye.Core.Data.ServerMap.GetName(serverId - 300) : "";
            return code != "" ? code : "#" + serverId;
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
