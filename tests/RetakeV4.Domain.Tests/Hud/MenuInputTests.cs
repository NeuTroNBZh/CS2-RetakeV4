using RetakeV4.Domain.Geometry;
using RetakeV4.Domain.Hud;
using RetakeV4.Domain.Rounds;

namespace RetakeV4.Domain.Tests.Hud;

public class MenuInputTests
{
    [Fact]
    public void LiveRound_AlivePlayer_OnlyGetsKeys() =>
        Assert.Equal(new MenuControls(false, false, true), MenuInput.For(RoundPhase.Live, true, MenuInputSetting.AimAndKeys));

    [Theory]
    [InlineData(RoundPhase.FreezeTime, true)]
    [InlineData(RoundPhase.Live, false)]
    [InlineData(RoundPhase.Warmup, true)]
    [InlineData(RoundPhase.PostRound, true)]
    public void OtherwiseEverythingIsAvailable(RoundPhase phase, bool alive) =>
        Assert.Equal(new MenuControls(true, true, true), MenuInput.For(phase, alive, MenuInputSetting.AimAndKeys));

    [Fact]
    public void KeysSetting_DisablesAim() =>
        Assert.Equal(new MenuControls(false, true, true), MenuInput.For(RoundPhase.FreezeTime, true, MenuInputSetting.Keys));

    [Fact]
    public void CenteredLayout_PutsTheMiddleLineInFrontOfTheEyes()
    {
        var layout = AimMenuLayout.Centered(5, 60f, 4f, 18f);
        Assert.Equal(8f, layout.FirstLineUpUnits);
        var straight = new ViewAngles(0f, 90f);
        Assert.Equal(2, AimLineResolver.Resolve(layout, straight, straight));
    }
}
