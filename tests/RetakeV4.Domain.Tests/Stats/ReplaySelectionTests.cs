using RetakeV4.Domain.Common;
using RetakeV4.Domain.Stats;
using static RetakeV4.Domain.Tests.Stats.StatsTestData;

namespace RetakeV4.Domain.Tests.Stats;

public class ReplaySelectionTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 14, 20, 0, 0, TimeSpan.Zero);
    private static readonly ReplayCaps AllOn = new(10, 10, 20);

    private static ReplayOwnership Held(long roundId, double score, int minutes) => new(roundId, score, T0.AddMinutes(minutes));

    private static ReplayRound Tracked(params (ulong Id, TeamSide Side)[] players) =>
        new(8, players.Select(p => new ReplayTrack(p.Id, p.Side, "[]", 0)).ToList());

    private static RoundSummary Scored(RoundRecord record) => RoundScoring.Process(record, Rules);

    // --- Decide ---

    [Fact]
    public void Decide_CapZero_IsSkipped() =>
        Assert.Equal(ReplayDecision.Skip, ReplaySelection.Decide(9, new ReplayClaim(Ct1, ReplayReason.Best, 3.0), Array.Empty<ReplayOwnership>(), 0));

    [Fact]
    public void Decide_BelowTheCap_KeepsWithoutEviction()
    {
        var decision = ReplaySelection.Decide(9, new ReplayClaim(Ct1, ReplayReason.Best, 0.1), new[] { Held(1, 2.0, 0) }, 2);

        Assert.True(decision.Keep);
        Assert.Empty(decision.Evict);
    }

    [Fact]
    public void Decide_AlreadyOwned_IsSkipped() =>
        Assert.Equal(ReplayDecision.Skip, ReplaySelection.Decide(1, new ReplayClaim(Ct1, ReplayReason.Saved, 1.0), new[] { Held(1, 1.0, 0) }, 20));

    [Fact]
    public void Decide_BestWhenFull_ReplacesTheWeakest()
    {
        var held = new[] { Held(1, 1.5, 0), Held(2, 0.8, 1), Held(3, 2.0, 2) };

        var decision = ReplaySelection.Decide(9, new ReplayClaim(Ct1, ReplayReason.Best, 0.9), held, 3);

        Assert.True(decision.Keep);
        Assert.Equal(new long[] { 2 }, decision.Evict);
    }

    [Theory]
    [InlineData(0.8)]
    [InlineData(0.5)]
    public void Decide_BestWhenFull_NotStrictlyBetter_IsSkipped(double score)
    {
        var held = new[] { Held(1, 1.5, 0), Held(2, 0.8, 1) };

        Assert.Equal(ReplayDecision.Skip, ReplaySelection.Decide(9, new ReplayClaim(Ct1, ReplayReason.Best, score), held, 2));
    }

    // Equal weakest scores: the oldest goes first, then the lowest round id.
    [Fact]
    public void Decide_BestTies_EvictTheOldest()
    {
        var held = new[] { Held(5, 1.0, 3), Held(4, 1.0, 2), Held(6, 1.0, 2) };

        var decision = ReplaySelection.Decide(9, new ReplayClaim(Ct1, ReplayReason.Best, 1.2), held, 3);

        Assert.Equal(new long[] { 4 }, decision.Evict);
    }

    // A cap lowered from 4 to 2: the next insertion trims the surplus (3 weakest out, 1 in).
    [Fact]
    public void Decide_LoweredCap_EvictsTheSurplus()
    {
        var held = new[] { Held(1, 1.0, 0), Held(2, 2.0, 1), Held(3, 3.0, 2), Held(4, 4.0, 3) };

        var decision = ReplaySelection.Decide(9, new ReplayClaim(Ct1, ReplayReason.Best, 5.0), held, 2);

        Assert.True(decision.Keep);
        Assert.Equal(new long[] { 1, 2, 3 }, decision.Evict);
    }

    // Lowered cap: the claim must land in the top cap together with the held replays, otherwise nothing is trimmed.
    [Fact]
    public void Decide_LoweredCap_ClaimOutsideTheTop_IsSkipped()
    {
        var held = new[] { Held(1, 1.0, 0), Held(2, 2.0, 1), Held(3, 3.0, 2), Held(4, 4.0, 3) };

        Assert.Equal(ReplayDecision.Skip, ReplaySelection.Decide(9, new ReplayClaim(Ct1, ReplayReason.Best, 1.5), held, 2));
    }

    [Fact]
    public void Decide_LoweredCap_ClaimInTheTop_EvictsEverythingOutside()
    {
        var held = new[] { Held(1, 1.0, 0), Held(2, 2.0, 1), Held(3, 3.0, 2), Held(4, 4.0, 3) };

        var decision = ReplaySelection.Decide(9, new ReplayClaim(Ct1, ReplayReason.Best, 3.5), held, 2);

        Assert.True(decision.Keep);
        Assert.Equal(new long[] { 1, 2, 3 }, decision.Evict);
    }

    [Theory]
    [InlineData(ReplayReason.Highlight)]
    [InlineData(ReplayReason.Saved)]
    public void Decide_HighlightAndSaved_ReplaceTheOldest_WhateverTheScore(ReplayReason reason)
    {
        var held = new[] { Held(7, 9.0, 5), Held(3, 9.0, 1), Held(8, 9.0, 1) };

        var decision = ReplaySelection.Decide(9, new ReplayClaim(Ct1, reason, 0.1), held, 3);

        Assert.True(decision.Keep);
        Assert.Equal(new long[] { 3 }, decision.Evict);
    }

    [Fact]
    public void Reasons_HaveTheirStoredNames()
    {
        Assert.Equal("best", ReplayReasons.Name(ReplayReason.Best));
        Assert.Equal("highlight", ReplayReasons.Name(ReplayReason.Highlight));
        Assert.Equal("saved", ReplayReasons.Name(ReplayReason.Saved));
    }

    [Fact]
    public void Caps_PerReason_AndAny()
    {
        var caps = new ReplayCaps(1, 2, 3);
        Assert.Equal(1, caps.For(ReplayReason.Best));
        Assert.Equal(2, caps.For(ReplayReason.Highlight));
        Assert.Equal(3, caps.For(ReplayReason.Saved));
        Assert.True(caps.Any);
        Assert.False(new ReplayCaps(0, 0, 0).Any);
    }

    // --- Claims ---

    // 2v3: Ct1 kills the three Ts (an ace); everyone tracked claims best, Ct1 also claims a highlight. Scores = ratings.
    [Fact]
    public void Claims_Ace_IsAHighlight_AndEveryTrackedPlayerClaimsBest()
    {
        var s = Scored(Record(Context(P(Ct1, TeamSide.CT), P(Ct2, TeamSide.CT), P(T1, TeamSide.T), P(T2, TeamSide.T), P(T3, TeamSide.T)), TeamSide.CT, r =>
        {
            r.Add(Kill(Ct1, T1, 1000));
            r.Add(Kill(Ct1, T2, 2000));
            r.Add(Kill(Ct1, T3, 3000));
        }));
        var replay = Tracked((Ct1, TeamSide.CT), (Ct2, TeamSide.CT), (T1, TeamSide.T));

        var claims = ReplaySelection.Claims(s, replay, AllOn);

        Assert.Equal(
            new[]
            {
                new ReplayClaim(Ct1, ReplayReason.Best, Of(s, Ct1).Rating),
                new ReplayClaim(Ct1, ReplayReason.Highlight, Of(s, Ct1).Rating),
                new ReplayClaim(Ct2, ReplayReason.Best, Of(s, Ct2).Rating),
                new ReplayClaim(T1, ReplayReason.Best, Of(s, T1).Rating),
            },
            claims);
    }

    // Ct2 dies first: Ct1 is alone against three and the CT side wins with a single kill: a won 1v3 clutch.
    [Fact]
    public void Claims_WonClutchOneVersusThree_IsAHighlight()
    {
        var s = Scored(Record(Context(P(Ct1, TeamSide.CT), P(Ct2, TeamSide.CT), P(T1, TeamSide.T), P(T2, TeamSide.T), P(T3, TeamSide.T)), TeamSide.CT, r =>
        {
            r.Add(Kill(T1, Ct2, 1000));
            r.Add(Kill(Ct1, T1, 2000));
        }));

        Assert.True(ReplaySelection.IsHighlight(s, Of(s, Ct1)));
        Assert.Contains(new ReplayClaim(Ct1, ReplayReason.Highlight, Of(s, Ct1).Rating), ReplaySelection.Claims(s, Tracked((Ct1, TeamSide.CT)), AllOn));
    }

    // A won 1v2 and an ace on two enemies are below HighlightMinOpponents; a lost 1v3 is not a won clutch.
    [Fact]
    public void Claims_SmallerOrLostClutchesAndAces_AreNotHighlights()
    {
        var oneVsTwo = Scored(Record(Context(P(Ct1, TeamSide.CT), P(T1, TeamSide.T), P(T2, TeamSide.T)), TeamSide.CT, r =>
        {
            r.Add(Kill(Ct1, T1, 1000));
            r.Add(Kill(Ct1, T2, 2000));
        }));
        var lostOneVsThree = Scored(Record(Context(P(Ct1, TeamSide.CT), P(T1, TeamSide.T), P(T2, TeamSide.T), P(T3, TeamSide.T)), TeamSide.T));

        Assert.False(ReplaySelection.IsHighlight(oneVsTwo, Of(oneVsTwo, Ct1)));
        Assert.False(ReplaySelection.IsHighlight(lostOneVsThree, Of(lostOneVsThree, Ct1)));
    }

    // best needs a ranked round (MinPlayersPerSide 2 with one CT: unranked); the highlight still counts.
    [Fact]
    public void Claims_UnrankedRound_ClaimsNoBest()
    {
        var s = Scored(Record(Context(P(Ct1, TeamSide.CT), P(T1, TeamSide.T), P(T2, TeamSide.T), P(T3, TeamSide.T)), TeamSide.CT, r =>
        {
            r.Add(Kill(Ct1, T1, 1000));
            r.Add(Kill(Ct1, T2, 2000));
            r.Add(Kill(Ct1, T3, 3000));
        }, minPlayersPerSide: 2));

        var claims = ReplaySelection.Claims(s, Tracked((Ct1, TeamSide.CT), (T1, TeamSide.T)), AllOn);

        Assert.Equal(new[] { new ReplayClaim(Ct1, ReplayReason.Highlight, Of(s, Ct1).Rating) }, claims);
    }

    [Fact]
    public void Claims_UntrackedPlayersAndZeroCaps_ClaimNothing()
    {
        var s = Scored(Record(Context(P(Ct1, TeamSide.CT), P(T1, TeamSide.T), P(T2, TeamSide.T), P(T3, TeamSide.T)), TeamSide.CT, r =>
        {
            r.Add(Kill(Ct1, T1, 1000));
            r.Add(Kill(Ct1, T2, 2000));
            r.Add(Kill(Ct1, T3, 3000));
        }));

        Assert.Empty(ReplaySelection.Claims(s, Tracked((T1, TeamSide.T)), new ReplayCaps(0, 10, 20)));
        Assert.Empty(ReplaySelection.Claims(s, Tracked((Ct1, TeamSide.CT)), new ReplayCaps(0, 0, 20)));
    }
}
