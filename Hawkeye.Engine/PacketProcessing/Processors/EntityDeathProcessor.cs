using Hawkeye.Engine.PacketProcessing.Routing;
using Hawkeye.Engine.PacketProcessing.Shared;
using Hawkeye.Engine.Services.Entity;
using Hawkeye.Engine.Services.Session;

namespace Hawkeye.Engine.PacketProcessing.Processors
{

    [PacketOpcode(PacketOpcodes.EntityDeath)]
    public class EntityDeathProcessor(EntityTracker entityTracker, CombatSessionManager sessionManager) : IOpcodeProcessor
    {

        public void Process(Packet packet)
        {
            var reader = new PacketReader(packet.Data);

            reader.ReadVarInt();      // packet length
            reader.ReadU16();         // opcode
            var entityId = (int)reader.ReadVarInt();

            if (entityTracker.IsIdentifiedPlayer(entityId)) sessionManager.RegisterPlayerDeath(entityId);
        }
    }
}
