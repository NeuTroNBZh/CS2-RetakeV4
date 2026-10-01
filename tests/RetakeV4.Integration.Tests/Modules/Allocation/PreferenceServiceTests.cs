using System.Collections.Concurrent;
using RetakeV4.Domain.Common;
using RetakeV4.Domain.Loadouts;
using RetakeV4.Domain.Preferences;
using RetakeV4.Integration.Tests.Persistence;
using RetakeV4.Modules.Allocation;
using RetakeV4.Persistence;

namespace RetakeV4.Integration.Tests.Modules.Allocation;

public sealed class PreferenceServiceTests : IAsyncDisposable
{
    private const ulong Alice = 76561198000000001UL;
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

    private readonly FakePreferenceRepository _store = new();
    private readonly BlockingCollection<Action> _gameThread = new();
    private readonly PreferenceService _service;

    public PreferenceServiceTests() => _service = new PreferenceService(_store, new ListLogger(), _gameThread.Add);

    public ValueTask DisposeAsync() => _service.DisposeAsync();

    private void RunGameThread()
    {
        Assert.True(_gameThread.TryTake(out var action, Wait), "nothing reached the game thread");
        action!();
    }

    private void AssertNothingForTheGameThread() => Assert.False(_gameThread.TryTake(out _, TimeSpan.FromMilliseconds(200)));

    private static StoredPreference FullBuy(string primary) =>
        new(new PreferenceKey(Alice, TeamSide.CT, "FullBuy"), new LoadoutPreference(primary, null, false));

    private void Connect(string stamp, string primary)
    {
        _store.Stored.Add(FullBuy(primary));
        _store.StampValues[Alice] = stamp;
        _service.PlayerConnected(Alice);
        RunGameThread();
    }

    private void ChangeFromThePanel(string stamp, string primary)
    {
        _store.Stored.Clear();
        _store.Stored.Add(FullBuy(primary));
        _store.StampValues[Alice] = stamp;
    }

    [Fact]
    public void Connect_LoadsThePreferences()
    {
        Connect("s1", "weapon_m4a1_silencer");
        Assert.Equal("weapon_m4a1_silencer", _service.RequestFor(Alice, TeamSide.CT, "FullBuy")?.Primary);
    }

    [Fact]
    public void ChangeFromThePanel_IsReloaded()
    {
        Connect("s1", "weapon_m4a1_silencer");
        ChangeFromThePanel("s2", "weapon_aug");
        _service.CheckForExternalChanges();
        RunGameThread();
        RunGameThread();
        Assert.Equal("weapon_aug", _service.RequestFor(Alice, TeamSide.CT, "FullBuy")?.Primary);
    }

    [Fact]
    public void UnchangedStamp_DoesNotReload()
    {
        Connect("s1", "weapon_m4a1_silencer");
        _service.CheckForExternalChanges();
        RunGameThread();
        AssertNothingForTheGameThread();
        Assert.Equal(1, _store.SnapshotCalls);
    }

    [Fact]
    public void DatabaseDown_DuringCheck_KeepsPreferences()
    {
        Connect("s1", "weapon_m4a1_silencer");
        _store.Fail = true;
        _service.CheckForExternalChanges();
        AssertNothingForTheGameThread();
        Assert.Equal("weapon_m4a1_silencer", _service.RequestFor(Alice, TeamSide.CT, "FullBuy")?.Primary);
    }

    [Fact]
    public void DisconnectDuringReload_LeavesNoTrace()
    {
        Connect("s1", "weapon_m4a1_silencer");
        ChangeFromThePanel("s2", "weapon_aug");
        _service.CheckForExternalChanges();
        RunGameThread();
        _service.PlayerDisconnected(Alice);
        RunGameThread();
        Assert.Null(_service.RequestFor(Alice, TeamSide.CT, "FullBuy"));
        _service.CheckForExternalChanges();
        AssertNothingForTheGameThread();
    }

    [Fact]
    public void PublishCatalog_ReachesTheStore()
    {
        _service.PublishCatalog("agora-1", """{"roundTypes":[]}""");
        Assert.True(SpinWait.SpinUntil(() => !_store.Published.IsEmpty, Wait));
        Assert.Equal(new PublishedCatalog("agora-1", CatalogExport.FormatVersion, """{"roundTypes":[]}"""), Assert.Single(_store.Published));
    }
}
