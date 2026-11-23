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

        // Server names by their short code in ServerMap (the Korean ids). Only names shugo.gg uses for EU characters
        // (its leaderboard, 2026-11-23), plus Tahabata, which the owner saw as "TAH" next to an arena opponent.
        private static readonly Dictionary<string, string> NameByCode = new()
        {
            ["ISR"] = "israphel", ["SIE"] = "siel", ["TRI"] = "triniel", ["LUM"] = "lumiel", ["MAR"] = "marchutan",
            ["AZP"] = "azphel", ["ERE"] = "ereshkigal", ["LUD"] = "ludra", ["ODA"] = "odar", ["KRO"] = "kromede",
            ["NEZ"] = "nezekan", ["KAI"] = "kaisinel", ["YUS"] = "yustiel", ["ARI"] = "ariel", ["NAN"] = "nania",
            ["TAH"] = "tahabata", ["LUT"] = "luteros", ["BAK"] = "bakarma",
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
