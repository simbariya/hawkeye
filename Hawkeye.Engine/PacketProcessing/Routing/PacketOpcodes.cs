namespace Hawkeye.Engine.PacketProcessing.Routing;

public static class PacketOpcodes
{
    public const ushort ServerTime = 0x3603;          // 03 36
    public const ushort Damage = 0x3804;              // 04 38
    public const ushort DotDamage = 0x3805;           // 05 38
    public const ushort CompressedStream = 0xFFFF;    // FF FF
    public const ushort RemainHp = 0x8D00;            // 00 8D
    public const ushort MobSummon = 0x3641;           // 41 36
    public const ushort BuffEffectA = 0x382A;         // 2A 38
    public const ushort BuffEffectB = 0x382B;         // 2B 38
    public const ushort BuffRemoved = 0x382C;         // 2C 38  ([entity][count][reason][instance]..., v0.2.19)
    public const ushort PlayerInfo = 0x3633;          // 33 36
    public const ushort OtherPlayersInfo = 0x3645;    // 45 36
    public const ushort GlobalSessIdLinking = 0x3620; // 20 36
    public const ushort PlayerStats = 0x3649;         // 49 36
    public const ushort PartyInfo = 0x9702;           // 02 97
    public const ushort EntityDeath = 0x8D04;         // 04 8D
    public const ushort SkillCdUpdate = 0x3847;       // 47 38
    public const ushort SkillCast = 0x3802;           // 02 38
    public const ushort Counters = 0x610B;            // 0B 61  (Odyle is entry 1; see Services/Odyle)
    public const ushort CounterUpdate = 0x610C;       // 0C 61  (one counter changed, e.g. Odyle spent)
    public const ushort ItemCount = 0x561B;           // 1B 56  (an item's count changed; AP is an item)
    public const ushort RankPoints = 0xE273;          // 73 E2  (your rank points total)
    public const ushort Inventory = 0x5611;           // 11 56  (login inventory: [key][u64 count] records, AP among them)
    public const ushort OwnRanking = 0x8D57;          // 57 8D  (your line in a PvP ranking)
    public const ushort RankingTable = 0x8D55;        // 55 8D  (a PvP ranking table, top 500)
    public const ushort ArenaRoster = 0xE319;         // 19 E3  (arena: who is in the match, with ratings; v0.2.26)
    public const ushort ArenaKill = 0xE325;           // 25 E3  (arena: killer key, victim key)
    public const ushort ArenaResult = 0xE328;         // 28 E3  (arena: match over, your new rating)
    public const ushort ArenaStandings = 0xE32A;      // 2A E3  (arena: rating, wins, losses per mode, at zone changes)
    public const ushort ArenaOwnKey = 0xE343;         // 43 E3  (arena: your key, as you enter; v0.2.28)
    public const ushort SkillList = 0x5100;           // 00 51  (your skills with their levels, at login / zone change; v0.2.34)
    public const ushort OwnCombatPower = 0x3656;      // 56 36  (your combat power: u64 current at 3, u64 highest at 11; v0.2.35)
}