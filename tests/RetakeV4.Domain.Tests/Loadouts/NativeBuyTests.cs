using RetakeV4.Domain.Common;
using RetakeV4.Domain.Loadouts;
using RetakeV4.Domain.Rounds;

namespace RetakeV4.Domain.Tests.Loadouts;

public class NativeBuyTests
{
    private static readonly RoundTypeDefinition FullBuy = new(
        "FullBuy",
        ArmorKind.KevlarHelmet,
        new TeamWeapons(new[] { "weapon_ak47", "weapon_galilar" }, new[] { "weapon_m4a1", "weapon_m4a1_silencer" }, Array.Empty<string>()),
        new TeamWeapons(new[] { "weapon_glock", "weapon_tec9" }, new[] { "weapon_usp_silencer" }, new[] { "weapon_deagle" }),
        new TeamDefault("weapon_ak47", "weapon_glock"),
        new TeamDefault("weapon_m4a1", "weapon_usp_silencer"),
        new AwpSettings(true, 1, 0, 1.0),
        new DefuseKitSettings(DefuseKitMode.All, 0, 1.0, false),
        new ZeusSettings(false, 0),
        "default");

    [Theory]
    [InlineData("weapon_M4A1_Silencer", "m4a1silencer")]
    [InlineData(" AK-47 ", "ak47")]
    [InlineData("item_kevlar", "itemkevlar")]
    public void Normalize_KeepsLettersAndDigits(string raw, string expected) => Assert.Equal(expected, NativeBuyResolver.Normalize(raw));

    [Theory]
    [InlineData("ak47", "weapon_ak47")]
    [InlineData("weapon_m4a1_silencer", "weapon_m4a1_silencer")]
    [InlineData("m4a1s", "weapon_m4a1_silencer")]
    [InlineData("usp", "weapon_usp_silencer")]
    [InlineData("p2000", "weapon_hkp2000")]
    [InlineData("galil", "weapon_galilar")]
    [InlineData("deagle", "weapon_deagle")]
    [InlineData("awp", "weapon_awp")]
    public void NamedWeapons_AreResolved(string token, string expected) =>
        Assert.Equal(new BuyRequest(BuyRequestKind.Weapon, expected), NativeBuyResolver.Resolve(new[] { token }));

    [Theory]
    [InlineData("hegrenade")]
    [InlineData("item_assaultsuit")]
    [InlineData("vesthelm")]
    [InlineData("defuser")]
    [InlineData("weapon_taser")]
    public void AutoManagedItems_AreBlocked(string token) =>
        Assert.Equal(BuyRequestKind.AutoManaged, NativeBuyResolver.Resolve(new[] { token }).Kind);

    [Fact]
    public void NumericPayload_IsCaptured() =>
        Assert.Equal(BuyRequestKind.Capture, NativeBuyResolver.Resolve(new[] { "unused", "12" }).Kind);

    [Fact]
    public void UnknownOrEmptyPayload_IsCaptured()
    {
        Assert.Equal(BuyRequestKind.Capture, NativeBuyResolver.Resolve(new[] { "something_new" }).Kind);
        Assert.Equal(BuyRequestKind.Capture, NativeBuyResolver.Resolve(new[] { "buy", " " }).Kind);
    }

    [Theory]
    [InlineData(60, null, "weapon_m4a1_silencer")]
    [InlineData(7, "weapon_glock", "weapon_ak47")]
    [InlineData(0, "weapon_usp_silencer", "weapon_usp_silencer")]
    [InlineData(999, "weapon_mp9", "weapon_mp9")]
    [InlineData(44, "weapon_hegrenade", null)]
    public void Pickups_PreferTheDefindex(long defindex, string? item, string? expected) =>
        Assert.Equal(expected, NativeBuyResolver.FromPickup(defindex, item));

    [Fact]
    public void AutoManagedPickups_AreRecognised()
    {
        Assert.True(NativeBuyResolver.IsAutoManaged("weapon_flashbang"));
        Assert.False(NativeBuyResolver.IsAutoManaged("weapon_ak47"));
        Assert.False(NativeBuyResolver.IsAutoManaged(null));
    }

    [Fact]
    public void WeaponInThePool_UpdatesTheCurrentRoundPreference()
    {
        var decision = NativeBuy.Decide("weapon_galilar", TeamSide.T, FullBuy);
        Assert.Equal(new BuyDecision(BuyOutcome.SetWeapon, new WeaponMenuSelection(TeamSide.T, "FullBuy", WeaponSlot.Primary, "weapon_galilar")), decision);
        Assert.Equal(WeaponSlot.Secondary, NativeBuy.Decide("weapon_deagle", TeamSide.CT, FullBuy).Selection?.Slot);
    }

    [Fact]
    public void WeaponOutsideThePool_IsNotAvailable()
    {
        Assert.Equal(BuyOutcome.NotAvailable, NativeBuy.Decide("weapon_m4a1", TeamSide.T, FullBuy).Outcome);
        Assert.Equal(BuyOutcome.NotAvailable, NativeBuy.Decide("weapon_nova", TeamSide.CT, FullBuy).Outcome);
        Assert.Equal(BuyOutcome.NotAvailable, NativeBuy.Decide("weapon_ak47", TeamSide.T, null).Outcome);
    }

    [Fact]
    public void Awp_MakesAVolunteer() =>
        Assert.Equal(BuyOutcome.AwpVolunteer, NativeBuy.Decide(WeaponCatalog.Awp, TeamSide.T, null).Outcome);

    [Fact]
    public void MenuMode_ClosesTheBuyMenu()
    {
        Assert.Contains(("mp_buytime", "0"), AllocationModes.Cvars(AllocationMode.Menu));
        Assert.Contains(("mp_buy_anywhere", "1"), AllocationModes.Cvars(AllocationMode.NativeBuy));
        Assert.Contains(("mp_maxmoney", "16000"), AllocationModes.Cvars(AllocationMode.Both));
        Assert.True(AllocationModes.UsesMenu(AllocationMode.Both));
        Assert.False(AllocationModes.UsesMenu(AllocationMode.NativeBuy));
        Assert.False(AllocationModes.UsesNativeBuy(AllocationMode.Menu));
        Assert.Equal("allocation.howto.native", AllocationModes.HowToKey(AllocationMode.NativeBuy));
    }

    [Fact]
    public void InventoryDiff_FindsTheBoughtItemAndTheDroppedOne()
    {
        var before = new Dictionary<uint, string> { [10] = "weapon_knife", [11] = "weapon_ak47", [12] = "weapon_flashbang" };
        var after = new Dictionary<uint, string> { [10] = "weapon_knife", [12] = "weapon_flashbang", [20] = "weapon_galilar", [21] = "weapon_flashbang" };
        var diff = InventorySnapshot.Diff(before, after);
        Assert.Equal(new uint[] { 20, 21 }, diff.Added);
        Assert.Equal(new uint[] { 11 }, diff.Dropped);
    }

    [Theory]
    [InlineData(RoundPhase.FreezeTime, true, true)]
    [InlineData(RoundPhase.Live, true, false)]
    [InlineData(RoundPhase.FreezeTime, false, false)]
    [InlineData(RoundPhase.PostRound, true, false)]
    public void Capture_IsOnlyAllowedDuringFreezeTime(RoundPhase phase, bool alive, bool expected) =>
        Assert.Equal(expected, NativeBuy.CanCapture(phase, alive));
}
