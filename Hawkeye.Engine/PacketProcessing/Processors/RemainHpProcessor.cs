using Hawkeye.Engine.Extensions;
using Hawkeye.Engine.PacketProcessing.Routing;
using Hawkeye.Engine.Services.Entity;

namespace Hawkeye.Engine.PacketProcessing.Processors
{
    [PacketOpcode(PacketOpcodes.RemainHp)]
    internal class RemainHpProcessor(EntityTracker entityTracker) : IOpcodeProcessor
    {
        public void Process(Packet packet)
        {
            var data = packet.Data;
            int offset = 3;
            var entityIdInfo = data.ReadVarInt(offset);
            offset += entityIdInfo.Length;
            offset += data.ReadVarInt(offset).Length;
            offset += data.ReadVarInt(offset).Length;
            offset += data.ReadVarInt(offset).Length;
            var hpCurrent = data.ReadUInt64Le(offset);
            entityTracker.UpdateTargetEntityHpCurrent(entityIdInfo.Value, (long)hpCurrent);
        }

    }
}
