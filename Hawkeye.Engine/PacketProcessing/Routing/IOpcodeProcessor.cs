namespace Hawkeye.Engine.PacketProcessing.Routing
{
    public interface IOpcodeProcessor
    {
        void Process(Packet packet);
    }
}
