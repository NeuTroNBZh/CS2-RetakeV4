using RetakeV4.Domain.Admin;
using RetakeV4.Domain.Geometry;
using RetakeV4.Domain.Hud;
using RetakeV4.Domain.InstaDefuse;
using RetakeV4.Domain.Loadouts;
using RetakeV4.Domain.RoundTypes;
using RetakeV4.Domain.Spawns;
using RetakeV4.Domain.Teams;

namespace RetakeV4.Domain.Common;

// The first round end runs most of the Domain for the first time: just-in-time compilation and type loading then stall the
// server long enough for the engine to kick the connected players. Running the same path once, off the game thread, at load
// pays that cost before anyone plays.
public static class DomainWarmup
{
    public static void Run()
    {
        var random = new SystemRandom(new Random(1));
        var first = new PlayerId(0);
        var second = new PlayerId(1);
        WarmTeams(first, second, random);
        var spawns = WarmSpawns(first, second, random);
        var definition = WarmLoadouts(first, second, random);
        WarmMenus(definition, spawns);
        WarmHud();
        InstaDefusePolicy.Evaluate(new InstaDefuseRules(true, true, true, true, true), ThreatState.Empty, new DefuseSituation(false, 10f, 5f, true));
        NativeBuy.Decide("weapon_ak47", TeamSide.T, definition);
        NativeBuyResolver.Resolve(new[] { "weapon_ak47" });
    }

    private static void WarmTeams(PlayerId first, PlayerId second, IRandom random)
    {
        var rules = new TeamRules(9, 0.499, 5, true);
        var state = TeamPlanner.RequestJoin(TeamState.Empty, first, 0, false, TeamSide.T, rules).State;
        state = TeamPlanner.RequestJoin(state, second, 0, false, TeamSide.CT, rules).State;
        state = TeamPlanner.Adopt(state, new[] { (first, TeamSide.T) }, rules);
        state = TeamPlanner.PlanRoundEnd(state, RoundWinner.None, false, rules, random).State;
        state = TeamPlanner.PlanRoundEnd(state, RoundWinner.T, true, rules, random).State;
        TeamPlanner.Reconcile(state, new Dictionary<PlayerId, TeamSide?> { [first] = TeamSide.T, [second] = null }, _ => 0);
        TeamPlanner.Leave(state, second);
    }

    private static IReadOnlyList<SpawnPoint> WarmSpawns(PlayerId first, PlayerId second, IRandom random)
    {
        var spawns = new[]
        {
            new SpawnPoint(Guid.NewGuid(), TeamSide.T, BombSite.A, true, new Vec3(0, 0, 0), new ViewAngles(0, 90)),
            new SpawnPoint(Guid.NewGuid(), TeamSide.CT, BombSite.A, false, new Vec3(100, 0, 0), new ViewAngles(0, 270)),
            new SpawnPoint(Guid.NewGuid(), TeamSide.T, BombSite.B, true, new Vec3(0, 100, 0), new ViewAngles(0, 90)),
            new SpawnPoint(Guid.NewGuid(), TeamSide.CT, BombSite.B, false, new Vec3(100, 100, 0), new ViewAngles(0, 270)),
        };
        var parsed = SpawnFileFormat.Parse(SpawnFileFormat.Serialize("warmup", spawns)).Spawns;
        var site = SiteSelector.Choose(SiteHistory.Empty, null, 2, new[] { BombSite.A, BombSite.B }, random).Site;
        SpawnSelector.Place(new[] { new SpawnRequest(first, TeamSide.T), new SpawnRequest(second, TeamSide.CT) }, parsed, site, random);
        return parsed;
    }

    private static RoundTypeDefinition WarmLoadouts(PlayerId first, PlayerId second, IRandom random)
    {
        var rules = new RoundTypeRules(RoundTypeMode.Random, new[] { "Pistol", "FullBuy" }, new[] { new RoundTypeSequenceEntry("Pistol", 1) }, "FullBuy");
        RoundTypeSelector.Select(rules, 0, random);
        RoundTypeSelector.Select(rules with { Mode = RoundTypeMode.Sequence }, 3, random);
        var definition = new RoundTypeDefinition(
            "FullBuy", ArmorKind.KevlarHelmet,
            new TeamWeapons(new[] { "weapon_ak47" }, new[] { "weapon_m4a1" }, Array.Empty<string>()),
            new TeamWeapons(new[] { "weapon_glock" }, new[] { "weapon_usp_silencer" }, new[] { "weapon_deagle" }),
            new TeamDefault("weapon_ak47", "weapon_glock"), new TeamDefault("weapon_m4a1", "weapon_usp_silencer"),
            new AwpSettings(true, 1, 0, 30), new DefuseKitSettings(DefuseKitMode.All, 1, 100, false), new ZeusSettings(true, 20), "Default");
        var requests = new[] { new LoadoutRequest(first, TeamSide.T, null), new LoadoutRequest(second, TeamSide.CT, null) };
        var grenades = new[] { new GrenadeKit(null, new[] { "weapon_flashbang" }) };
        LoadoutPlanner.Plan(definition, requests, grenades, random);
        return definition;
    }

    private static void WarmMenus(RoundTypeDefinition definition, IReadOnlyList<SpawnPoint> spawns)
    {
        var weapons = WeaponMenu.Build(new WeaponMenuState(new[] { definition }, definition, TeamSide.T, (_, _) => null, false));
        var editor = SpawnEditorMenu.Build(new SpawnEditorView(new SpawnSet(spawns, false), spawns[0], false));
        foreach (var menu in new[] { weapons, AdminMenu.Build(), editor })
        {
            var navigator = MenuNavigator.Open(menu);
            navigator.Lines();
            navigator.Move(1).Activate(0);
        }
        AimMenuLayout.Centered(5, 64f, 4f, 20f);
    }

    private static void WarmHud()
    {
        var now = DateTimeOffset.UtcNow;
        var info = RoundInfoWidget.Empty.Show(new RoundInfo("FullBuy", BombSite.A, 1, 1), now, TimeSpan.FromSeconds(6)).Render(now);
        var lines = CenterComposer.Compose(new[] { (10, info) }, 6);
        CenterHtml.Format(lines.Select(l => ("text", l.Style)).ToList(), new HudTheme("#FFFFFF", "#FFFFFF", "#FFFFFF"));
    }
}
