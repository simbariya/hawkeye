using Hawkeye.Engine.PacketProcessing.Routing;
using Hawkeye.Engine.Services.Pvp;

namespace Hawkeye.Engine.PacketProcessing.Processors
{
    /// <summary>An item's count changed (0x561B). Only AP matters here.</summary>
    [PacketOpcode(PacketOpcodes.ItemCount)]
    public sealed class ItemCountProcessor(PvpTracker pvp) : IOpcodeProcessor
    {
        public void Process(Packet packet)
        {
            if (PvpParser.ParseApTotal(packet.Data) is { } ap) pvp.OnApTotal(ap);
        }
    }

    /// <summary>Your rank points total (0xE273).</summary>
    [PacketOpcode(PacketOpcodes.RankPoints)]
    public sealed class RankPointsProcessor(PvpTracker pvp) : IOpcodeProcessor
    {
        public void Process(Packet packet)
        {
            if (PvpParser.ParseRankTotal(packet.Data) is { } rank) pvp.OnRankTotal(rank);
        }
    }
}
