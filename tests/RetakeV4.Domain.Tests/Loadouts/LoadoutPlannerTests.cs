using RetakeV4.Domain.Common;
using RetakeV4.Domain.Loadouts;
using RetakeV4.Domain.Tests.TestDoubles;

namespace RetakeV4.Domain.Tests.Loadouts;

public class LoadoutPlannerTests
{
    private static readonly RoundTypeDefinition FullBuy = new(
        "FullBuy", ArmorKind.KevlarHelmet,
        new TeamWeapons(new[] { "weapon_ak47" }, new[] { "weapon_m4a1", "weapon_m4a1_silencer" }, new[] { "weapon_ssg08" }),
        new TeamWeapons(new[] { "weapon_glock" }, new[] { "weapon_usp_silencer" }, new[] { "weapon_deagle" }),
        new TeamDefault("weapon_ak47", "weapon_deagle"),
        new TeamDefault("weapon_m4a1", "weapon_deagle"),
        new AwpSettings(true, 1, 5, 30),
        new DefuseKitSettings(DefuseKitMode.All, 1, 100, false),
        new ZeusSettings(false, 20),
        "Default");

    private static readonly RoundTypeDefinition Pistol = FullBuy with
    {
        Name = "Pistol",
        Armor = ArmorKind.Kevlar,
        Primaries = TeamWeapons.Empty,
        DefaultT = new TeamDefault(null, "weapon_glock"),
        DefaultCT = new TeamDefault(null, "weapon_usp_silencer"),
        Awp = new AwpSettings(false, 1, 5, 30),
        DefuseKit = new DefuseKitSettings(DefuseKitMode.Chance, 1, 34.44444, true),
    };

    private static LoadoutRequest Req(int slot, TeamSide team, LoadoutPreference? preference = null) =>
        new(new PlayerId(slot), team, preference);

    private static IReadOnlyList<LoadoutRequest> Lobby(int t, int ct, LoadoutPreference? preference = null) =>
        Enumerable.Range(1, t).Select(i => Req(i, TeamSide.T, preference))
            .Concat(Enumerable.Range(100, ct).Select(i => Req(i, TeamSide.CT, preference)))
            .ToList();

    [Fact]
    public void ResolveWeapons_WithoutPreference_UsesDefaults()
    {
        Assert.Equal(("weapon_m4a1", "weapon_deagle"), LoadoutPlanner.ResolveWeapons(FullBuy, Req(1, TeamSide.CT)));
        Assert.Equal(((string?)null, "weapon_glock"), LoadoutPlanner.ResolveWeapons(Pistol, Req(1, TeamSide.T)));
    }

    [Fact]
    public void ResolveWeapons_ValidPreference_IsUsed()
    {
        var preference = new LoadoutPreference("weapon_m4a1_silencer", "weapon_usp_silencer", false);
        Assert.Equal(("weapon_m4a1_silencer", "weapon_usp_silencer"), LoadoutPlanner.ResolveWeapons(FullBuy, Req(1, TeamSide.CT, preference)));
    }

    [Fact]
    public void ResolveWeapons_PreferenceFromOtherSideOrUnknown_FallsBackToDefault()
    {
        var preference = new LoadoutPreference("weapon_m4a1", "weapon_tec9", false);
        Assert.Equal(("weapon_ak47", "weapon_deagle"), LoadoutPlanner.ResolveWeapons(FullBuy, Req(1, TeamSide.T, preference)));
    }

    [Fact]
    public void ResolveWeapons_AnyPoolWeapon_IsAllowedForBothSides()
    {
        var preference = new LoadoutPreference("weapon_ssg08", null, false);
        Assert.Equal("weapon_ssg08", LoadoutPlanner.ResolveWeapons(FullBuy, Req(1, TeamSide.T, preference)).Primary);
    }

    [Fact]
    public void Awp_Disabled_GivesNone() =>
        Assert.Empty(LoadoutPlanner.PickAwpRecipients(Pistol.Awp, Lobby(3, 3, new LoadoutPreference(null, null, true)), new FixedRandom()));

    [Fact]
    public void Awp_BelowMinimumPlayers_GivesNone() =>
        Assert.Empty(LoadoutPlanner.PickAwpRecipients(FullBuy.Awp, Lobby(2, 2, new LoadoutPreference(null, null, true)), new FixedRandom()));

    [Fact]
    public void Awp_VolunteerWinningTheRoll_GetsIt_OnePerTeam()
    {
        var recipients = LoadoutPlanner.PickAwpRecipients(FullBuy.Awp, Lobby(3, 3, new LoadoutPreference(null, null, true)), new FixedRandom { DoubleValue = 0.0 });
        Assert.Equal(2, recipients.Count);
        Assert.Single(recipients, p => p.Slot < 100);
        Assert.Single(recipients, p => p.Slot >= 100);
    }

    [Fact]
    public void Awp_LosingTheRoll_GivesNone() =>
        Assert.Empty(LoadoutPlanner.PickAwpRecipients(FullBuy.Awp, Lobby(3, 3, new LoadoutPreference(null, null, true)), new FixedRandom { DoubleValue = 0.99 }));

    [Fact]
    public void Awp_NonVolunteers_NeverGetIt() =>
        Assert.Empty(LoadoutPlanner.PickAwpRecipients(FullBuy.Awp, Lobby(3, 3), new FixedRandom { DoubleValue = 0.0 }));

    [Fact]
    public void Kits_All_GivesEveryCt()
    {
        var cts = new[] { new PlayerId(1), new PlayerId(2) };
        Assert.Equal(2, LoadoutPlanner.PickDefuseKits(FullBuy.DefuseKit, cts, new FixedRandom()).Count);
    }

    [Fact]
    public void Kits_Quota_GivesExactlyQuota()
    {
        var settings = new DefuseKitSettings(DefuseKitMode.Quota, 1, 100, false);
        var cts = new[] { new PlayerId(1), new PlayerId(2), new PlayerId(3) };
        Assert.Single(LoadoutPlanner.PickDefuseKits(settings, cts, new FixedRandom()));
    }

    [Fact]
    public void Kits_ChanceAllFailing_WithGuarantee_GivesOne()
    {
        var cts = new[] { new PlayerId(1), new PlayerId(2), new PlayerId(3) };
        Assert.Single(LoadoutPlanner.PickDefuseKits(Pistol.DefuseKit, cts, new FixedRandom { DoubleValue = 0.99 }));
    }

    [Fact]
    public void Kits_ChanceAllFailing_WithoutGuarantee_GivesNone()
    {
        var settings = Pistol.DefuseKit with { GuaranteeMinimum = false };
        Assert.Empty(LoadoutPlanner.PickDefuseKits(settings, new[] { new PlayerId(1) }, new FixedRandom { DoubleValue = 0.99 }));
    }

    [Fact]
    public void Kits_NoCt_GivesNone_EvenWithGuarantee() =>
        Assert.Empty(LoadoutPlanner.PickDefuseKits(Pistol.DefuseKit, Array.Empty<PlayerId>(), new FixedRandom()));

    [Fact]
    public void Grenades_PicksAmongSideAndSharedKits()
    {
        var kits = new[]
        {
            new GrenadeKit(null, new[] { "weapon_flashbang" }),
            new GrenadeKit(TeamSide.CT, new[] { "weapon_incgrenade" }),
            new GrenadeKit(TeamSide.T, new[] { "weapon_molotov" }),
        };
        Assert.Equal(new[] { "weapon_molotov" }, LoadoutPlanner.PickGrenades(kits, TeamSide.T, new FixedRandom(1)));
        Assert.Equal(new[] { "weapon_incgrenade" }, LoadoutPlanner.PickGrenades(kits, TeamSide.CT, new FixedRandom(1)));
    }

    [Fact]
    public void Grenades_NoEligibleKit_GivesNothing() =>
        Assert.Empty(LoadoutPlanner.PickGrenades(new[] { new GrenadeKit(TeamSide.CT, new[] { "weapon_flashbang" }) }, TeamSide.T, new FixedRandom()));

    [Fact]
    public void Plan_BuildsFullLoadouts_KitsOnlyForCts()
    {
        var plan = LoadoutPlanner.Plan(FullBuy, Lobby(2, 2), Array.Empty<GrenadeKit>(), new FixedRandom());
        Assert.Equal(4, plan.Count);
        Assert.All(plan.Where(p => p.Key.Slot < 100), p => Assert.False(p.Value.DefuseKit));
        Assert.All(plan.Where(p => p.Key.Slot >= 100), p => Assert.True(p.Value.DefuseKit));
        Assert.All(plan.Values, l => Assert.Equal(ArmorKind.KevlarHelmet, l.Armor));
        Assert.All(plan.Values, l => Assert.False(l.Zeus));
        Assert.Equal("weapon_ak47", plan[new PlayerId(1)].Primary);
    }

    [Fact]
    public void Plan_AwpRecipient_GetsAwpAsPrimary()
    {
        var plan = LoadoutPlanner.Plan(FullBuy, Lobby(3, 3, new LoadoutPreference(null, null, true)), Array.Empty<GrenadeKit>(), new FixedRandom { DoubleValue = 0.0 });
        Assert.Equal(2, plan.Values.Count(l => l.Primary == WeaponCatalog.Awp));
    }

    [Fact]
    public void Plan_ZeusEnabledAtFullChance_GivesZeus()
    {
        var definition = FullBuy with { Zeus = new ZeusSettings(true, 100) };
        var plan = LoadoutPlanner.Plan(definition, Lobby(1, 1), Array.Empty<GrenadeKit>(), new FixedRandom());
        Assert.All(plan.Values, l => Assert.True(l.Zeus));
    }
}
