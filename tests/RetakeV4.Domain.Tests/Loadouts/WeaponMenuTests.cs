using RetakeV4.Domain.Common;
using RetakeV4.Domain.Hud;
using RetakeV4.Domain.Loadouts;
using RetakeV4.Domain.Rounds;

namespace RetakeV4.Domain.Tests.Loadouts;

public class WeaponMenuTests
{
    private static RoundTypeDefinition Definition(string name, string[] primaries, string[] secondaries, string? defaultPrimary = "weapon_ak47") =>
        new(
            name,
            ArmorKind.KevlarHelmet,
            new TeamWeapons(primaries, primaries, Array.Empty<string>()),
            new TeamWeapons(secondaries, secondaries, Array.Empty<string>()),
            new TeamDefault(defaultPrimary, "weapon_glock"),
            new TeamDefault(defaultPrimary, "weapon_usp_silencer"),
            new AwpSettings(true, 1, 0, 1.0),
            new DefuseKitSettings(DefuseKitMode.All, 0, 1.0, false),
            new ZeusSettings(false, 0),
            "default");

    private static readonly RoundTypeDefinition FullBuy =
        Definition("FullBuy", new[] { "weapon_ak47", "weapon_m4a1_silencer" }, new[] { "weapon_deagle" });

    private static readonly RoundTypeDefinition Pistol =
        Definition("Pistol", Array.Empty<string>(), new[] { "weapon_p250", "weapon_tec9" }, defaultPrimary: null);

    private static readonly RoundTypeDefinition Fixed =
        Definition("Fixed", Array.Empty<string>(), Array.Empty<string>());

    private static WeaponMenuState State(
        TeamSide? team, RoundTypeDefinition? current, LoadoutPreference? preference = null, bool awp = false, params RoundTypeDefinition[] definitions) =>
        new(definitions.Length > 0 ? definitions : new[] { FullBuy }, current, team, (_, _) => preference, awp);

    private static MenuItem Item(Menu menu, string id) => Assert.Single(menu.Items, i => i.Id == id);

    [Fact]
    public void Options_IncludeTheDefault_AndExcludeAwpAndWrongSlots()
    {
        var definition = Definition("Mixed", new[] { "weapon_m4a1", "weapon_awp", "weapon_glock" }, new[] { "weapon_ak47" });
        Assert.Equal(new[] { "weapon_ak47", "weapon_m4a1" }, WeaponMenu.Options(definition, TeamSide.T, WeaponSlot.Primary));
        Assert.Equal(new[] { "weapon_glock" }, WeaponMenu.Options(definition, TeamSide.T, WeaponSlot.Secondary));
    }

    [Fact]
    public void Build_CurrentConfig_ListsBothSlots_WithTheEffectiveChoiceMarked()
    {
        var menu = WeaponMenu.Build(State(TeamSide.CT, FullBuy, new LoadoutPreference("weapon_m4a1_silencer", null, false)));
        Assert.Equal(WeaponMenu.MenuId, menu.Id);
        var current = Item(menu, WeaponMenu.CurrentItemId).Submenu!;
        Assert.Equal(new[] { "primary", "secondary" }, current.Items.Select(i => i.Id));
        var primaries = Item(current, "primary").Submenu!.Items;
        Assert.Equal(new[] { "AK-47", "M4A1-S" }, primaries.Select(i => i.Label.Literal));
        Assert.Equal(new[] { false, true }, primaries.Select(i => i.IsOn));
        Assert.All(primaries, i => Assert.Equal(MenuItemKind.Choice, i.Kind));
        Assert.Equal(new object[] { "USP-S" }, Item(current, "secondary").Label.Args);
    }

    [Fact]
    public void Build_PistolRound_OnlyOffersTheSecondary()
    {
        var menu = WeaponMenu.Build(State(TeamSide.T, Pistol, definitions: new[] { Pistol }));
        var current = Item(menu, WeaponMenu.CurrentItemId).Submenu!;
        Assert.Equal(new[] { "secondary" }, current.Items.Select(i => i.Id));
    }

    [Fact]
    public void Build_Others_ExcludeTheCurrentConfig_AndConfigsWithoutChoice()
    {
        var menu = WeaponMenu.Build(State(TeamSide.T, FullBuy, definitions: new[] { FullBuy, Fixed }));
        var others = Item(menu, WeaponMenu.OthersItemId).Submenu!;
        Assert.Equal(new[] { "cfg:CT:FullBuy" }, others.Items.Select(i => i.Id));
    }

    [Fact]
    public void Build_ForASpectator_HasNoCurrentEntry_ButOffersEveryConfig()
    {
        var menu = WeaponMenu.Build(State(null, FullBuy));
        Assert.DoesNotContain(menu.Items, i => i.Id == WeaponMenu.CurrentItemId);
        Assert.Equal(new[] { "cfg:T:FullBuy", "cfg:CT:FullBuy" }, Item(menu, WeaponMenu.OthersItemId).Submenu!.Items.Select(i => i.Id));
    }

    [Fact]
    public void Build_WithoutAnyChoice_OnlyShowsTheAwpToggle()
    {
        var menu = WeaponMenu.Build(State(TeamSide.T, Fixed, awp: true, definitions: new[] { Fixed }));
        var awp = Assert.Single(menu.Items);
        Assert.Equal(WeaponMenu.AwpItemId, awp.Id);
        Assert.Equal(MenuItemKind.Toggle, awp.Kind);
        Assert.True(awp.IsOn);
    }

    [Fact]
    public void Selection_RoundTrips_WithColonsInTheRoundType()
    {
        var selection = new WeaponMenuSelection(TeamSide.CT, "Full:Buy<b>", WeaponSlot.Secondary, "weapon_deagle");
        Assert.Equal(selection, WeaponMenuSelection.Parse(selection.ToItemId()));
    }

    [Theory]
    [InlineData("awp")]
    [InlineData("pick:CT:Primary:weapon_ak47")]
    [InlineData("pick:XX:Primary:weapon_ak47:FullBuy")]
    [InlineData("pick:CT:Knife:weapon_ak47:FullBuy")]
    [InlineData("pick:7:Primary:weapon_ak47:FullBuy")]
    [InlineData("pick:CT:Primary::FullBuy")]
    [InlineData("other:CT:Primary:weapon_ak47:FullBuy")]
    public void Parse_RejectsMalformedIds(string itemId) => Assert.Null(WeaponMenuSelection.Parse(itemId));

    [Fact]
    public void IsAllowed_RejectsStaleOrForbiddenWeapons()
    {
        var definitions = new[] { FullBuy };
        Assert.True(WeaponMenu.IsAllowed(new WeaponMenuSelection(TeamSide.T, "FullBuy", WeaponSlot.Primary, "weapon_m4a1_silencer"), definitions));
        Assert.False(WeaponMenu.IsAllowed(new WeaponMenuSelection(TeamSide.T, "FullBuy", WeaponSlot.Primary, "weapon_famas"), definitions));
        Assert.False(WeaponMenu.IsAllowed(new WeaponMenuSelection(TeamSide.T, "FullBuy", WeaponSlot.Primary, "weapon_awp"), definitions));
        Assert.False(WeaponMenu.IsAllowed(new WeaponMenuSelection(TeamSide.T, "FullBuy", WeaponSlot.Secondary, "weapon_ak47"), definitions));
        Assert.False(WeaponMenu.IsAllowed(new WeaponMenuSelection(TeamSide.T, "Deleted", WeaponSlot.Primary, "weapon_ak47"), definitions));
    }

    [Theory]
    [InlineData(RoundPhase.FreezeTime, true, TeamSide.CT, "FullBuy", true)]
    [InlineData(RoundPhase.Live, true, TeamSide.CT, "FullBuy", false)]
    [InlineData(RoundPhase.FreezeTime, false, TeamSide.CT, "FullBuy", false)]
    [InlineData(RoundPhase.FreezeTime, true, TeamSide.T, "FullBuy", false)]
    [InlineData(RoundPhase.FreezeTime, true, TeamSide.CT, "Mid", false)]
    [InlineData(RoundPhase.Warmup, true, TeamSide.CT, "FullBuy", false)]
    public void AppliesNow_OnlyDuringFreezeTimeForTheOwnCurrentConfig(RoundPhase phase, bool alive, TeamSide side, string current, bool expected)
    {
        var selection = new WeaponMenuSelection(TeamSide.CT, "FullBuy", WeaponSlot.Primary, "weapon_m4a1_silencer");
        Assert.Equal(expected, WeaponMenu.AppliesNow(selection, phase, alive, side, current));
    }

    [Theory]
    [InlineData("weapon_m4a1_silencer", "M4A1-S")]
    [InlineData("weapon_custom", "CUSTOM")]
    [InlineData(null, "-")]
    public void WeaponNames_AreReadable(string? id, string expected) => Assert.Equal(expected, WeaponNames.Display(id));
}
