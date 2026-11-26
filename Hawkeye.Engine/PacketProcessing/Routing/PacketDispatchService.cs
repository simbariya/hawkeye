using Hawkeye.Engine.Extensions;
using Microsoft.Extensions.Logging;

namespace Hawkeye.Engine.PacketProcessing.Routing
{
    public sealed class PacketDispatchService(OpcodeProcessorRegistry registry, ILogger<PacketDispatchService> logger)
    {
        /// <summary>Every message, before its processor (packet thread). For local add-ons (Hawkeye.App LocalExtras).</summary>
        public event Action<Packet>? Seen;

        public void Dispatch(Packet packet)
        {
            if (Seen is { } seen)
            {
                try { seen(packet); }
                catch (Exception ex) { logger.LogError(ex, "A local add-on failed on a message."); }
            }

            if (!TryReadOpcode(packet.Data, out ushort opcode))
            {
                logger.LogTrace("Broken packet, cannot read opcode.");
                return;
            }

            if (!registry.TryGet(opcode, out var processor))
            {
                logger.LogTrace("No processor mapped for opcode 0x{Opcode:X4}.", opcode);
                return;
            }

            try
            {
                processor.Process(packet);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing packet for opcode 0x{Opcode:X4}.", opcode);
            }
        }

        private static bool TryReadOpcode(byte[] packet, out ushort opcode)
        {
            opcode = 0;
            if (packet.Length < 3) return false;

            var lenVarInt = packet.ReadVarInt();
            if (lenVarInt.Length <= 0) return false;

            int opcodeOffset = lenVarInt.Length;
            if (packet.Length < opcodeOffset + 2) return false;

            byte op1 = packet[opcodeOffset];
            byte op2 = packet[opcodeOffset + 1];
            opcode = (ushort)(op1 | (op2 << 8));
            return true;
        }
    }
}