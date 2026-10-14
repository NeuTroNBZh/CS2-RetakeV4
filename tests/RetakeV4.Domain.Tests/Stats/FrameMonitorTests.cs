using RetakeV4.Domain.Stats;

namespace RetakeV4.Domain.Tests.Stats;

public class FrameMonitorTests
{
    [Fact]
    public void FirstTick_MeasuresNothing() => Assert.Null(new FrameMonitor(50).Tick(1000));

    [Fact]
    public void ShortFrames_AreIgnored_LongOnesReported()
    {
        var monitor = new FrameMonitor(50);
        monitor.Tick(0);
        Assert.Null(monitor.Tick(16));
        Assert.Equal(50, monitor.Tick(66));
        Assert.Equal(134, monitor.Tick(200));
    }

    // Hibernation and map loads stop the ticks: that gap is not a frame.
    [Fact]
    public void GapsOfTenSecondsOrMore_AreAPause()
    {
        var monitor = new FrameMonitor(50);
        monitor.Tick(0);
        Assert.Null(monitor.Tick(FrameMonitor.MaxGapMs));
        Assert.Equal(60, monitor.Tick(FrameMonitor.MaxGapMs + 60));
    }

    [Fact]
    public void Reset_ForgetsTheLastFrame()
    {
        var monitor = new FrameMonitor(50);
        monitor.Tick(0);
        monitor.Reset();
        Assert.Null(monitor.Tick(5000));
    }

    [Fact]
    public void Threshold_MustBePositive() => Assert.Throws<ArgumentOutOfRangeException>(() => new FrameMonitor(0));
}
