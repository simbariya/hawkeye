using Hawkeye.Engine.PacketProcessing.Routing;
using Hawkeye.Engine.Services.Skills;

namespace Hawkeye.Engine.PacketProcessing.Processors
{
    /// <summary>Your skills and their levels (0x5100), at login, zone changes and skill setup switches.</summary>
    [PacketOpcode(PacketOpcodes.SkillList)]
    public sealed class SkillListProcessor(SkillLevels levels) : IOpcodeProcessor
    {
        public void Process(Packet packet)
        {
            if (SkillListParser.Parse(packet.Data) is { } list) levels.OnSkillList(list);
        }
    }
}
