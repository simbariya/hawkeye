using Hawkeye.Engine.PacketProcessing.Routing;
using Hawkeye.Engine.PacketProcessing.Shared;
using Hawkeye.Engine.Services.Entity;
using Hawkeye.Engine.Services.Pvp;
using Hawkeye.Engine.Services.Session;

namespace Hawkeye.Engine.PacketProcessing.Processors
{
    /// <summary>
    /// Someone died (0x8D04). Feeds the boss-fight death count, and PvP mode (it also carries the killer;
    /// layout in <see cref="PvpParser"/>).
    /// </summary>
    [PacketOpcode(PacketOpcodes.EntityDeath)]
    public class EntityDeathProcessor(EntityTracker entityTracker, CombatSessionManager sessionManager, PvpTracker pvp) : IOpcodeProcessor
    {
        public void Process(Packet packet)
        {
            var reader = new PacketReader(packet.Data);

            reader.ReadVarInt();      // packet length
            reader.ReadU16();         // opcode
            var entityId = (int)reader.ReadVarInt();

            if (entityTracker.IsIdentifiedPlayer(entityId)) sessionManager.RegisterPlayerDeath(entityId);

            if (PvpParser.ParseDeath(packet.Data) is { } death) pvp.OnDeath(death);
        }
    }
}
