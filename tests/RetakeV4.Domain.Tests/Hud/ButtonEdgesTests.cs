using RetakeV4.Domain.Hud;

namespace RetakeV4.Domain.Tests.Hud;

public class ButtonEdgesTests
{
    private const ulong Forward = 1 << 3;
    private const ulong Use = 1 << 5;

    [Fact]
    public void NewlyHeldButtons_ArePressed() => Assert.Equal(Forward, ButtonEdges.Pressed(0, Forward));

    // Holding a key must move the cursor once, not every tick.
    [Fact]
    public void ButtonsStillHeld_AreNotPressedAgain() => Assert.Equal(0UL, ButtonEdges.Pressed(Forward, Forward));

    [Fact]
    public void OnlyTheNewButton_IsPressed() => Assert.Equal(Use, ButtonEdges.Pressed(Forward, Forward | Use));

    [Fact]
    public void ReleasedButtons_AreNotPressed() => Assert.Equal(0UL, ButtonEdges.Pressed(Forward | Use, 0));
}
