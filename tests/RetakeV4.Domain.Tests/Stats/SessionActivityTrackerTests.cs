using RetakeV4.Domain.Stats;

namespace RetakeV4.Domain.Tests.Stats;

public class SessionActivityTrackerTests
{
    [Fact]
    public void Ping_IsAveragedAndMaxed_NegativeSamplesIgnored()
    {
        var tracker = new SessionActivityTracker();
        tracker.Start(1);
        tracker.AddPing(1, 40);
        tracker.AddPing(1, 61);
        tracker.AddPing(1, -1);
        Assert.Equal(new SessionActivity(51, 61), tracker.Close(1));
    }

    [Fact]
    public void Ping_IsCapped()
    {
        var tracker = new SessionActivityTracker();
        tracker.Start(1);
        tracker.AddPing(1, 5000);
        Assert.Equal(new SessionActivity(SessionActivityTracker.MaxPing, SessionActivityTracker.MaxPing), tracker.Close(1));
    }

    [Fact]
    public void NoSample_GivesNoPing()
    {
        var tracker = new SessionActivityTracker();
        tracker.Start(1);
        Assert.Equal(new SessionActivity(null, null), tracker.Close(1));
    }

    [Fact]
    public void UntrackedPlayer_IsIgnored()
    {
        var tracker = new SessionActivityTracker();
        tracker.AddPing(2, 50);
        Assert.Equal(SessionActivity.None, tracker.Close(2));
    }

    [Fact]
    public void Start_ResetsTheSession()
    {
        var tracker = new SessionActivityTracker();
        tracker.Start(1);
        tracker.AddPing(1, 80);
        tracker.Start(1);
        Assert.Equal(SessionActivity.None, tracker.Close(1));
    }
}
