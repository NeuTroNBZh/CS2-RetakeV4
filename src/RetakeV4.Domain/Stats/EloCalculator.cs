using RetakeV4.Domain.Common;

namespace RetakeV4.Domain.Stats;

// Team ELO: each side's rating is its mean ELO plus PlayerAdvantage per extra player; every member of a side gets the
// same delta and the two sides get opposite deltas.
public static class EloCalculator
{
    public static IReadOnlyDictionary<ulong, int> Deltas(IReadOnlyList<RoundParticipant> participants, TeamSide? winner, bool ranked, EloRules rules)
    {
        ArgumentNullException.ThrowIfNull(participants);
        ArgumentNullException.ThrowIfNull(rules);
        var deltas = participants.ToDictionary(p => p.SteamId, _ => 0);
        var ct = participants.Where(p => p.Side == TeamSide.CT).ToList();
        var t = participants.Where(p => p.Side == TeamSide.T).ToList();
        if (!ranked || winner is null || ct.Count == 0 || t.Count == 0)
        {
            return deltas;
        }
        var ratingCt = ct.Average(p => (double)p.EloBefore) + rules.PlayerAdvantage * (ct.Count - t.Count);
        var ratingT = t.Average(p => (double)p.EloBefore) + rules.PlayerAdvantage * (t.Count - ct.Count);
        var expectedCt = 1.0 / (1.0 + Math.Pow(10, (ratingT - ratingCt) / 400.0));
        var scoreCt = winner == TeamSide.CT ? 1.0 : 0.0;
        var deltaCt = (int)Math.Round(rules.K * (scoreCt - expectedCt), MidpointRounding.AwayFromZero);
        foreach (var player in ct)
        {
            deltas[player.SteamId] = deltaCt;
        }
        foreach (var player in t)
        {
            deltas[player.SteamId] = -deltaCt;
        }
        return deltas;
    }

    public static int Apply(int before, int delta, EloRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        return Math.Max(rules.Floor, before + delta);
    }
}
