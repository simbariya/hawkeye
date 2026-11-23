using Hawkeye.Core.Data;
using Hawkeye.Core.GameData.Services;
using Hawkeye.Core.Models;
using Hawkeye.Engine.Models;
using Hawkeye.Engine.PacketProcessing.Routing;
using Hawkeye.Engine.PacketProcessing.Shared;
using Hawkeye.Engine.Services.Entity;
using Hawkeye.Engine.Services.Session;
using Hawkeye.Engine.Services.Theostone;
using Microsoft.Extensions.Logging;

namespace Hawkeye.Engine.PacketProcessing.Processors
{
    [PacketOpcode(PacketOpcodes.DotDamage)]
    public sealed class DotDamagePacketProcessor : IOpcodeProcessor
    {
        public event EventHandler<PlayerDamage>? DamageReceived;

        private readonly GameDataProvider gameData;
        private readonly EntityTracker entityTracker;
        private readonly CombatSessionManager sessionManager;
        private readonly ILogger<DotDamagePacketProcessor> logger;
        private readonly TheostoneAlerts? theostones;

        public DotDamagePacketProcessor(EntityTracker entityTracker, CombatSessionManager sessionManager, ILogger<DotDamagePacketProcessor> logger, TheostoneAlerts? theostones = null)
        {
            this.theostones = theostones;
            this.entityTracker = entityTracker ?? throw new ArgumentNullException(nameof(entityTracker));
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            this.gameData = GameDataProvider.Instance;
            this.sessionManager = sessionManager;

        }

        public void Process(Packet packet)
        {
            var reader = new PacketReader(packet.Data);

            reader.ReadVarInt(); //len
            reader.ReadU16();    //opcode

            var targetId = reader.ReadVarInt();

            var effectType = reader.ReadU8();
            if ((effectType & 0x02) == 0) return;

            var actorId = reader.ReadVarInt();
            reader.ReadVarInt(); // unknown

            var skillCode = ((int)reader.ReadU32()) / 100;
            if (!DataValidationHelper.IsReasonableSkillCode(skillCode)) return;

            var damage = reader.ReadVarInt();

            if (targetId == actorId) return;

            // A Poison or Bleed tick from a Theostone: adds to its alert tile (v0.2.21).
            if (theostones is not null && gameData.IsTheostone(skillCode))
                theostones.OnTick((int)actorId, (int)targetId, gameData.GetSkillOrDefault(skillCode).Name, damage);

            CharacterClass? characterClass;
            if (!gameData.IsTheostone(skillCode))
            {
                characterClass = gameData.GetClassBySkillCode(skillCode);
                if (characterClass == null)
                {
                    logger.LogWarning("Unknown class for skill code: {SkillCode}", skillCode);
                    return;
                }
                if (!gameData.IsDotDamageSkill(skillCode)) return;
                if (gameData.IsHealingSkill(skillCode)) return;
            }
            else
            {
                var player = entityTracker.GetPlayerEntity((int)actorId);
                if (player == null)
                {
                    logger.LogWarning("Unknown player for theostone code: {SkillCode}", skillCode);
                    return;
                }
                characterClass = player.CharacterClass;
            }

            var dmg =  new PlayerDamage
            {
                DateTime = DateTime.Now,
                SourceEntity = entityTracker.GetOrCreateSessionPlayer((int)actorId, characterClass),
                TargetEntity = entityTracker.GetOrCreateTargetEntity((int)targetId),
                Skill = gameData.GetSkillOrDefault(skillCode),
                CharacterClass = characterClass,
                Damage = damage,
                IsDot = true,
            };

            sessionManager.ProcessDamageEvent(dmg);
        }

      
    }
}
