namespace RetakeV4.Domain.Rounds;

public sealed record WarmupSnapshot(bool IsWarmup, float WarmupPeriodEnd, float Now);

public sealed record WarmupTracker(float FallbackSeconds, float MapStartedAt, bool ForcedThisMap)
{
    public static WarmupTracker Start(float fallbackSeconds, float now)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(fallbackSeconds);
        return new WarmupTracker(fallbackSeconds, now, false);
    }

    public (WarmupTracker Next, bool ForceEnd) Evaluate(WarmupSnapshot snapshot)
    {
        if (ForcedThisMap || !snapshot.IsWarmup)
        {
            return (this, false);
        }

        var timerElapsed = snapshot.WarmupPeriodEnd > 0f && snapshot.Now >= snapshot.WarmupPeriodEnd;
        var fallbackElapsed = snapshot.WarmupPeriodEnd <= 0f
            && FallbackSeconds > 0f
            && snapshot.Now - MapStartedAt >= FallbackSeconds;

        return timerElapsed || fallbackElapsed
            ? (this with { ForcedThisMap = true }, true)
            : (this, false);
    }
}
