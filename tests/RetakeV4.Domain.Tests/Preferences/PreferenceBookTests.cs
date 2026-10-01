using RetakeV4.Domain.Common;
using RetakeV4.Domain.Loadouts;
using RetakeV4.Domain.Preferences;

namespace RetakeV4.Domain.Tests.Preferences;

public class PreferenceBookTests
{
    private const ulong Alice = 76561198000000001UL;
    private const ulong Bob = 76561198000000002UL;

    private static StoredPreference Weapons(ulong steamId, TeamSide team, string roundType, string? primary, string? secondary = null) =>
        new(new PreferenceKey(steamId, team, roundType), new LoadoutPreference(primary, secondary, false));

    private static PreferenceBook Loaded(ulong steamId, params StoredPreference[] stored)
    {
        var (book, token) = PreferenceBook.Empty.StartSession(steamId);
        return book.WithPlayer(steamId, token, stored);
    }

    [Fact]
    public void RequestFor_ReturnsTheRoundTypePreference()
    {
        var book = Loaded(Alice, Weapons(Alice, TeamSide.CT, "FullBuy", "weapon_m4a1_silencer"));
        Assert.Equal("weapon_m4a1_silencer", book.RequestFor(Alice, TeamSide.CT, "FullBuy")?.Primary);
        Assert.Null(book.RequestFor(Alice, TeamSide.T, "FullBuy"));
        Assert.Null(book.RequestFor(Bob, TeamSide.CT, "FullBuy"));
    }

    [Fact]
    public void RequestFor_MergesAwpVolunteering()
    {
        var (book, _, optIn) = Loaded(Alice, Weapons(Alice, TeamSide.CT, "FullBuy", "weapon_m4a1")).ToggleAwp(Alice);
        Assert.True(optIn);
        var request = book.RequestFor(Alice, TeamSide.CT, "FullBuy");
        Assert.Equal("weapon_m4a1", request?.Primary);
        Assert.True(request?.AwpOptIn);
        Assert.True(book.RequestFor(Alice, TeamSide.T, "Mid")?.AwpOptIn);
    }

    [Fact]
    public void ToggleAwp_WritesBothTeams_AndTogglesBack()
    {
        var (book, changes, optIn) = PreferenceBook.Empty.ToggleAwp(Alice);
        Assert.True(optIn);
        Assert.Equal(2, changes.Count);
        Assert.All(changes, c => Assert.Equal(PreferenceKey.AnyRoundType, c.Key.RoundType));
        Assert.True(book.IsAwpVolunteer(Alice));
        var (after, _, secondOptIn) = book.ToggleAwp(Alice);
        Assert.False(secondOptIn);
        Assert.False(after.IsAwpVolunteer(Alice));
    }

    [Fact]
    public void WithoutPlayer_ForgetsEverything()
    {
        var book = Loaded(Alice, Weapons(Alice, TeamSide.CT, "FullBuy", "weapon_m4a1")).WithoutPlayer(Alice);
        Assert.Null(book.RequestFor(Alice, TeamSide.CT, "FullBuy"));
    }

    [Fact]
    public void WithPlayer_ForUnknownSession_IsIgnored()
    {
        var (book, token) = PreferenceBook.Empty.StartSession(Alice);
        var afterLeave = book.WithoutPlayer(Alice).WithPlayer(Alice, token, new[] { Weapons(Alice, TeamSide.CT, "FullBuy", "weapon_m4a1") });
        Assert.Null(afterLeave.RequestFor(Alice, TeamSide.CT, "FullBuy"));
    }

    [Fact]
    public void WithPlayer_FromAnOlderSession_IsIgnored()
    {
        var (first, oldToken) = PreferenceBook.Empty.StartSession(Alice);
        var (second, _) = first.StartSession(Alice);
        var result = second.WithPlayer(Alice, oldToken, new[] { Weapons(Alice, TeamSide.CT, "FullBuy", "weapon_m4a1") });
        Assert.Null(result.RequestFor(Alice, TeamSide.CT, "FullBuy"));
    }

    [Fact]
    public void WithPlayer_KeepsChangesMadeWhileLoading()
    {
        var (book, token) = PreferenceBook.Empty.StartSession(Alice);
        var (toggled, _, _) = book.ToggleAwp(Alice);
        var loaded = toggled.WithPlayer(Alice, token, new[] { Weapons(Alice, TeamSide.CT, "FullBuy", "weapon_m4a1") });
        Assert.True(loaded.IsAwpVolunteer(Alice));
        Assert.Equal("weapon_m4a1", loaded.RequestFor(Alice, TeamSide.CT, "FullBuy")?.Primary);
    }

    [Fact]
    public void With_ReplacesASingleEntry()
    {
        var book = PreferenceBook.Empty
            .With(Weapons(Alice, TeamSide.T, "Mid", "weapon_mac10"))
            .With(Weapons(Alice, TeamSide.T, "Mid", "weapon_galilar"));
        Assert.Equal("weapon_galilar", book.RequestFor(Alice, TeamSide.T, "Mid")?.Primary);
    }

    [Fact]
    public void SetWeapon_ChangesOneSlot_AndKeepsTheOther()
    {
        var book = Loaded(Alice, Weapons(Alice, TeamSide.CT, "FullBuy", "weapon_m4a1", "weapon_deagle"));
        var (after, change) = book.SetWeapon(Alice, TeamSide.CT, "FullBuy", WeaponSlot.Primary, "weapon_aug");
        Assert.Equal(new PreferenceKey(Alice, TeamSide.CT, "FullBuy"), change.Key);
        Assert.Equal(new LoadoutPreference("weapon_aug", "weapon_deagle", false), change.Preference);
        Assert.Equal("weapon_aug", after.RequestFor(Alice, TeamSide.CT, "FullBuy")?.Primary);
        var (fresh, secondary) = PreferenceBook.Empty.SetWeapon(Bob, TeamSide.T, "Pistol", WeaponSlot.Secondary, "weapon_tec9");
        Assert.Equal(new LoadoutPreference(null, "weapon_tec9", false), secondary.Preference);
        Assert.Equal("weapon_tec9", fresh.RequestFor(Bob, TeamSide.T, "Pistol")?.Secondary);
    }

    [Fact]
    public void Replace_SwapsAllEntriesOfThePlayer()
    {
        var book = Loaded(Alice, Weapons(Alice, TeamSide.CT, "FullBuy", "weapon_m4a1_silencer"), Weapons(Alice, TeamSide.T, "Mid", "weapon_mac10"));
        var replaced = book.Replace(Alice, new[] { Weapons(Alice, TeamSide.CT, "FullBuy", "weapon_aug") });
        Assert.Equal("weapon_aug", replaced.RequestFor(Alice, TeamSide.CT, "FullBuy")?.Primary);
        Assert.Null(replaced.RequestFor(Alice, TeamSide.T, "Mid"));
    }

    [Fact]
    public void Replace_KeepsOtherPlayers()
    {
        var (withBob, bobToken) = Loaded(Alice).StartSession(Bob);
        var book = withBob.WithPlayer(Bob, bobToken, new[] { Weapons(Bob, TeamSide.T, "Mid", "weapon_mp9") });
        Assert.Equal("weapon_mp9", book.Replace(Alice, Array.Empty<StoredPreference>()).RequestFor(Bob, TeamSide.T, "Mid")?.Primary);
    }

    [Fact]
    public void Replace_IgnoresDisconnectedPlayers()
    {
        var book = Loaded(Alice).WithoutPlayer(Alice);
        Assert.Same(book, book.Replace(Alice, new[] { Weapons(Alice, TeamSide.T, "Mid", "weapon_mp9") }));
    }

    [Fact]
    public void Replace_IgnoresRowsOfOtherPlayers()
    {
        var book = Loaded(Alice).Replace(Alice, new[] { Weapons(Bob, TeamSide.T, "Mid", "weapon_mp9") });
        Assert.Null(book.RequestFor(Bob, TeamSide.T, "Mid"));
    }

    [Fact]
    public void Merge_KeepsInGameEntries_AndAddsStoredOnes()
    {
        var (book, _) = PreferenceBook.Empty.StartSession(Alice);
        var edited = book.SetWeapon(Alice, TeamSide.CT, "FullBuy", WeaponSlot.Primary, "weapon_aug").Book;
        var merged = edited.Merge(Alice, new[] { Weapons(Alice, TeamSide.CT, "FullBuy", "weapon_m4a1_silencer"), Weapons(Alice, TeamSide.T, "Mid", "weapon_mac10") });
        Assert.Equal("weapon_aug", merged.RequestFor(Alice, TeamSide.CT, "FullBuy")?.Primary);
        Assert.Equal("weapon_mac10", merged.RequestFor(Alice, TeamSide.T, "Mid")?.Primary);
    }

    [Fact]
    public void Merge_IgnoresDisconnectedPlayers()
    {
        var book = Loaded(Alice).WithoutPlayer(Alice);
        Assert.Same(book, book.Merge(Alice, new[] { Weapons(Alice, TeamSide.T, "Mid", "weapon_mp9") }));
    }
}
