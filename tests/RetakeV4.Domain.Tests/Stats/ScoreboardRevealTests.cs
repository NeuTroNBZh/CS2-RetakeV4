using RetakeV4.Domain.Stats;

namespace RetakeV4.Domain.Tests.Stats;

public class ScoreboardRevealTests
{
    private const ulong Tab = 1UL << 33;
    private const ulong Jump = 1UL << 1;

    [Fact]
    public void FirstPoll_WithTabHeld_IsAPress() => Assert.True(new ScoreboardReveal(Tab).Pressed(3, Tab));

    [Fact]
    public void TabStillHeld_IsNotPressedAgain()
    {
        var reveal = new ScoreboardReveal(Tab);
        reveal.Pressed(3, Tab);

        Assert.False(reveal.Pressed(3, Tab | Jump));
    }

    [Fact]
    public void TabReleasedThenPressed_IsAPressAgain()
    {
        var reveal = new ScoreboardReveal(Tab);
        reveal.Pressed(3, Tab);
        reveal.Pressed(3, 0);

        Assert.True(reveal.Pressed(3, Tab));
    }

    [Fact]
    public void OtherButtons_AreNotAPress() => Assert.False(new ScoreboardReveal(Tab).Pressed(3, Jump));

    [Fact]
    public void Slots_AreTrackedSeparately()
    {
        var reveal = new ScoreboardReveal(Tab);
        reveal.Pressed(3, Tab);

        Assert.True(reveal.Pressed(4, Tab));
    }

    [Fact]
    public void ForgottenSlot_StartsOver()
    {
        var reveal = new ScoreboardReveal(Tab);
        reveal.Pressed(3, Tab);
        reveal.Forget(3);

        Assert.True(reveal.Pressed(3, Tab));
    }

    [Fact]
    public void Clear_ForgetsEverySlot()
    {
        var reveal = new ScoreboardReveal(Tab);
        reveal.Pressed(3, Tab);
        reveal.Pressed(4, Tab);
        reveal.Clear();

        Assert.True(reveal.Pressed(3, Tab));
        Assert.True(reveal.Pressed(4, Tab));
    }
}
