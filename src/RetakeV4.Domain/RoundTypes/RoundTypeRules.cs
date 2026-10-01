namespace RetakeV4.Domain.RoundTypes;

public sealed record RoundTypeSequenceEntry(string RoundType, int Count);

public sealed record RoundTypeRules(
    RoundTypeMode Mode,
    IReadOnlyList<string> Available,
    IReadOnlyList<RoundTypeSequenceEntry> Sequence,
    string Specific);
