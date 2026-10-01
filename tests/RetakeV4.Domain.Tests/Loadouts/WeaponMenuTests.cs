using RetakeV4.Domain.Common;
using RetakeV4.Domain.Hud;
using RetakeV4.Domain.Loadouts;
using RetakeV4.Domain.Rounds;

namespace RetakeV4.Domain.Tests.Loadouts;

public class WeaponMenuTests
{
    private static RoundTypeDefinition Definition(string name, string[] primaries, string[] secondaries, string? defaultPrimary = "weapon_ak47", bool awp = false) =>
        new(
            name,
            ArmorKind.KevlarHelmet,
            new TeamWeapons(primaries, primaries, Array.Empty<string>()),
            new TeamWeapons(secondaries, secondaries, Array.Empty<string>()),
            new TeamDefault(defaultPrimary, "weapon_glock"),
            new TeamDefault(defaultPrimary, "weapon_usp_silencer"),
            new AwpSettings(awp, 1, 0, 1.0),
            new DefuseKitSettings(DefuseKitMode.All, 0, 1.0, false),
            new ZeusSettings(false, 0),
            "default");

    private static readonly RoundTypeDefinition FullBuy =
        Definition("FullBuy", new[] { "weapon_ak47", "weapon_m4a1_silencer" }, new[] { "weapon_deagle" }, awp: true);

    private static readonly RoundTypeDefinition Pistol =
        Definition("Pistol", Array.Empty<string>(), new[] { "weapon_p250", "weapon_tec9" }, defaultPrimary: null);

    private static readonly RoundTypeDefinition Fixed =
        Definition("Fixed", Array.Empty<string>(), Array.Empty<string>());

    private static WeaponMenuState State(
        TeamSide? team, RoundTypeDefinition? current, LoadoutPreference? preference = null, TeamSide? awpFor = null, params RoundTypeDefinition[] definitions) =>
        new(definitions.Length > 0 ? definitions : new[] { FullBuy }, current, team, (_, _) => preference, side => side == awpFor);

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
        Assert.Equal(new[] { "primary", "secondary", WeaponMenu.AwpItemId(TeamSide.CT) }, current.Items.Select(i => i.Id));
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
    public void Build_HasOneSectionPerTeam_ListingEveryRoundTypeWithAChoice()
    {
        var menu = WeaponMenu.Build(State(TeamSide.T, FullBuy, definitions: new[] { FullBuy, Pistol, Fixed }));
        Assert.Equal(new[] { WeaponMenu.CurrentItemId, "team:T", "team:CT" }, menu.Items.Select(i => i.Id));
        var terrorists = Item(menu, "team:T");
        Assert.Equal(TeamSide.T, terrorists.Team);
        Assert.Equal(new[] { "cfg:T:FullBuy", "cfg:T:Pistol" }, terrorists.Submenu!.Items.Select(i => i.Id));
        Assert.All(terrorists.Submenu.Items, i => Assert.Equal(TeamSide.T, i.Team));
    }

    // The section shows what the player gets, before entering the round type.
    [Fact]
    public void Build_RoundTypeEntries_SummariseTheEffectiveWeapons()
    {
        var menu = WeaponMenu.Build(State(TeamSide.CT, FullBuy, new LoadoutPreference("weapon_m4a1_silencer", null, false), definitions: new[] { FullBuy, Pistol }));
        var fullBuy = Item(Item(menu, "team:CT").Submenu!, "cfg:CT:FullBuy");
        Assert.Equal("allocation.menu.summary", fullBuy.Label.Key);
        Assert.Equal(new object[] { "FullBuy", "M4A1-S", "USP-S" }, fullBuy.Label.Args);
        var pistol = Item(Item(menu, "team:CT").Submenu!, "cfg:CT:Pistol");
        Assert.Equal(new object[] { "Pistol", "-", "USP-S" }, pistol.Label.Args);
    }

    [Fact]
    public void Build_SkipsATeamWithoutAnyChoice()
    {
        var tOnly = Definition("TOnly", Array.Empty<string>(), Array.Empty<string>()) with
        {
            Awp = new AwpSettings(false, 0, 0, 0),
            Secondaries = new TeamWeapons(new[] { "weapon_glock", "weapon_tec9" }, Array.Empty<string>(), Array.Empty<string>()),
        };
        var menu = WeaponMenu.Build(State(null, null, definitions: new[] { tOnly }));
        Assert.Equal(new[] { "team:T" }, menu.Items.Select(i => i.Id));
    }

    [Fact]
    public void Build_ForASpectator_HasNoCurrentEntry_ButBothSections()
    {
        var menu = WeaponMenu.Build(State(null, FullBuy));
        Assert.Equal(new[] { "team:T", "team:CT" }, menu.Items.Select(i => i.Id));
    }

    // The AWP is volunteered per team, and only inside round types that hand it out (FullBuy by default).
    [Fact]
    public void AwpToggle_IsPerTeam_InsideRoundTypesThatHandItOut()
    {
        var noAwp = Pistol with { Awp = new AwpSettings(false, 0, 0, 0) };
        var menu = WeaponMenu.Build(State(TeamSide.CT, FullBuy, awpFor: TeamSide.CT, definitions: new[] { FullBuy, noAwp }));
        var ctFullBuy = Item(Item(menu, "team:CT").Submenu!, "cfg:CT:FullBuy").Submenu!;
        var awp = Item(ctFullBuy, WeaponMenu.AwpItemId(TeamSide.CT));
        Assert.Equal(MenuItemKind.Toggle, awp.Kind);
        Assert.True(awp.IsOn);
        var tFullBuy = Item(Item(menu, "team:T").Submenu!, "cfg:T:FullBuy").Submenu!;
        Assert.False(Item(tFullBuy, WeaponMenu.AwpItemId(TeamSide.T)).IsOn);
        Assert.DoesNotContain(Item(Item(menu, "team:CT").Submenu!, "cfg:CT:Pistol").Submenu!.Items, i => i.Kind == MenuItemKind.Toggle);
    }

    [Fact]
    public void RoundTypeWithOnlyTheAwp_IsStillListed()
    {
        var awpOnly = Fixed with { Awp = new AwpSettings(true, 1, 0, 1.0) };
        var menu = WeaponMenu.Build(State(TeamSide.T, awpOnly, definitions: new[] { awpOnly }));
        var config = Item(Item(menu, "team:T").Submenu!, "cfg:T:Fixed").Submenu!;
        Assert.Equal(new[] { WeaponMenu.AwpItemId(TeamSide.T) }, config.Items.Select(i => i.Id));
    }

    [Theory]
    [InlineData(TeamSide.T)]
    [InlineData(TeamSide.CT)]
    public void AwpItemId_RoundTrips(TeamSide side) => Assert.Equal(side, WeaponMenu.ParseAwp(WeaponMenu.AwpItemId(side)));

    [Theory]
    [InlineData("awp")]
    [InlineData("awp:X")]
    [InlineData("pick:CT:Primary:weapon_ak47:FullBuy")]
    public void ParseAwp_RejectsOtherIds(string itemId) => Assert.Null(WeaponMenu.ParseAwp(itemId));

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
