using RetakeV4.Domain.Stats;

namespace RetakeV4.Domain.Tests.Stats;

public class PlaytimeTrackerTests
{
    [Fact]
    public void TimeIsSplitByPresence()
    {
        var tracker = new PlaytimeTracker();
        tracker.Set(1, Presence.Ct, 0);
        tracker.Set(1, Presence.T, 10);
        tracker.Set(1, Presence.T, 12);
        tracker.Set(1, Presence.Queue, 20);
        tracker.Set(1, Presence.Spectator, 23);
        var playtime = tracker.Close(1, 30)!;
        Assert.Equal(new Playtime(10, 10, 7, 3), playtime);
        Assert.Equal(30, playtime.Total);
        Assert.False(tracker.IsTracked(1));
    }

    [Fact]
    public void Close_UnknownPlayer_IsNull() => Assert.Null(new PlaytimeTracker().Close(1, 10));

    [Fact]
    public void Reconnect_StartsAFreshSession()
    {
        var tracker = new PlaytimeTracker();
        tracker.Set(1, Presence.Ct, 0);
        Assert.Equal(10, tracker.Close(1, 10)!.Total);
        tracker.Set(1, Presence.Ct, 20);
        Assert.Equal(10, tracker.Close(1, 30)!.Total);
    }

    [Fact]
    public void CloseAll_ClosesEveryone()
    {
        var tracker = new PlaytimeTracker();
        tracker.Set(1, Presence.Ct, 0);
        tracker.Set(2, Presence.T, 5);
        var closed = tracker.CloseAll(10);
        Assert.Equal(2, closed.Count);
        Assert.Equal(10, closed.Single(c => c.Player == 1).Playtime.SecondsCt);
        Assert.Equal(5, closed.Single(c => c.Player == 2).Playtime.SecondsT);
        Assert.False(tracker.IsTracked(1));
    }

    [Fact]
    public void ClockGoingBack_CountsNothing()
    {
        var tracker = new PlaytimeTracker();
        tracker.Set(1, Presence.Ct, 100);
        Assert.Equal(0, tracker.Close(1, 50)!.Total);
    }

    [Fact]
    public void VipTime_CountsWhileFlaggedOnly()
    {
        var tracker = new PlaytimeTracker();
        tracker.Set(1, Presence.Queue, 0, vip: true);
        tracker.Set(1, Presence.Queue, 30, vip: false);
        tracker.Set(1, Presence.Ct, 40);
        var playtime = tracker.Close(1, 100)!;
        Assert.Equal(40, playtime.SecondsQueue);
        Assert.Equal(30, playtime.SecondsVip);
        Assert.Equal(60, playtime.SecondsCt);
    }

    [Fact]
    public void VipFlag_IsIgnoredOutsideTheQueue()
    {
        var tracker = new PlaytimeTracker();
        tracker.Set(1, Presence.Ct, 0, vip: true);
        Assert.Equal(0, tracker.Close(1, 50)!.SecondsVip);
    }

    [Fact]
    public void CloseAll_IncludesVipTime()
    {
        var tracker = new PlaytimeTracker();
        tracker.Set(1, Presence.Queue, 0, vip: true);
        tracker.Set(2, Presence.Queue, 0);
        var closed = tracker.CloseAll(20);
        Assert.Equal(20, closed.Single(c => c.Player == 1).Playtime.SecondsVip);
        Assert.Equal(0, closed.Single(c => c.Player == 2).Playtime.SecondsVip);
    }
}
