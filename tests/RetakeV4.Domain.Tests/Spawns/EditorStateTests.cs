using RetakeV4.Domain.Spawns;

namespace RetakeV4.Domain.Tests.Spawns;

public class EditorStateTests
{
    [Fact]
    public void FirstEditor_PausesTheGame_OthersDoNot()
    {
        var (first, pause) = EditorState.Idle.Enter(1, isWarmupNow: false);
        Assert.True(pause);
        Assert.True(first.IsActive);
        var (second, pauseAgain) = first.Enter(2, isWarmupNow: true);
        Assert.False(pauseAgain);
        Assert.Equal(false, second.WarmupBefore);
        var (same, pauseThird) = second.Enter(2, isWarmupNow: true);
        Assert.False(pauseThird);
        Assert.Equal(second.Editors, same.Editors);
    }

    [Fact]
    public void LastEditorLeaving_RestoresTheGame()
    {
        var (state, _) = EditorState.Idle.Enter(1, isWarmupNow: false);
        (state, _) = state.Enter(2, isWarmupNow: true);
        var (afterFirst, endPause, endWarmup) = state.Leave(1);
        Assert.False(endPause);
        Assert.False(endWarmup);
        var (afterLast, lastEndPause, lastEndWarmup) = afterFirst.Leave(2);
        Assert.True(lastEndPause);
        Assert.True(lastEndWarmup);
        Assert.False(afterLast.IsActive);
        Assert.Null(afterLast.WarmupBefore);
    }

    [Fact]
    public void EditingDuringWarmup_KeepsTheWarmupWhenLeaving()
    {
        var (state, _) = EditorState.Idle.Enter(1, isWarmupNow: true);
        var (_, endPause, endWarmup) = state.Leave(1);
        Assert.True(endPause);
        Assert.False(endWarmup);
    }

    [Fact]
    public void LeavingWithoutEditing_DoesNothing()
    {
        var (_, endPause, endWarmup) = EditorState.Idle.Leave(3);
        Assert.False(endPause);
        Assert.False(endWarmup);
    }

    [Fact]
    public void Noclip_IsOnlyForEditors_AndClearedWhenLeaving()
    {
        Assert.False(EditorState.Idle.ToggleNoclip(1).Enabled);
        var (state, _) = EditorState.Idle.Enter(1, isWarmupNow: false);
        var (on, enabled) = state.ToggleNoclip(1);
        Assert.True(enabled);
        Assert.False(on.ToggleNoclip(1).Enabled);
        Assert.DoesNotContain(1, on.Leave(1).State.Noclip);
    }
}
