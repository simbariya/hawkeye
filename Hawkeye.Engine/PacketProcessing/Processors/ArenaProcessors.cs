using Hawkeye.Engine.PacketProcessing.Routing;
using Hawkeye.Engine.Services.Arena;

namespace Hawkeye.Engine.PacketProcessing.Processors
{
    /// <summary>The arena roster as a match loads (0xE319).</summary>
    [PacketOpcode(PacketOpcodes.ArenaRoster)]
    public sealed class ArenaRosterProcessor(ArenaTracker arena) : IOpcodeProcessor
    {
        public void Process(Packet packet)
        {
            if (ArenaParser.ParseRoster(packet.Data) is { } players) arena.OnRoster(players);
        }
    }

    /// <summary>A kill in an arena match (0xE325).</summary>
    [PacketOpcode(PacketOpcodes.ArenaKill)]
    public sealed class ArenaKillProcessor(ArenaTracker arena) : IOpcodeProcessor
    {
        public void Process(Packet packet)
        {
            if (ArenaParser.ParseKill(packet.Data) is { } kill) arena.OnKill(kill);
        }
    }

    /// <summary>The end of an arena match, with your new rating (0xE328).</summary>
    [PacketOpcode(PacketOpcodes.ArenaResult)]
    public sealed class ArenaResultProcessor(ArenaTracker arena) : IOpcodeProcessor
    {
        public void Process(Packet packet)
        {
            if (ArenaParser.ParseResultRating(packet.Data) is { } rating) arena.OnResult(rating);
        }
    }

    /// <summary>Your arena rating, wins and losses per mode, sent at every zone change (0xE32A).</summary>
    [PacketOpcode(PacketOpcodes.ArenaStandings)]
    public sealed class ArenaStandingsProcessor(ArenaTracker arena) : IOpcodeProcessor
    {
        public void Process(Packet packet)
        {
            if (ArenaParser.ParseStandings(packet.Data) is { } list) arena.OnStandings(list);
        }
    }
}
