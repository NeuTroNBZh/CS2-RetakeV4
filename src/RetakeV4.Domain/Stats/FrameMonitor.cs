namespace RetakeV4.Domain.Stats;

// Measures the time between two server frames (monotonic milliseconds supplied by the caller). A gap of MaxGapMs or more
// is a pause (hibernation, map load), not a long frame.
public sealed class FrameMonitor
{
    public const int MaxGapMs = 10_000;

    private readonly int _thresholdMs;
    private double? _lastMs;

    public FrameMonitor(int thresholdMs)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(thresholdMs, 1);
        _thresholdMs = thresholdMs;
    }

    public void Reset() => _lastMs = null;

    // The frame duration in whole milliseconds when it reaches the threshold, else null.
    public int? Tick(double nowMs)
    {
        var last = _lastMs;
        _lastMs = nowMs;
        if (last is not { } previous)
        {
            return null;
        }
        var elapsed = nowMs - previous;
        return elapsed < _thresholdMs || elapsed >= MaxGapMs ? null : (int)Math.Round(elapsed);
    }
}
