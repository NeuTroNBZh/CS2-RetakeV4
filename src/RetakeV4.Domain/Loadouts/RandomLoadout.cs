using RetakeV4.Domain.Common;

namespace RetakeV4.Domain.Loadouts;

public enum RandomWeaponsMode
{
    Once,
    Sticky,
}

// Mode null: random weapons off.
public sealed record RandomWeaponsRequest(RandomWeaponsMode? Mode)
{
    public static RandomWeaponsRequest? Parse(string? argument) => argument?.Trim().ToLowerInvariant() switch
    {
        null or "" or "once" => new RandomWeaponsRequest(RandomWeaponsMode.Once),
        "sticky" or "on" => new RandomWeaponsRequest(RandomWeaponsMode.Sticky),
        "off" => new RandomWeaponsRequest((RandomWeaponsMode?)null),
        _ => null,
    };
}

public sealed record RandomWeaponsSwitch(RandomWeaponsMode? Mode)
{
    public static RandomWeaponsSwitch Off { get; } = new((RandomWeaponsMode?)null);

    // Read once per round: a "once" switch turns itself off after the round it was used for.
    public (bool Active, RandomWeaponsSwitch Next) Consume() => Mode switch
    {
        RandomWeaponsMode.Once => (true, Off),
        RandomWeaponsMode.Sticky => (true, this),
        _ => (false, this),
    };
}

public sealed record WeaponPair(string? Primary, string Secondary);

public static class RandomLoadout
{
    public static IReadOnlyDictionary<PlayerId, WeaponPair> Assign(
        RoundTypeDefinition definition, IReadOnlyList<LoadoutRequest> players, IRandom random)
    {
        var primaries = Draw(PoolOf(definition.Primaries), players, random);
        var secondaries = Draw(PoolOf(definition.Secondaries), players, random);
        // An empty pool keeps the round type's default for that slot (no primary on a pistol round).
        return players.ToDictionary(
            p => p.Player,
            p => new WeaponPair(
                primaries.GetValueOrDefault(p.Player) ?? definition.DefaultFor(p.Team).Primary,
                secondaries.GetValueOrDefault(p.Player) ?? definition.DefaultFor(p.Team).Secondary));
    }

    // Both sides' weapons for everyone: a random round can hand a CT an AK-47. The AWP keeps its own volunteer system.
    public static IReadOnlyList<string> PoolOf(TeamWeapons weapons) =>
        weapons.T.Concat(weapons.CT).Concat(weapons.Any)
            .Where(w => w != WeaponCatalog.Awp)
            .Distinct(StringComparer.Ordinal)
            .ToList();

    // Every team receives the same tiers (the smaller team the first ones), each team with its own weapons of those tiers.
    private static Dictionary<PlayerId, string> Draw(IReadOnlyList<string> pool, IReadOnlyList<LoadoutRequest> players, IRandom random)
    {
        var drawn = new Dictionary<PlayerId, string>();
        if (pool.Count == 0 || players.Count == 0)
        {
            return drawn;
        }
        var teams = players.GroupBy(p => p.Team).Select(g => random.Shuffle(g.Select(p => p.Player))).ToList();
        var tiers = DrawTiers(pool, teams.Max(t => t.Count), random);
        foreach (var team in teams)
        {
            var bags = new TierBags(pool, random);
            for (var i = 0; i < team.Count; i++)
            {
                drawn[team[i]] = bags.Take(tiers[i]);
            }
        }
        return drawn;
    }

    // Tiers follow the pool's own mix, one full shuffled pass at a time: a team never has more players of a tier than the pool
    // has weapons of it until every weapon is used, so teammates only share a weapon when the pool is smaller than the team.
    private static IReadOnlyList<WeaponTier> DrawTiers(IReadOnlyList<string> pool, int count, IRandom random)
    {
        var tiers = new List<WeaponTier>(count);
        while (tiers.Count < count)
        {
            tiers.AddRange(random.Shuffle(pool).Select(WeaponTiers.Of));
        }
        return tiers.Take(count).ToList();
    }

    private sealed class TierBags(IReadOnlyList<string> pool, IRandom random)
    {
        private readonly Dictionary<WeaponTier, Queue<string>> _bags = new();

        public string Take(WeaponTier tier)
        {
            if (!_bags.TryGetValue(tier, out var bag) || bag.Count == 0)
            {
                bag = new Queue<string>(random.Shuffle(pool.Where(w => WeaponTiers.Of(w) == tier)));
                _bags[tier] = bag;
            }
            return bag.Dequeue();
        }
    }
}
