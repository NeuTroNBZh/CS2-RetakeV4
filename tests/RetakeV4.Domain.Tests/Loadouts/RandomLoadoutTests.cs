using RetakeV4.Domain.Common;
using RetakeV4.Domain.Loadouts;

namespace RetakeV4.Domain.Tests.Loadouts;

public class RandomLoadoutTests
{
    private static readonly RoundTypeDefinition FullBuy = new(
        "FullBuy", ArmorKind.KevlarHelmet,
        new TeamWeapons(
            new[] { "weapon_ak47", "weapon_mac10" },
            new[] { "weapon_m4a1", "weapon_mp9" },
            new[] { "weapon_awp", "weapon_famas", "weapon_ump45", "weapon_aug" }),
        new TeamWeapons(new[] { "weapon_glock" }, new[] { "weapon_usp_silencer" }, new[] { "weapon_deagle", "weapon_p250" }),
        new TeamDefault("weapon_ak47", "weapon_deagle"),
        new TeamDefault("weapon_m4a1", "weapon_deagle"),
        new AwpSettings(true, 1, 1, 100),
        new DefuseKitSettings(DefuseKitMode.All, 1, 100, false),
        new ZeusSettings(false, 0),
        "Default");

    private static readonly RoundTypeDefinition Pistol = FullBuy with
    {
        Name = "Pistol",
        Primaries = TeamWeapons.Empty,
        DefaultT = new TeamDefault(null, "weapon_glock"),
        DefaultCT = new TeamDefault(null, "weapon_usp_silencer"),
    };

    private static IReadOnlyList<LoadoutRequest> Lobby(int t, int ct) =>
        Enumerable.Range(1, t).Select(i => new LoadoutRequest(new PlayerId(i), TeamSide.T, null))
            .Concat(Enumerable.Range(100, ct).Select(i => new LoadoutRequest(new PlayerId(i), TeamSide.CT, null)))
            .ToList();

    private static IReadOnlyList<WeaponPair> Team(IReadOnlyDictionary<PlayerId, WeaponPair> weapons, TeamSide side) =>
        weapons.Where(w => side == TeamSide.T ? w.Key.Slot < 100 : w.Key.Slot >= 100).Select(w => w.Value).ToList();

    [Fact]
    public void Pool_MixesBothTeams_AndLeavesTheAwpOut() =>
        Assert.Equal(
            new[] { "weapon_ak47", "weapon_mac10", "weapon_m4a1", "weapon_mp9", "weapon_famas", "weapon_ump45", "weapon_aug" },
            RandomLoadout.PoolOf(FullBuy.Primaries));

    [Fact]
    public void EveryPlayer_GetsWeaponsFromTheRoundTypePools()
    {
        var weapons = RandomLoadout.Assign(FullBuy, Lobby(4, 5), SystemRandom.Shared);
        Assert.Equal(9, weapons.Count);
        Assert.All(weapons.Values, w => Assert.Contains(w.Primary, RandomLoadout.PoolOf(FullBuy.Primaries)));
        Assert.All(weapons.Values, w => Assert.Contains(w.Secondary, RandomLoadout.PoolOf(FullBuy.Secondaries)));
    }

    [Fact]
    public void PistolRound_GivesNoPrimary()
    {
        var weapons = RandomLoadout.Assign(Pistol, Lobby(2, 3), SystemRandom.Shared);
        Assert.All(weapons.Values, w => Assert.Null(w.Primary));
        Assert.All(weapons.Values, w => Assert.Contains(w.Secondary, RandomLoadout.PoolOf(Pistol.Secondaries)));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(42)]
    [InlineData(1234)]
    public void Teammates_NeverShareAWeapon_WhileThePoolIsLargeEnough(int seed)
    {
        var weapons = RandomLoadout.Assign(FullBuy, Lobby(4, 4), new SystemRandom(new Random(seed)));
        foreach (var side in new[] { TeamSide.T, TeamSide.CT })
        {
            var team = Team(weapons, side);
            Assert.Equal(team.Count, team.Select(w => w.Primary).Distinct().Count());
            Assert.Equal(team.Count, team.Select(w => w.Secondary).Distinct().Count());
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(42)]
    [InlineData(1234)]
    public void BothTeams_GetTheSameTiers(int seed)
    {
        var weapons = RandomLoadout.Assign(FullBuy, Lobby(4, 4), new SystemRandom(new Random(seed)));
        var t = Team(weapons, TeamSide.T);
        var ct = Team(weapons, TeamSide.CT);
        Assert.Equal(t.Select(w => WeaponTiers.Of(w.Primary!)).Order(), ct.Select(w => WeaponTiers.Of(w.Primary!)).Order());
        Assert.Equal(t.Select(w => WeaponTiers.Of(w.Secondary)).Order(), ct.Select(w => WeaponTiers.Of(w.Secondary)).Order());
    }

    [Theory]
    [InlineData(3)]
    [InlineData(99)]
    public void SmallerTeam_GetsTheFirstTiersOfTheLargerOne(int seed)
    {
        var weapons = RandomLoadout.Assign(FullBuy, Lobby(2, 4), new SystemRandom(new Random(seed)));
        var t = Team(weapons, TeamSide.T).Select(w => WeaponTiers.Of(w.Primary!)).ToList();
        var ct = Team(weapons, TeamSide.CT).Select(w => WeaponTiers.Of(w.Primary!)).ToList();
        Assert.All(t.GroupBy(x => x), g => Assert.True(ct.Count(x => x == g.Key) >= g.Count()));
    }

    [Fact]
    public void MorePlayersThanWeapons_RepeatsWeaponsInsteadOfFailing()
    {
        var tiny = FullBuy with { Secondaries = new TeamWeapons(new[] { "weapon_glock" }, Array.Empty<string>(), Array.Empty<string>()) };
        var weapons = RandomLoadout.Assign(tiny, Lobby(3, 3), SystemRandom.Shared);
        Assert.All(weapons.Values, w => Assert.Equal("weapon_glock", w.Secondary));
    }

    [Theory]
    [InlineData(5)]
    [InlineData(77)]
    [InlineData(2026)]
    public void SecondPassOverThePool_StillGivesBothTeamsTheSameTiers(int seed)
    {
        var weapons = RandomLoadout.Assign(FullBuy, Lobby(9, 8), new SystemRandom(new Random(seed)));
        var t = Team(weapons, TeamSide.T).Select(w => WeaponTiers.Of(w.Primary!)).ToList();
        var ct = Team(weapons, TeamSide.CT).Select(w => WeaponTiers.Of(w.Primary!)).ToList();
        Assert.All(ct.GroupBy(x => x), g => Assert.True(t.Count(x => x == g.Key) >= g.Count()));
        Assert.Equal(RandomLoadout.PoolOf(FullBuy.Primaries).Count, Team(weapons, TeamSide.T).Select(w => w.Primary).Distinct().Count());
    }

    [Fact]
    public void EmptyPrimaryPool_KeepsTheTeamDefault()
    {
        var withDefault = Pistol with { DefaultT = new TeamDefault("weapon_ak47", "weapon_glock") };
        var weapons = RandomLoadout.Assign(withDefault, Lobby(1, 1), SystemRandom.Shared);
        Assert.Equal("weapon_ak47", weapons[new PlayerId(1)].Primary);
        Assert.Null(weapons[new PlayerId(100)].Primary);
    }

    [Fact]
    public void EmptySecondaryPool_FallsBackToTheTeamDefault()
    {
        var none = FullBuy with { Secondaries = TeamWeapons.Empty };
        var weapons = RandomLoadout.Assign(none, Lobby(1, 1), SystemRandom.Shared);
        Assert.Equal("weapon_deagle", weapons[new PlayerId(1)].Secondary);
    }

    [Fact]
    public void NoPlayers_GivesNothing() =>
        Assert.Empty(RandomLoadout.Assign(FullBuy, Array.Empty<LoadoutRequest>(), SystemRandom.Shared));
}

public class WeaponTiersTests
{
    [Theory]
    [InlineData("weapon_ak47", WeaponTier.Strong)]
    [InlineData("weapon_aug", WeaponTier.Strong)]
    [InlineData("weapon_famas", WeaponTier.Medium)]
    [InlineData("weapon_mac10", WeaponTier.Light)]
    [InlineData("weapon_deagle", WeaponTier.Strong)]
    [InlineData("weapon_p250", WeaponTier.Medium)]
    [InlineData("weapon_glock", WeaponTier.Light)]
    public void KnownWeapons_HaveTheirTier(string weapon, WeaponTier expected) => Assert.Equal(expected, WeaponTiers.Of(weapon));

    [Fact]
    public void UnknownWeapon_IsMedium() => Assert.Equal(WeaponTier.Medium, WeaponTiers.Of("weapon_future"));

    [Fact]
    public void EveryCatalogGun_HasAnExplicitTier() =>
        Assert.All(WeaponCatalog.Guns, gun => Assert.True(WeaponTiers.IsKnown(gun), gun));
}

public class RandomWeaponsSwitchTests
{
    [Fact]
    public void Off_KeepsNormalRounds() =>
        Assert.Equal((false, RandomWeaponsSwitch.Off), RandomWeaponsSwitch.Off.Consume());

    [Fact]
    public void Once_IsUsedByOneRoundThenTurnsOff() =>
        Assert.Equal((true, RandomWeaponsSwitch.Off), new RandomWeaponsSwitch(RandomWeaponsMode.Once).Consume());

    [Fact]
    public void Sticky_StaysOn()
    {
        var sticky = new RandomWeaponsSwitch(RandomWeaponsMode.Sticky);
        Assert.Equal((true, sticky), sticky.Consume());
    }

    [Theory]
    [InlineData("once", RandomWeaponsMode.Once)]
    [InlineData(null, RandomWeaponsMode.Once)]
    [InlineData("", RandomWeaponsMode.Once)]
    [InlineData(" STICKY ", RandomWeaponsMode.Sticky)]
    [InlineData("on", RandomWeaponsMode.Sticky)]
    public void Command_ParsesTheMode(string? argument, RandomWeaponsMode expected) =>
        Assert.Equal(new RandomWeaponsRequest(expected), RandomWeaponsRequest.Parse(argument));

    [Fact]
    public void Command_Off_ClearsTheSwitch() =>
        Assert.Equal(new RandomWeaponsRequest(null), RandomWeaponsRequest.Parse("off"));

    [Fact]
    public void Command_Unknown_IsRejected() => Assert.Null(RandomWeaponsRequest.Parse("forever"));
}
