namespace Hawkeye.Engine.Models
{
    public record DpsDataPoint(
        double SecondOffset,
        long PerSecondDamage,
        double CumulativeDps);
}
