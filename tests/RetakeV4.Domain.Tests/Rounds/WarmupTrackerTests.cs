using RetakeV4.Domain.Rounds;

namespace RetakeV4.Domain.Tests.Rounds;

public class WarmupTrackerTests
{
    [Fact]
    public void NotWarmup_NeverForces()
    {
        var (_, force) = WarmupTracker.Start(16f, 0f).Evaluate(new WarmupSnapshot(false, 0f, 999f));
        Assert.False(force);
    }

    [Fact]
    public void TimedWarmup_ForcesWhenTimerElapsed()
    {
        var (_, force) = WarmupTracker.Start(16f, 0f).Evaluate(new WarmupSnapshot(true, 30f, 30f));
        Assert.True(force);
    }

    [Fact]
    public void TimedWarmup_DoesNotForceBeforeTimer()
    {
        var (_, force) = WarmupTracker.Start(16f, 0f).Evaluate(new WarmupSnapshot(true, 30f, 29.9f));
        Assert.False(force);
    }

    [Fact]
    public void CompetitiveWarmup_UsesFallbackFromMapStart()
    {
        var tracker = WarmupTracker.Start(16f, 100f);
        Assert.False(tracker.Evaluate(new WarmupSnapshot(true, 0f, 115.9f)).ForceEnd);
        Assert.True(tracker.Evaluate(new WarmupSnapshot(true, 0f, 116f)).ForceEnd);
    }

    [Fact]
    public void ForcesOnlyOncePerMap()
    {
        var (next, first) = WarmupTracker.Start(16f, 0f).Evaluate(new WarmupSnapshot(true, 0f, 20f));
        var (_, second) = next.Evaluate(new WarmupSnapshot(true, 0f, 21f));
        Assert.True(first);
        Assert.False(second);
    }

    [Fact]
    public void NewMap_RearmsTheWatchdog()
    {
        var (afterFirstMap, _) = WarmupTracker.Start(16f, 0f).Evaluate(new WarmupSnapshot(true, 0f, 20f));
        Assert.True(afterFirstMap.ForcedThisMap);
        var secondMap = WarmupTracker.Start(afterFirstMap.FallbackSeconds, 500f);
        Assert.True(secondMap.Evaluate(new WarmupSnapshot(true, 0f, 516f)).ForceEnd);
    }

    [Fact]
    public void ZeroFallback_DisablesCompetitiveFallback()
    {
        Assert.False(WarmupTracker.Start(0f, 0f).Evaluate(new WarmupSnapshot(true, 0f, 10_000f)).ForceEnd);
    }

    [Fact]
    public void NegativeFallback_IsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => WarmupTracker.Start(-1f, 0f));
    }
}
