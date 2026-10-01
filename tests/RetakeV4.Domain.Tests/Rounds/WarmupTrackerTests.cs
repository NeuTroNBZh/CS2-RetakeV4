using RetakeV4.Domain.Rounds;

namespace RetakeV4.Domain.Tests.Rounds;

public class WarmupTrackerTests
{
    private static (WarmupTracker Next, bool ForceEnd) Feed(WarmupTracker tracker, params WarmupSnapshot[] snapshots)
    {
        var result = (Next: tracker, ForceEnd: false);
        foreach (var snapshot in snapshots)
        {
            result = result.Next.Evaluate(snapshot);
        }
        return result;
    }

    [Fact]
    public void NotWarmup_NeverForces()
    {
        var (_, force) = WarmupTracker.Start(16f).Evaluate(new WarmupSnapshot(false, 0f, 999f));
        Assert.False(force);
    }

    [Fact]
    public void TimedWarmup_ForcesWhenTimerElapsed()
    {
        var (_, force) = WarmupTracker.Start(16f).Evaluate(new WarmupSnapshot(true, 30f, 30f));
        Assert.True(force);
    }

    [Fact]
    public void TimedWarmup_DoesNotForceBeforeTimer()
    {
        var (_, force) = WarmupTracker.Start(16f).Evaluate(new WarmupSnapshot(true, 30f, 29.9f));
        Assert.False(force);
    }

    [Fact]
    public void CompetitiveWarmup_UsesFallbackFromFirstWarmupObservation()
    {
        var (tracker, _) = WarmupTracker.Start(16f).Evaluate(new WarmupSnapshot(true, 0f, 100f));
        Assert.False(tracker.Evaluate(new WarmupSnapshot(true, 0f, 115.9f)).ForceEnd);
        Assert.True(tracker.Evaluate(new WarmupSnapshot(true, 0f, 116f)).ForceEnd);
    }

    [Fact]
    public void ForcesOnlyOncePerMap()
    {
        var (next, first) = Feed(WarmupTracker.Start(16f), new WarmupSnapshot(true, 0f, 0f), new WarmupSnapshot(true, 0f, 20f));
        var (_, second) = next.Evaluate(new WarmupSnapshot(true, 0f, 21f));
        Assert.True(first);
        Assert.False(second);
    }

    [Fact]
    public void WarmupEndedNaturally_ThenRestartedByAdmin_IsNotForced()
    {
        var (_, force) = Feed(
            WarmupTracker.Start(16f),
            new WarmupSnapshot(true, 0f, 0f),
            new WarmupSnapshot(false, 0f, 15f),
            new WarmupSnapshot(true, 0f, 300f),
            new WarmupSnapshot(true, 0f, 400f));
        Assert.False(force);
    }

    [Fact]
    public void BriefNonWarmupBeforeWarmupStarts_DoesNotDisarm()
    {
        var (_, force) = Feed(
            WarmupTracker.Start(16f),
            new WarmupSnapshot(false, 0f, 0f),
            new WarmupSnapshot(true, 0f, 1f),
            new WarmupSnapshot(true, 0f, 17f));
        Assert.True(force);
    }

    [Fact]
    public void AdminWarmupAfterHotReloadMidMatch_UsesFallbackFromItsOwnStart()
    {
        var (tracker, early) = Feed(
            WarmupTracker.Start(16f),
            new WarmupSnapshot(false, 0f, 500f),
            new WarmupSnapshot(true, 0f, 900f),
            new WarmupSnapshot(true, 0f, 910f));
        Assert.False(early);
        Assert.True(tracker.Evaluate(new WarmupSnapshot(true, 0f, 916f)).ForceEnd);
    }

    [Fact]
    public void NewMap_RearmsTheWatchdog()
    {
        var (afterFirstMap, _) = Feed(WarmupTracker.Start(16f), new WarmupSnapshot(true, 0f, 0f), new WarmupSnapshot(true, 0f, 20f));
        Assert.True(afterFirstMap.Settled);
        var (_, force) = Feed(WarmupTracker.Start(afterFirstMap.FallbackSeconds), new WarmupSnapshot(true, 0f, 500f), new WarmupSnapshot(true, 0f, 516f));
        Assert.True(force);
    }

    [Fact]
    public void ZeroFallback_DisablesCompetitiveFallback()
    {
        var (_, force) = Feed(WarmupTracker.Start(0f), new WarmupSnapshot(true, 0f, 0f), new WarmupSnapshot(true, 0f, 10_000f));
        Assert.False(force);
    }

    [Fact]
    public void NegativeFallback_IsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => WarmupTracker.Start(-1f));
    }

    [Fact]
    public void Settle_StopsTheWatchdog()
    {
        var tracker = WarmupTracker.Start(16f).Settle();
        var (_, forceEnd) = tracker.Evaluate(new WarmupSnapshot(true, 1f, 100f));
        Assert.False(forceEnd);
    }

    // Observed on Dathost: an endless warmup reports WarmupPeriodEnd = +Infinity, not 0.
    [Theory]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NaN)]
    public void EndlessWarmup_ReportedAsNonFinite_UsesTheFallback(float end)
    {
        var (_, early) = Feed(WarmupTracker.Start(16f), new WarmupSnapshot(true, end, 222f), new WarmupSnapshot(true, end, 230f));
        Assert.False(early);
        var (_, force) = Feed(WarmupTracker.Start(16f), new WarmupSnapshot(true, end, 222f), new WarmupSnapshot(true, end, 238f));
        Assert.True(force);
    }
}
