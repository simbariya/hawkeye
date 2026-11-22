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
            if (PvpParser.ParseApTotal(packet.Data) is { } ap) pvp.OnApTotal(ap, PvpParser.ParseApKey(packet.Data));
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

    /// <summary>The login inventory (0x5611): holds the AP total you logged in with.</summary>
    [PacketOpcode(PacketOpcodes.Inventory)]
    public sealed class InventoryProcessor(PvpTracker pvp) : IOpcodeProcessor
    {
        public void Process(Packet packet) => pvp.OnInventory(packet.Data);
    }

    /// <summary>Your line in a PvP ranking (0x8D57).</summary>
    [PacketOpcode(PacketOpcodes.OwnRanking)]
    public sealed class OwnRankingProcessor(PvpTracker pvp) : IOpcodeProcessor
    {
        public void Process(Packet packet)
        {
            if (PvpRankingParser.ParseOwn(packet.Data) is { } list) pvp.OnOwnRank(list);
        }
    }

    /// <summary>A PvP ranking table (0x8D55).</summary>
    [PacketOpcode(PacketOpcodes.RankingTable)]
    public sealed class RankingTableProcessor(PvpTracker pvp) : IOpcodeProcessor
    {
        public void Process(Packet packet)
        {
            if (PvpRankingParser.ParseTable(packet.Data) is { } list) pvp.OnRankTable(list);
        }
    }
}
