using Hawkeye.Core.Data;
using Hawkeye.Core.GameData.Services;
using Hawkeye.Core.Models;
using Hawkeye.Engine.Models;
using Hawkeye.Engine.PacketProcessing.Routing;
using Hawkeye.Engine.PacketProcessing.Shared;
using Hawkeye.Engine.PacketProcessing.Shared.Exceptions;
using Hawkeye.Engine.Services.Entity;
using Hawkeye.Engine.Services.Session;
using Hawkeye.Engine.Services.Theostone;
using Microsoft.Extensions.Logging;

namespace Hawkeye.Engine.PacketProcessing.Processors
{
    [PacketOpcode(PacketOpcodes.Damage)]
    public sealed class DamagePacketProcessor : IOpcodeProcessor
    {
        private const int CriticalDamageType = 3;
        private readonly GameDataProvider gameData;
        private readonly EntityTracker entityTracker;
        private readonly CombatSessionManager sessionManager;
        private readonly ILogger<DamagePacketProcessor> logger;
        private readonly TheostoneAlerts? theostones;

        public DamagePacketProcessor(EntityTracker entityTracker, CombatSessionManager sessionManager, ILogger<DamagePacketProcessor> logger, TheostoneAlerts? theostones = null)
        {
            this.entityTracker = entityTracker ?? throw new ArgumentNullException(nameof(entityTracker));
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            this.gameData = GameDataProvider.Instance;
            this.sessionManager = sessionManager;
            this.theostones = theostones;
        }

      
        public void Process(Packet packet)
        {
            // Who fights whom, for "debuffs on you" (v0.2.42): only someone who hit you or whom you hit can debuff you.
            // Read from the header, so hits the damage layout below refuses still count (PvP recording: a Poach hit
            // with damage that Parse throws on). A heal is not a fight. A player's skill on you counts only if it
            // carries damage (v0.2.43; mask 4 to 7, as every attack in the recordings): with none it is a buff, like a
            // chanter's mantra (TheostoneAlerts.OnFight).
            if (theostones is not null && Header(packet.Data) is { } fight && !gameData.IsHealingSkill(fight.Code))
                theostones.OnFight(fight.Actor, fight.Target, hurt: !IsClassSkill(fight.Code) || (fight.Mask & 0x0F) is >= 4 and <= 7);

            if (theostones is not null && NoDamageTheostone(packet.Data) is { } proc)
            {
                theostones.OnHit(proc.Actor, proc.Target, gameData.GetSkillOrDefault(proc.Code).Name, 0, proc.Code);
                return;
            }

            var parsed = Parse(packet.Data);
            if (!parsed.IsValid)
            {
                logger.LogDebug("04-38 parsing failed: {Result}", parsed.Result);
                return;
            }

            var d = parsed.Data!;
            if (theostones is not null && gameData.IsTheostone(d.SkillCode))
                theostones.OnHit(d.ActorId, d.TargetId, gameData.GetSkillOrDefault(d.SkillCode).Name, d.Damage, d.SkillCode, d.IsCritical, d.IsDoubleDamage);   // before the player check below drops unknown attackers

            var playerDamage = BuildPlayerDamage(d);
            if (playerDamage != null)
                sessionManager.ProcessDamageEvent(playerDamage);
        }

        private PlayerDamage? BuildPlayerDamage(DamagePacketData data)
        {
            if (gameData.IsHealingSkill(data.SkillCode)) return null;
            CharacterClass? characterClass;
            if (!gameData.IsTheostone(data.SkillCode))
            {
                characterClass = gameData.GetClassBySkillCode(data.SkillCode);
                if (characterClass == null)
                {
                    logger.LogWarning("Unknown class for skill code: {SkillCode}", data.SkillCode);
                    return null;
                }
            }
            else
            {
                var player = entityTracker.GetPlayerEntity(data.ActorId);
                if (player == null)
                {
                    logger.LogWarning("Unknown player for theostone code: {SkillCode}", data.SkillCode);
                    return null;
                }
                characterClass = player.CharacterClass;
            }

            return new PlayerDamage
            {
                DateTime = data.Timestamp,
                SourceEntity = entityTracker.GetOrCreateSessionPlayer(data.ActorId, characterClass!),
                TargetEntity = entityTracker.GetOrCreateTargetEntity(data.TargetId),
                Skill = gameData.GetSkillOrDefault(data.SkillCode),
                CharacterClass = characterClass!,
                Damage = data.Damage,
                IsCritical = data.IsCritical,
                IsBackAttack = data.IsBackAttack,
                IsPerfect = data.IsPerfect,
                IsDoubleDamage = data.IsDoubleDamage,
                IsParry = data.IsParry,
                IsFrontAttack = data.IsFrontAttack,
                MultihitCunt = data.MultihitCount,
                MultihitDamage = data.MultihitDamageArray.Sum()
            };
        }

        private ParsedDamagePacket Parse(byte[] packet)
        {

            var reader = new PacketReader(packet);

            reader.ReadVarInt(); //len
            reader.ReadU16();    //opcode

            var targetId = reader.ReadVarInt();
            var mask =reader.ReadVarInt();
            var switchValue = ProcessSwitchValue((int)mask);
            if (switchValue < 0) return Fail(PacketProcessResult.SWITH_VALUE_ERROR);
            reader.ReadVarInt(); // unkown flag
            var actorId = reader.ReadVarInt();

            if (actorId == targetId) return Fail(PacketProcessResult.ACTORID_EQUALS_TARGETID);

            var skillCode = reader.ReadU32();
            if (!DataValidationHelper.IsReasonableSkillCode((int)skillCode)) return Fail(PacketProcessResult.SKILLCODE_ERROR);
            reader.ReadU8(); // unknown
            var damageType = reader.ReadVarInt();
            var specialFlags = ReadSpecialBytesBlock(reader, switchValue);
            var unknownVarInt = reader.ReadVarInt(); // unknown
            var damage = reader.ReadVarInt();

            if ((mask & 0x0F) == 4) reader.ReadVarInt();

            int extraHitsCount = 0;
            List<int> multihitDamageList = new List<int>();
            if ((mask & 0x20) != 0)
            {
               extraHitsCount = (int)reader.ReadVarInt();
               for (int i = 0; i < extraHitsCount; i++)
               {
                   multihitDamageList.Add((int)reader.ReadVarInt());
               }
            }

            var data = new DamagePacketData
            {
                TargetId = (int)targetId,
                ActorId = (int)actorId,
                SkillCode = (int)skillCode,
                DamageType = (int)damageType,
                Damage = damage,
                IsCritical = damageType == CriticalDamageType,
                IsBackAttack = specialFlags.IsBackAttack,
                IsFrontAttack = specialFlags.IsFrontAttack,
                IsParry = specialFlags.IsParry,
                IsPerfect = specialFlags.IsPerfect,
                IsDoubleDamage = specialFlags.IsDoubleDamage,
                UnknownVarInt = (int)unknownVarInt,
                MultihitCount = extraHitsCount,
                MultihitDamageArray = multihitDamageList.ToArray()
            };

            return new ParsedDamagePacket(data, PacketProcessResult.SUCCES);

        }

        /// <summary>
        /// A debuff Theostone's proc (Blind, Silence, Poison...) is a hit with no damage: its mask's low bits are 0, which
        /// the damage layout below refuses (found 2026-11-26: all 96 such procs in the owner's recordings had mask 0,
        /// every damage stone's had 4 or 6). Until v0.2.38 these never reached the alerts, so your own debuff stones only
        /// showed when the debuff landed, and then not at all (the buff reader got the caster wrong too).
        /// Returns the proc, or null for anything else.
        /// </summary>
        private (int Actor, int Target, int Code)? NoDamageTheostone(byte[] packet) =>
            Header(packet) is { } h && (h.Mask & 0x0F) == 0 && gameData.IsTheostone(h.Code) ? (h.Actor, h.Target, h.Code) : null;

        /// <summary>A player class's skill (11,000,000 to 19,999,999); monsters' skills and Theostones are below.</summary>
        private static bool IsClassSkill(int code) => code >= 11_000_000 && code < 20_000_000;

        /// <summary>[len][op][target varint][mask varint][flag varint][actor varint][u32 skill], or null.</summary>
        private static (int Actor, int Target, int Code, long Mask)? Header(byte[] packet)
        {
            try
            {
                var reader = new PacketReader(packet);
                reader.ReadVarInt(); //len
                reader.ReadU16();    //opcode
                var targetId = (int)reader.ReadVarInt();
                var mask = reader.ReadVarInt();
                if (mask > 255) return null;
                reader.ReadVarInt(); // unknown flag
                var actorId = (int)reader.ReadVarInt();
                var code = (int)reader.ReadU32();
                return actorId != targetId ? (actorId, targetId, code, (long)mask) : null;
            }
            catch (Exception) { return null; }
        }

        private int ProcessSwitchValue(int switchVar)
        {
            if (switchVar > 255) return -1;
            int switchVal = switchVar & 0x0F;
            if (switchVal == 0 || (uint)(switchVal - 4) > 3) return -1;
            return switchVal;
        }

        private SpecialFlags ReadSpecialBytesBlock(PacketReader reader, int switchValue)
        {
            SpecialFlags flags = default;
            byte attackDirectionType = 0;
            byte damageFlagByte = 0;
            if (switchValue == 4)
            {
                if (reader.Remaining < 8) throw new PacketProcessException("Not enough bytes remaining for special flags");
            }
            else
            {
                if (reader.Remaining < 12) throw new PacketProcessException("Not enough bytes remaining for special flags");
                damageFlagByte = reader.ReadU8();
                reader.ReadU8(); // unknown
                attackDirectionType = reader.ReadU8();
            }

            flags = ParseSpecialFlags(damageFlagByte);

            flags.IsBackAttack = (attackDirectionType & 0x01) != 0;
            flags.IsFrontAttack = (attackDirectionType & 0x02) != 0;

            reader.ReadU32(); // unknown

            reader.Skip(4); //unknown tail bytes

            return flags;

        }

        private static ParsedDamagePacket Fail(PacketProcessResult result) => new(null, result);
        private record ParsedDamagePacket(DamagePacketData? Data, PacketProcessResult Result)
        {
            public bool IsValid => Result == PacketProcessResult.SUCCES;
        }

        private struct SpecialFlags
        {
            public bool IsBackAttack { get; set; }
            public bool IsFrontAttack { get; set; }
            public bool IsParry { get; init; }
            public bool IsPerfect { get; init; }
            public bool IsDoubleDamage { get; init; }
        }

        private static SpecialFlags ParseSpecialFlags(byte flagByte) => new()
        {
            IsBackAttack = (flagByte & 0x01) != 0,
            IsParry = (flagByte & 0x02) != 0,
            IsPerfect = (flagByte & 0x04) != 0,
            IsDoubleDamage = (flagByte & 0x08) != 0
        };


        private  class DamagePacketData
        {
            public int TargetId { get; init; }
            public int ActorId { get; init; }
            public int SkillCode { get; init; }
            public int DamageType { get; init; }
            public long Damage { get; init; }
            public bool IsCritical { get; init; }
            public bool IsBackAttack { get; init; }
            public bool IsFrontAttack { get; init; }
            public bool IsParry { get; init; }
            public bool IsPerfect { get; init; }
            public bool IsDoubleDamage { get; init; }
            public DateTime Timestamp { get; init; } = DateTime.Now;
            public int UnknownVarInt { get; init; }

            public int MultihitCount { get; init; }
            public int[] MultihitDamageArray { get; init; } = Array.Empty<int>();
        }
    }
}
