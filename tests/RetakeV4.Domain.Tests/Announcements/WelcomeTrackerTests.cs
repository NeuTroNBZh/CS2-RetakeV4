using RetakeV4.Domain.Announcements;

namespace RetakeV4.Domain.Tests.Announcements;

public class WelcomeTrackerTests
{
    [Fact]
    public void FirstJoin_Greets_ThenNeverAgainInTheSession()
    {
        var (tracker, first) = WelcomeTracker.Empty.Joined(1);
        var (tracker2, second) = tracker.Joined(1);
        Assert.True(first);
        Assert.False(second);
        Assert.False(tracker2.Joined(1).Greet);
    }

    [Fact]
    public void Reconnecting_StartsANewSession()
    {
        var (tracker, _) = WelcomeTracker.Empty.Joined(1);
        Assert.True(tracker.Left(1).Joined(1).Greet);
    }

    [Fact]
    public void Players_AreTrackedSeparately()
    {
        var (tracker, _) = WelcomeTracker.Empty.Joined(1);
        Assert.True(tracker.Joined(2).Greet);
    }
}
