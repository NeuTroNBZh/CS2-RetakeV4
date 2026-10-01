using RetakeV4.Domain.Common;

namespace RetakeV4.Domain.Loadouts;

public sealed record WeaponSwap(bool Primary, bool Secondary);

public static class LoadoutPlanner
{
    public static IReadOnlyDictionary<PlayerId, Loadout> Plan(
        RoundTypeDefinition definition, IReadOnlyList<LoadoutRequest> players, IReadOnlyList<GrenadeKit> grenadeKits, IRandom random)
    {
        var awp = PickAwpRecipients(definition.Awp, players, random);
        var cts = players.Where(p => p.Team == TeamSide.CT).Select(p => p.Player).ToList();
        var kits = PickDefuseKits(definition.DefuseKit, cts, random);
        return players.ToDictionary(
            p => p.Player,
            p => Build(definition, p, awp.Contains(p.Player), kits.Contains(p.Player), grenadeKits, random));
    }

    public static (string? Primary, string Secondary) ResolveWeapons(RoundTypeDefinition definition, LoadoutRequest request)
    {
        var fallback = definition.DefaultFor(request.Team);
        var preference = request.Preference;
        var primary = preference?.Primary is { } p && definition.Primaries.For(request.Team).Contains(p) ? p : fallback.Primary;
        var secondary = preference?.Secondary is { } s && definition.Secondaries.For(request.Team).Contains(s) ? s : fallback.Secondary;
        return (primary, secondary);
    }

    // A freeze-time weapon change keeps what was already handed out (AWP, armor, kit, Zeus, grenades) and only swaps the guns.
    public static Loadout WithWeapons(Loadout current, RoundTypeDefinition definition, LoadoutRequest request)
    {
        var (primary, secondary) = ResolveWeapons(definition, request);
        return current with { Primary = current.Primary == WeaponCatalog.Awp ? WeaponCatalog.Awp : primary, Secondary = secondary };
    }

    public static WeaponSwap SwapBetween(Loadout before, Loadout after) =>
        new(before.Primary != after.Primary, before.Secondary != after.Secondary);

    public static IReadOnlySet<PlayerId> PickAwpRecipients(AwpSettings settings, IReadOnlyList<LoadoutRequest> players, IRandom random)
    {
        var recipients = new HashSet<PlayerId>();
        if (!settings.Enabled || players.Count < settings.MinActivePlayers)
        {
            return recipients;
        }
        foreach (var side in new[] { TeamSide.T, TeamSide.CT })
        {
            var volunteers = random.Shuffle(players.Where(p => p.Team == side && p.Preference?.AwpOptIn == true).Select(p => p.Player));
            foreach (var volunteer in volunteers.Take(Math.Max(0, settings.MaxPerTeam)))
            {
                if (Chance.Roll(settings.Chance, random))
                {
                    recipients.Add(volunteer);
                }
            }
        }
        return recipients;
    }

    public static IReadOnlySet<PlayerId> PickDefuseKits(DefuseKitSettings settings, IReadOnlyList<PlayerId> cts, IRandom random)
    {
        var chosen = settings.Mode switch
        {
            DefuseKitMode.All => cts.ToHashSet(),
            DefuseKitMode.Quota => random.Shuffle(cts).Take(Math.Max(0, settings.Quota)).ToHashSet(),
            _ => cts.Where(_ => Chance.Roll(settings.Chance, random)).ToHashSet(),
        };
        if (settings.GuaranteeMinimum && chosen.Count == 0 && cts.Count > 0)
        {
            chosen.Add(random.Pick(cts));
        }
        return chosen;
    }

    public static IReadOnlyList<string> PickGrenades(IReadOnlyList<GrenadeKit> kits, TeamSide team, IRandom random)
    {
        var eligible = kits.Where(k => k.Team is null || k.Team == team).ToList();
        return eligible.Count == 0 ? Array.Empty<string>() : random.Pick(eligible).Grenades;
    }

    private static Loadout Build(
        RoundTypeDefinition definition, LoadoutRequest request, bool awp, bool kit, IReadOnlyList<GrenadeKit> grenadeKits, IRandom random)
    {
        var (primary, secondary) = ResolveWeapons(definition, request);
        var zeus = definition.Zeus.Enabled && Chance.Roll(definition.Zeus.Chance, random);
        return new Loadout(
            awp ? WeaponCatalog.Awp : primary,
            secondary,
            definition.Armor,
            kit,
            zeus,
            PickGrenades(grenadeKits, request.Team, random));
    }
}
