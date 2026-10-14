using System.Collections.Immutable;
using RetakeV4.Domain.Common;

namespace RetakeV4.Domain.MapVote;

public sealed record MapVoteCount(string Map, int Votes);

public sealed record MapVote(IReadOnlyList<string> Maps, ImmutableDictionary<int, string> Votes)
{
    public static MapVote Open(IReadOnlyList<string> maps) => new(maps, ImmutableDictionary<int, string>.Empty);

    public MapVote Cast(int slot, string map) => Maps.Contains(map) ? this with { Votes = Votes.SetItem(slot, map) } : this;

    public MapVote Remove(int slot) => this with { Votes = Votes.Remove(slot) };

    public string? Choice(int slot) => Votes.GetValueOrDefault(slot);

    // Votes per map, in list order; maps without a vote are included.
    public IReadOnlyList<MapVoteCount> Tally() => Maps.Select(m => new MapVoteCount(m, Votes.Values.Count(v => v == m))).ToList();

    // Most votes wins; a tie (or no vote at all) is drawn among the leaders, in list order.
    public (string Map, int Votes) Result(IRandom random)
    {
        if (Maps.Count == 0)
        {
            throw new InvalidOperationException("A map vote needs at least one map");
        }
        var counts = Tally();
        var best = counts.Max(c => c.Votes);
        var leaders = counts.Where(c => c.Votes == best).ToList();
        var winner = leaders[random.Next(leaders.Count)];
        return (winner.Map, winner.Votes);
    }
}
