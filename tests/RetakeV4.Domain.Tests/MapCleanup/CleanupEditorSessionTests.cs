using RetakeV4.Domain.MapCleanup;

namespace RetakeV4.Domain.Tests.MapCleanup;

public class CleanupEditorSessionTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 1, 20, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Idle_AfterTheLimitWithoutActivity()
    {
        var session = new CleanupEditorSession(3, T0);
        Assert.False(session.IsIdle(T0 + CleanupEditorSession.IdleLimit - TimeSpan.FromSeconds(1)));
        Assert.True(session.IsIdle(T0 + CleanupEditorSession.IdleLimit));
        Assert.False(session.Touched(T0 + TimeSpan.FromMinutes(2)).IsIdle(T0 + CleanupEditorSession.IdleLimit));
    }

    [Theory]
    [InlineData(3, "weapons.main", false, true)]
    [InlineData(3, "weapons.main", true, false)]
    [InlineData(3, CleanupEditorMenu.MenuId, false, false)]
    [InlineData(4, "weapons.main", false, false)]
    public void AnotherMenuOpenedForTheEditor_EndsTheSession(int slot, string menuId, bool refreshOnly, bool expected)
    {
        Assert.Equal(expected, new CleanupEditorSession(3, T0).IsReplacedBy(slot, menuId, refreshOnly));
    }
}
