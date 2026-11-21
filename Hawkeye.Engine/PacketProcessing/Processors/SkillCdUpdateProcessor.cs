using Hawkeye.Core.GameData.Services;
using Hawkeye.Engine.PacketProcessing.Routing;
using Hawkeye.Engine.PacketProcessing.Shared;
using Hawkeye.Engine.Services.Session;
using Hawkeye.Engine.Services.Timed;

namespace Hawkeye.Engine.PacketProcessing.Processors
{
    [PacketOpcode(PacketOpcodes.SkillCdUpdate)]
    internal class SkillCdUpdateProcessor(CombatSessionManager sessionManager) : IOpcodeProcessor
    {
        private readonly GameDataProvider gameData = GameDataProvider.Instance;
        public void Process(Packet packet)
        {
            var r = new PacketReader(packet.Data);

            r.ReadVarInt(); //len
            r.ReadU16(); //opcode

            var count = r.ReadU8();

            for (int i = 0; i < count; i++)
            {
                ProcessEntry(r);
            }
        }
       
        private void ProcessEntry(PacketReader r)
        {
            var skillId = r.ReadU32();
            var msLeft = r.ReadVarInt();
            var skill = gameData.GetSkillOrDefault((int)skillId);

            sessionManager.RegisterSkillCdUpdateEvent(new TimedEvent()
            {
                Name = skill.Name,
                IconUrl = skill.Icon ?? string.Empty,
                Id = (uint)skill.Id,
                Duration = TimeSpan.FromMilliseconds(msLeft)
            });

        }

    }
}
