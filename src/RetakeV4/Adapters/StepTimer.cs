using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace RetakeV4.Adapters;

// Diagnostic: names the slow steps of a handler when their total crosses the threshold (the guard only gives the total).
internal sealed class StepTimer
{
    private static readonly TimeSpan Threshold = TimeSpan.FromMilliseconds(50);

    private readonly List<(string Step, TimeSpan Elapsed)> _steps = new();
    private long _last = Stopwatch.GetTimestamp();

    public void Mark(string step)
    {
        var now = Stopwatch.GetTimestamp();
        _steps.Add((step, Stopwatch.GetElapsedTime(_last, now)));
        _last = now;
    }

    public void ReportIfSlow(ILogger logger, string handler)
    {
        var total = _steps.Aggregate(TimeSpan.Zero, (sum, s) => sum + s.Elapsed);
        if (total >= Threshold)
        {
            logger.LogWarning("Slow steps in {Handler}: {Steps}", handler,
                string.Join(", ", _steps.Select(s => $"{s.Step} {(int)s.Elapsed.TotalMilliseconds} ms")));
        }
    }
}
