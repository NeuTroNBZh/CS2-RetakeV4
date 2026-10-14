using RetakeV4.Domain.Stats;

namespace RetakeV4.Domain.Tests.Stats;

public class PlayerInputTrackerTests
{
    private const ulong Tab = 1UL << 33;
    private const ulong Jump = 1UL << 1;
    private const ulong Duck = 1UL << 2;

    private static PlayerInputTracker Tracker() => new(Tab, Jump);

    [Fact]
    public void FirstPoll_WithTabHeld_IsAScoreboardPress() => Assert.True(Tracker().Update(3, Tab, onGround: true).Scoreboard);

    [Fact]
    public void TabStillHeld_IsNotPressedAgain()
    {
        var tracker = Tracker();
        tracker.Update(3, Tab, true);
        Assert.False(tracker.Update(3, Tab | Duck, true).Scoreboard);
    }

    [Fact]
    public void TabReleasedThenPressed_IsAPressAgain()
    {
        var tracker = Tracker();
        tracker.Update(3, Tab, true);
        tracker.Update(3, 0, true);
        Assert.True(tracker.Update(3, Tab, true).Scoreboard);
    }

    [Fact]
    public void OtherButtons_AreNotAScoreboardPress() => Assert.False(Tracker().Update(3, Duck, true).Scoreboard);

    [Fact]
    public void JumpPressedOnTheGround_IsAJump() => Assert.True(Tracker().Update(3, Jump, onGround: true).Jumped);

    [Fact]
    public void JumpPressedOnTheTickTheGroundIsLeft_IsAJump()
    {
        var tracker = Tracker();
        tracker.Update(3, 0, onGround: true);
        Assert.True(tracker.Update(3, Jump, onGround: false).Jumped);
    }

    [Fact]
    public void JumpPressedInTheAir_IsNotAJump()
    {
        var tracker = Tracker();
        tracker.Update(3, 0, onGround: false);
        Assert.False(tracker.Update(3, Jump, onGround: false).Jumped);
    }

    [Fact]
    public void FirstPollInTheAir_IsNotAJump() => Assert.False(Tracker().Update(3, Jump, onGround: false).Jumped);

    [Fact]
    public void JumpHeld_CountsOnce()
    {
        var tracker = Tracker();
        tracker.Update(3, Jump, true);
        Assert.False(tracker.Update(3, Jump, true).Jumped);
    }

    [Fact]
    public void Slots_AreTrackedSeparately()
    {
        var tracker = Tracker();
        tracker.Update(3, Tab, true);
        Assert.True(tracker.Update(4, Tab, true).Scoreboard);
    }

    [Fact]
    public void ForgottenSlot_StartsOver()
    {
        var tracker = Tracker();
        tracker.Update(3, Tab, true);
        tracker.Forget(3);
        Assert.True(tracker.Update(3, Tab, true).Scoreboard);
    }

    [Fact]
    public void Clear_ForgetsEverySlot()
    {
        var tracker = Tracker();
        tracker.Update(3, Tab, true);
        tracker.Update(4, Tab, true);
        tracker.Clear();
        Assert.True(tracker.Update(3, Tab, true).Scoreboard);
        Assert.True(tracker.Update(4, Tab, true).Scoreboard);
    }
}
