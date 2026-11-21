namespace Hawkeye.Engine.Models
{
    public class PacketExtractionResult
    {
        public List<byte[]>? Packets { get; init; }
        public List<byte[]>? RemainingSegments { get; init; }
    }
}
