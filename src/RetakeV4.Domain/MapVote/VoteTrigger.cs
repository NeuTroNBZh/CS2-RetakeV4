namespace RetakeV4.Domain.MapVote;

public static class VoteTrigger
{
    // Checked at round start: roundsPlayed counts finished rounds, so 30 - 27 = 3 rounds left including this one.
    public static bool ShouldOpen(int maxRounds, int roundsPlayed, int triggerRoundsBeforeEnd, bool warmup, bool alreadyVoted) =>
        maxRounds > 0 && !warmup && !alreadyVoted && maxRounds - roundsPlayed <= triggerRoundsBeforeEnd;
}
