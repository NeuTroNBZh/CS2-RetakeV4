using System.Globalization;
using RetakeV4.Domain.Events;

namespace RetakeV4.Domain.Stats;

// One stats_server_events row. Occurrences counts aggregated events (the votes for map_vote); MaxMs is the worst
// duration for slow_handler and long_frame.
public sealed record ServerEvent(DateTimeOffset At, string Type, string? Module, string? Value, int Occurrences = 1, int? MaxMs = null);

// One stats_presence row.
public sealed record PresenceSnapshot(DateTimeOffset At, string MapName, int Humans, int Bots, int Queued);

public static class ServerEventTypes
{
    public const string PluginStart = "plugin_start";
    public const string HotReload = "hot_reload";
    public const string ModuleError = "module_error";
    public const string ModuleDisabled = "module_disabled";
    public const string SlowHandler = "slow_handler";
    public const string LongFrame = "long_frame";
    public const string Uptime = "uptime";
    public const string MapVote = "map_vote";
    public const string MapVoteResult = "map_vote_result";
    public const string Command = "command";
    public const string Chat = "chat";
}

public static class ServerEventFactory
{
    public static ServerEvent Started(DateTimeOffset at, bool hotReload, string version) =>
        new(at, hotReload ? ServerEventTypes.HotReload : ServerEventTypes.PluginStart, null, version);

    public static ServerEvent Uptime(DateTimeOffset at, TimeSpan uptime) =>
        new(at, ServerEventTypes.Uptime, null, ((long)Math.Max(0, uptime.TotalSeconds)).ToString(CultureInfo.InvariantCulture));

    // One map_vote row per map of the vote (Occurrences = votes, maps without a vote included), then map_vote_result.
    public static IReadOnlyList<ServerEvent> MapVoteRows(DateTimeOffset at, MapVoteClosed closed)
    {
        ArgumentNullException.ThrowIfNull(closed);
        return closed.Tally
            .Select(t => new ServerEvent(at, ServerEventTypes.MapVote, null, t.Map, t.Votes))
            .Append(new ServerEvent(at, ServerEventTypes.MapVoteResult, null, closed.Winner, closed.Votes))
            .ToList();
    }
}
