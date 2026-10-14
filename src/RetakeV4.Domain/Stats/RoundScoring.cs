namespace RetakeV4.Domain.Stats;

public static class RoundScoring
{
    public static RoundSummary Process(RoundRecord record, StatsRules rules) => Score(RoundAnalyzer.Analyze(record, rules), rules);

    public static RoundSummary Score(RoundSummary summary, StatsRules rules)
    {
        ArgumentNullException.ThrowIfNull(summary);
        ArgumentNullException.ThrowIfNull(rules);
        var record = summary.Record;
        var deltas = EloCalculator.Deltas(record.Context.Participants, record.Outcome.Winner, record.Ranked, rules.Elo);
        var players = summary.Players
            .Select(p => p with
            {
                Rating = RetakeRating.For(p, rules.Rating),
                EloDelta = deltas[p.SteamId],
                EloAfter = EloCalculator.Apply(p.EloBefore, deltas[p.SteamId], rules.Elo),
            })
            .ToList();
        return summary with { Players = players };
    }
}
