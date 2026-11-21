using Hawkeye.Engine.PacketProcessing.Routing;
using Hawkeye.Engine.Services.Odyle;

namespace Hawkeye.Engine.PacketProcessing.Processors
{
    /// <summary>The game's counters list (0x610B, at login/switch): Odyle is entry 1. See <see cref="OdyleParser"/>.</summary>
    [PacketOpcode(PacketOpcodes.Counters)]
    public sealed class CountersProcessor(OdyleTracker odyle) : IOpcodeProcessor
    {
        public void Process(Packet packet)
        {
            if (OdyleParser.ParseWithKey(packet.Data) is { } reading)
                odyle.OnReading(reading.Current, reading.Pool, reading.Key);
        }
    }

    /// <summary>One counter changed (0x610C), e.g. Odyle spent on a cube. Counts only with Odyle's key.</summary>
    [PacketOpcode(PacketOpcodes.CounterUpdate)]
    public sealed class CounterUpdateProcessor(OdyleTracker odyle) : IOpcodeProcessor
    {
        public void Process(Packet packet)
        {
            if (OdyleParser.ParseUpdate(packet.Data, odyle.OdyleKey) is { } update)
                odyle.OnUpdate(update.Current, update.Pool);
        }
    }
}
