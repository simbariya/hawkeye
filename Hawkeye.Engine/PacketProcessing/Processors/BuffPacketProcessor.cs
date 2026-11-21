using Hawkeye.Core.Data;
using Hawkeye.Core.GameData.Services;
using Hawkeye.Core.Models;
using Hawkeye.Engine.PacketProcessing.Routing;
using Hawkeye.Engine.PacketProcessing.Shared;
using Hawkeye.Engine.Services.Session;
using Microsoft.Extensions.Logging;

namespace Hawkeye.Engine.PacketProcessing.Processors
{
    [PacketOpcode(PacketOpcodes.BuffEffectA)]
    [PacketOpcode(PacketOpcodes.BuffEffectB)]
    public sealed class BuffPacketProcessor : IOpcodeProcessor
    {

        private const uint MaxReasonableBuffDurationMs = 3_600_000;

        private readonly GameDataProvider gameData;
        private readonly ILogger<BuffPacketProcessor> logger;
        private readonly CombatSessionManager sessionManager;

        public BuffPacketProcessor(ILogger<BuffPacketProcessor> logger, CombatSessionManager sessionManager)
        {
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            this.gameData = GameDataProvider.Instance;
            this.sessionManager = sessionManager;

        }

        public void Process(Packet packet)
        {
            string? skipped = null;
            try
            {
                skipped = TryProcess(packet);
            }
            catch (Exception ex)
            {
                skipped = "read failed: " + ex.Message;
                throw;
            }
            finally
            {
                if (skipped is not null) LogIfTracked(packet, skipped);
            }
        }

        /// <summary>
        /// A tracked buff that never reaches the icon is invisible otherwise (I11). When a buff message is
        /// dropped, look for any tracked skill id in its raw bytes (stored as id x 10, little-endian) and,
        /// if one is there, log the whole message so the variant can be decoded.
        /// </summary>
        private void LogIfTracked(Packet packet, string reason)
        {
            var data = packet.Data;
            for (int i = 0; i + 4 <= data.Length; i++)
            {
                uint raw = BitConverter.ToUInt32(data, i);
                if (raw < 10 || !sessionManager.IsTrackedSkill((int)(raw / 10))) continue;
                logger.LogInformation("[BUFF-SKIP] tracked skill {SkillId} in a dropped buff message ({Reason}) at byte {Offset}: {Hex}",
                    raw / 10, reason, i, Convert.ToHexString(data));
                return;
            }
        }

        /// <summary>Returns null when the buff was passed on, or why it was dropped.</summary>
        private string? TryProcess(Packet packet)
        {
            var r = new PacketReader(packet.Data);

            r.ReadVarInt(); //len
            r.ReadU16();    //opcode

            var entityId = (int)r.ReadVarInt();
            r.ReadU8();     //unknown
            byte type = r.ReadU8();
            r.ReadVarInt(); //unknown
            var buffId =(int)r.ReadU32() / 10;

            if (!gameData.IsBuff(buffId)) return $"id {buffId} not a known buff, type {type}";
            var skill = gameData.GetSkillOrDefault(buffId);

            var durationMs = r.ReadU32();
            if (durationMs < 100 || durationMs > MaxReasonableBuffDurationMs) return $"duration {durationMs} ms out of range, type {type}";

            r.Skip(4); //unknown data

            r.ReadU32(); //timestamp

            var casterId = (int)r.ReadVarInt();

            logger.LogTrace("[BUFF] entityId={EntityId} buffId={BuffId} buffName={BuffName} type={Type} durationMs={DurationMs} casterId={CasterId}",
                entityId, buffId, skill.Name, type, durationMs, casterId);

            var buffEvent = new BuffEvent
            {
                EntityId = entityId,
                BuffId = skill.Id,
                BuffName = skill.Name,
                BuffIcon = skill.Icon,
                Description = "",
                DurationMs = durationMs,
                AppliedAt = DateTime.Now,
                CasterId = casterId,
            };

            if (sessionManager.IsTrackedSkill(skill.Id))
                logger.LogInformation("[BUFF] tracked {Name} message type {Type} on {EntityId} for {Ms} ms", skill.Name, type, entityId, durationMs);

            sessionManager.ProcessBuffEvent(buffEvent);
            return null;
        }

       
    }
}
