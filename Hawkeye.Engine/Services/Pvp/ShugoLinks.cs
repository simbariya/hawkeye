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

        public static string? For(PvpEvent e, string? enemyServerSetting)
        {
            if (string.IsNullOrWhiteSpace(e.Name)) return null;
            var slug = KnownServers.TryGetValue(e.ServerId, out var known) ? known : Slug(enemyServerSetting);
            return slug is null ? null : $"https://shugo.gg/character/global/{slug}/{Uri.EscapeDataString(e.Name)}";
        }

        [GeneratedRegex("[^a-z]+")]
        private static partial Regex NonLetters();

        [GeneratedRegex("^([a-z]+)-[a-z]+(?:-[a-z]+)*$")]
        private static partial Regex SlugForm();
    }
}
