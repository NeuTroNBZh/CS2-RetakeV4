using RetakeV4.Domain.Common;

namespace RetakeV4.Domain.RoundTypes;

public static class RoundTypeSelector
{
    public static string Select(RoundTypeRules rules, int roundsPlayed, IRandom random) => rules.Mode switch
    {
        RoundTypeMode.Sequence => FromSequence(rules, Math.Max(0, roundsPlayed)),
        RoundTypeMode.Random => rules.Available.Count == 0 ? rules.Specific : random.Pick(rules.Available),
        _ => rules.Specific,
    };

    private static string FromSequence(RoundTypeRules rules, int roundsPlayed)
    {
        var remaining = roundsPlayed;
        foreach (var entry in rules.Sequence)
        {
            if (entry.Count < 0 || remaining < entry.Count)
            {
                return entry.RoundType;
            }
            remaining -= entry.Count;
        }
        return rules.Sequence.Count > 0 ? rules.Sequence[^1].RoundType : rules.Specific;
    }
}
