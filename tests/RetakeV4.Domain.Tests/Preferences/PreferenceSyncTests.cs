using RetakeV4.Domain.Preferences;

namespace RetakeV4.Domain.Tests.Preferences;

public class PreferenceSyncTests
{
    private const ulong Alice = 76561198000000001UL;
    private const ulong Bob = 76561198000000002UL;

    private static Dictionary<ulong, string> Current(params (ulong Id, string Stamp)[] stamps) =>
        stamps.ToDictionary(s => s.Id, s => s.Stamp);

    [Fact]
    public void SameStamp_IsNotChanged()
    {
        var sync = PreferenceSync.Empty.Stamped(Alice, "2026-10-01 12:00:00.000001");
        Assert.Empty(sync.Changed(new[] { Alice }, Current((Alice, "2026-10-01 12:00:00.000001"))));
    }

    [Fact]
    public void DifferentStamp_IsChanged()
    {
        var sync = PreferenceSync.Empty.Stamped(Alice, "a").Stamped(Bob, "b");
        Assert.Equal(new[] { Alice }, sync.Changed(new[] { Alice, Bob }, Current((Alice, "a2"), (Bob, "b"))));
    }

    [Fact]
    public void RowsDeletedElsewhere_AreChanged()
    {
        var sync = PreferenceSync.Empty.Stamped(Alice, "a");
        Assert.Equal(new[] { Alice }, sync.Changed(new[] { Alice }, Current()));
    }

    [Fact]
    public void PlayerWithoutRows_StaysUnchanged()
    {
        var sync = PreferenceSync.Empty.Stamped(Alice, PreferenceSync.NoRows);
        Assert.Empty(sync.Changed(new[] { Alice }, Current()));
    }

    // A player whose first load failed (database down) is retried at the next check.
    [Fact]
    public void UnstampedPlayer_IsChanged() =>
        Assert.Equal(new[] { Alice }, PreferenceSync.Empty.Changed(new[] { Alice }, Current()));

    [Fact]
    public void PendingWrite_IsNotReloaded()
    {
        var sync = PreferenceSync.Empty.Stamped(Alice, "a").Edited(Alice);
        Assert.Empty(sync.Changed(new[] { Alice }, Current((Alice, "b"))));
        Assert.Equal(new[] { Alice }, sync.Flushed(Alice).Changed(new[] { Alice }, Current((Alice, "b"))));
    }

    [Fact]
    public void EditDuringReload_BlocksApply()
    {
        var sync = PreferenceSync.Empty.Stamped(Alice, "a");
        var edit = sync.EditOf(Alice);
        Assert.True(sync.CanApply(Alice, edit));
        var edited = sync.Edited(Alice).Flushed(Alice);
        Assert.False(edited.CanApply(Alice, edit));
    }

    [Fact]
    public void PendingWrite_BlocksApply()
    {
        var sync = PreferenceSync.Empty.Edited(Alice);
        Assert.False(sync.CanApply(Alice, sync.EditOf(Alice)));
    }

    [Fact]
    public void Flushed_NeverGoesBelowZero()
    {
        var sync = PreferenceSync.Empty.Flushed(Alice);
        Assert.True(sync.CanApply(Alice, sync.EditOf(Alice)));
    }

    [Fact]
    public void Forget_RemovesEverything()
    {
        var sync = PreferenceSync.Empty.Stamped(Alice, "a").Edited(Alice).Forget(Alice);
        Assert.Empty(sync.Stamps);
        Assert.Empty(sync.Edits);
        Assert.Empty(sync.Pending);
    }
}
