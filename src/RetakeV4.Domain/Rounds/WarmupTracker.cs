namespace RetakeV4.Domain.Rounds;

public sealed record WarmupSnapshot(bool IsWarmup, float WarmupPeriodEnd, float Now);

// Settled = the map's first warmup is over (ended naturally or forced): later warmups
// (admin mp_warmup_start, spawn editor pause) must never be cut by the watchdog.
public sealed record WarmupTracker(float FallbackSeconds, float? WarmupSeenAt, bool Settled)
{
    public static WarmupTracker Start(float fallbackSeconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(fallbackSeconds);
        return new WarmupTracker(fallbackSeconds, null, false);
    }

    // An admin-driven warmup (spawn editor) must never be cut by the watchdog, even during the map's first warmup.
    public WarmupTracker Settle() => this with { Settled = true };

    public (WarmupTracker Next, bool ForceEnd) Evaluate(WarmupSnapshot snapshot)
    {
        if (Settled)
        {
            return (this, false);
        }
        if (!snapshot.IsWarmup)
        {
            return WarmupSeenAt is null ? (this, false) : (this with { Settled = true }, false);
        }

        var seenAt = WarmupSeenAt ?? snapshot.Now;
        // An endless warmup reports no end as 0 or, on some hosts, +Infinity: both mean "no timer, use the fallback".
        var hasTimer = float.IsFinite(snapshot.WarmupPeriodEnd) && snapshot.WarmupPeriodEnd > 0f;
        var timerElapsed = hasTimer && snapshot.Now >= snapshot.WarmupPeriodEnd;
        var fallbackElapsed = !hasTimer
            && FallbackSeconds > 0f
            && snapshot.Now - seenAt >= FallbackSeconds;

        return timerElapsed || fallbackElapsed
            ? (this with { WarmupSeenAt = seenAt, Settled = true }, true)
            : (this with { WarmupSeenAt = seenAt }, false);
    }
}
