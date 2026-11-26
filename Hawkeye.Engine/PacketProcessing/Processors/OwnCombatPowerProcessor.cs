using Hawkeye.Engine.PacketProcessing.Routing;
using Hawkeye.Engine.Services.Entity;

namespace Hawkeye.Engine.PacketProcessing.Processors
{
    /// <summary>
    /// Your combat power (0x3656, 19 bytes): u64 current at 3, u64 highest at 11. Sent at zone loads and when it
    /// changes (gear swaps), so your meter row follows the set you wear.
    /// </summary>
    [PacketOpcode(PacketOpcodes.OwnCombatPower)]
    public sealed class OwnCombatPowerProcessor(EntityTracker entities) : IOpcodeProcessor
    {
        public void Process(Packet packet)
        {
            var d = packet.Data;
            if (d.Length < 19) return;
            ulong current = BitConverter.ToUInt64(d, 3);
            ulong highest = BitConverter.ToUInt64(d, 11);
            if (current == 0 || current > highest || current > 10_000_000) return;
            entities.SetUserCombatPower((int)current);
        }
    }
}
