using Hawkeye.Core.Data;
using Hawkeye.Core.GameData.Services;
using Hawkeye.Core.Models;
using Hawkeye.Engine.PacketProcessing.Routing;
using Hawkeye.Engine.PacketProcessing.Shared;
using Hawkeye.Engine.Services.Entity;
using Hawkeye.Engine.Services.Session;
using Hawkeye.Engine.Services.Theostone;
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
        private readonly TheostoneAlerts? theostones;
        private readonly EntityTracker entities;

        public BuffPacketProcessor(ILogger<BuffPacketProcessor> logger, CombatSessionManager sessionManager, TheostoneAlerts? theostones = null)
        {
            this.theostones = theostones;
            this.entities = sessionManager.Entities;
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

        /// <summary>
        /// Returns null when the buff was passed on, or why it was dropped.
        ///
        /// Layout (checked 2026-11-26 on every buff message in the owner's recordings): 0x382A (new) is
        /// [entity varint][u8 ?][u8 type][instance varint], 0x382B (refreshed) the same without the u8 before the type;
        /// then [u32 id x10][u32 ms][4 bytes][u64 time, ms since 1970][caster varint]. Until v0.2.38 Hawkeye read the
        /// caster from inside the time (so it was always 161, "the fixed caster" of I9/I10), Theostone debuffs read it
        /// one byte late, and 0x382B was read as if it had the extra byte.
        /// </summary>
        private string? TryProcess(Packet packet)
        {
            var r = new PacketReader(packet.Data);

            r.ReadVarInt(); //len
            var opcode = r.ReadU16();

            var entityId = (int)r.ReadVarInt();
            if (opcode == PacketOpcodes.BuffEffectA) r.ReadU8();     //unknown, only in a new buff
            byte type = r.ReadU8();
            var instance = (int)r.ReadVarInt(); // the game's number for this buff; its removal (0x382C) names it
            var buffId =(int)r.ReadU32() / 10;
            var durationMs = r.ReadU32();
            int casterId = 0;
            try { r.Skip(4 + 8); casterId = (int)r.ReadVarInt(); } catch (Exception) { /* caster missing: a short message */ }

            // A Theostone debuff (on you, or yours on an enemy: an alert until it ends).
            if (theostones is not null && gameData.IsTheostone(buffId))
            {
                if (durationMs >= 100 && durationMs <= MaxReasonableBuffDurationMs)
                    theostones.OnDebuff(entityId, instance, gameData.GetSkillOrDefault(buffId).Name, durationMs, casterId, buffId);
                return null;
            }

            // Any other debuff on you: put there by someone who is not you and was never in your group (v0.2.38).
            if (theostones is not null && casterId != 0 && !entities.IsFriendly(casterId) && entities.IsCurrentPlayer(entityId))
                theostones.OnStatus(entityId, instance, StatusName(buffId), durationMs, casterId);

            if (!gameData.IsBuff(buffId)) return $"id {buffId} not a known buff, type {type}";
            var skill = gameData.GetSkillOrDefault(buffId);

            if (durationMs < 100 || durationMs > MaxReasonableBuffDurationMs) return $"duration {durationMs} ms out of range, type {type}";

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

        /// <summary>
        /// The tile's word for a debuff: its skill's name, or "Debuff" when the game data has no name for it. Class
        /// skills (11,000,000 and up) carry variant digits and are looked up by their base; anything else only by its
        /// exact id (the game's general effects 10000020 to 10000080, 2 to 5 s in PvP, have no name, and the base
        /// lookup turned them into "Glide"). Those general effects say what they are instead where known: "Stun",
        /// "Root", "Knockdown", "Blind", "Slow", "Silence" (<see cref="StatusKinds"/>).
        /// </summary>
        private string StatusName(int buffId)
        {
            if (StatusKinds.Kind(buffId) is { } kind) return kind;
            var name = buffId >= 11_000_000 && buffId < 20_000_000
                ? gameData.Skills.GetName(gameData.Skills.NormalizeSkillCode(buffId))
                : gameData.Skills.GetName(buffId);
            return string.IsNullOrWhiteSpace(name) ? "Debuff" : name;
        }
       
    }
}
