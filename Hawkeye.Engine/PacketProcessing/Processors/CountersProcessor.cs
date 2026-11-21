using Hawkeye.Engine.PacketProcessing.Routing;
using Hawkeye.Engine.Services.Odyle;

namespace Hawkeye.Engine.PacketProcessing.Processors
{
    /// <summary>The game's counters list (0x610B): Odyle is entry 1. See <see cref="OdyleParser"/>.</summary>
    [PacketOpcode(PacketOpcodes.Counters)]
    public sealed class CountersProcessor(OdyleTracker odyle) : IOpcodeProcessor
    {
        public void Process(Packet packet)
        {
            if (OdyleParser.Parse(packet.Data) is { } reading)
                odyle.OnReading(reading.Current, reading.Pool);
        }
    }
}
