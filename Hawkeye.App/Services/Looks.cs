using Hawkeye.Engine.Settings;

namespace Hawkeye.App.Services
{
    /// <summary>The CSS class for each look (wwwroot/css/meter.css, hawkeye.css) and the meter row sizes it uses.</summary>
    public static class Looks
    {
        public static string ClassFor(Look look) => look switch
        {
            Look.Aurora => "theme-aurora",
            Look.Graphite => "theme-graphite",
            _ => "theme-monolith",
        };

        public static string NameOf(Look look) => look switch
        {
            Look.Aurora => "Aurora",
            Look.Graphite => "Graphite & Vermilion",
            _ => "Monolith",
        };

        /// <summary>Row height and gap in px; must match --row-h / --row-gap in meter.css.</summary>
        public static (int Row, int Gap) RowMetrics(Look look) => look switch
        {
            Look.Aurora => (20, 6),
            Look.Graphite => (27, 0),
            _ => (26, 2),
        };
    }
}
