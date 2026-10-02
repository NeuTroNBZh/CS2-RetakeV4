using RetakeV4.Domain.Remote;

namespace RetakeV4.Domain.Tests.Remote;

public class RoundClockTests
{
    [Theory]
    [InlineData("Live", 100f, 115, 172.2f, 43)]
    [InlineData("Live", 100f, 115, 215f, 0)]
    [InlineData("Live", 100f, 115, 300f, 0)]
    public void TimeLeft_InLiveRounds_IsRoundedUpAndNeverNegative(string phase, float start, int time, float now, int expected)
    {
        Assert.Equal(expected, RoundClock.TimeLeft(phase, start, time, now));
    }

    [Theory]
    [InlineData("Warmup")]
    [InlineData("FreezeTime")]
    [InlineData("Preparing")]
    [InlineData("PostRound")]
    public void TimeLeft_OutsideLiveRounds_IsNull(string phase)
    {
        Assert.Null(RoundClock.TimeLeft(phase, 100f, 115, 120f));
    }

    [Theory]
    [InlineData(false, false, "none")]
    [InlineData(true, false, "planted")]
    [InlineData(true, true, "defused")]
    [InlineData(false, true, "defused")]
    public void Bomb(bool planted, bool defused, string expected) => Assert.Equal(expected, RoundClock.Bomb(planted, defused));

    // Retake: the bomb is planted for the whole round, so its countdown is the time that matters.
    [Theory]
    [InlineData(150.4f, 120f, 31)]
    [InlineData(110f, 120f, 0)]
    public void TimeLeft_WithAPlantedBomb_IsTheBombCountdown(float c4Blow, float now, int expected)
    {
        Assert.Equal(expected, RoundClock.TimeLeft("Live", 100f, 115, now, c4Blow));
    }

    [Fact]
    public void TimeLeft_WithABombOutsideLiveRounds_IsNull()
    {
        Assert.Null(RoundClock.TimeLeft("PostRound", 100f, 115, 120f, 150f));
    }
}
