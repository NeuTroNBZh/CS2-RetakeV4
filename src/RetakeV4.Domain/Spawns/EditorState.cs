using System.Collections.Immutable;

namespace RetakeV4.Domain.Spawns;

// WarmupBefore: whether the game was already in warmup when the first editor paused it (restored when the last one leaves).
public sealed record EditorState(ImmutableHashSet<int> Editors, ImmutableHashSet<int> Noclip, bool? WarmupBefore)
{
    public static EditorState Idle { get; } = new(ImmutableHashSet<int>.Empty, ImmutableHashSet<int>.Empty, null);

    public bool IsActive => !Editors.IsEmpty;

    public bool IsEditing(int slot) => Editors.Contains(slot);

    public (EditorState State, bool StartPause) Enter(int slot, bool isWarmupNow)
    {
        if (Editors.Contains(slot))
        {
            return (this, false);
        }
        return IsActive
            ? (this with { Editors = Editors.Add(slot) }, false)
            : (this with { Editors = Editors.Add(slot), WarmupBefore = isWarmupNow }, true);
    }

    public (EditorState State, bool EndPause, bool EndWarmup) Leave(int slot)
    {
        if (!Editors.Contains(slot))
        {
            return (this, false, false);
        }
        var next = this with { Editors = Editors.Remove(slot), Noclip = Noclip.Remove(slot) };
        return next.IsActive
            ? (next, false, false)
            : (next with { WarmupBefore = null }, true, WarmupBefore == false);
    }

    public (EditorState State, bool Enabled) ToggleNoclip(int slot)
    {
        if (!Editors.Contains(slot))
        {
            return (this, false);
        }
        return Noclip.Contains(slot)
            ? (this with { Noclip = Noclip.Remove(slot) }, false)
            : (this with { Noclip = Noclip.Add(slot) }, true);
    }
}
