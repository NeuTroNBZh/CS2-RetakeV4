using RetakeV4.Domain.Stats;

namespace RetakeV4.Domain.Events;

// Published on the game thread once a round is analyzed and scored, before it is written.
public sealed record RoundStatsRecorded(RoundSummary Summary);
