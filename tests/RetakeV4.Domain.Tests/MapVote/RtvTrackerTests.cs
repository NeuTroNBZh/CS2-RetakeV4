using RetakeV4.Domain.MapVote;

namespace RetakeV4.Domain.Tests.MapVote;

public class RtvTrackerTests
{
    [Theory]
    [InlineData(1, 60, 1)]
    [InlineData(2, 60, 2)]
    [InlineData(5, 60, 3)]
    [InlineData(10, 60, 6)]
    [InlineData(0, 60, 1)]
    public void Needed_RoundsUp_AtLeastOne(int humans, int percentage, int expected)
    {
        Assert.Equal(expected, RtvTracker.Needed(humans, percentage));
    }

    // Review Focus 4: a departure lowers the threshold and can reach it.
    [Fact]
    public void Departure_CanReachTheThreshold()
    {
        var rtv = RtvTracker.Empty.Want(1).Want(2).Want(2);
        Assert.False(rtv.IsReached(5, 60));
        Assert.True(rtv.Left(3).IsReached(3, 60));
        Assert.Single(rtv.Left(2).Wanting);
    }

    [Theory]
    [InlineData(false, false, 5, 0, RtvRefusal.Disabled)]
    [InlineData(true, true, 5, 0, RtvRefusal.Warmup)]
    [InlineData(true, false, 1, 5, RtvRefusal.NotEnoughPlayers)]
    [InlineData(true, false, 5, 2, RtvRefusal.TooEarly)]
    public void Check_Refuses(bool enabled, bool warmup, int humans, int played, RtvRefusal expected)
    {
        Assert.Equal(expected, RtvTracker.Check(enabled, warmup, humans, 2, played, 3));
    }

    [Fact]
    public void Check_Accepts() => Assert.Null(RtvTracker.Check(true, false, 2, 2, 3, 3));
}
