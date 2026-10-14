using RetakeV4.Domain.Common;
using RetakeV4.Domain.Geometry;

namespace RetakeV4.Domain.Stats;

// grenade_thrown carries no entity: a detonation takes the oldest pending throw of the same player and type.
// Blinds link to flashes by entity id; utility damage goes to the attacker's latest detonation of that type, enemies only.
internal static class GrenadeLinker
{
    // Plain properties (no captured primary-constructor parameters: CS9124 would fail the build).
    private sealed class Grenade(ulong thrower, GrenadeType type, int? thrownMs, int? detonatedMs, Vec3? throwPosition, Vec3? detonationPosition, int? entityId)
    {
        public ulong Thrower { get; } = thrower;

        public GrenadeType Type { get; } = type;

        public int? ThrownMs { get; } = thrownMs;

        public int? DetonatedMs { get; } = detonatedMs;

        public Vec3? ThrowPosition { get; } = throwPosition;

        public Vec3? DetonationPosition { get; } = detonationPosition;

        public int? EntityId { get; } = entityId;

        public int SortKey => DetonatedMs ?? ThrownMs ?? 0;

        public int Damage { get; set; }

        public int EnemiesBlinded { get; set; }

        public int TeammatesBlinded { get; set; }

        public double BlindSeconds { get; set; }

        public GrenadeSummary Build() => new(
            Thrower, Type, ThrownMs, DetonatedMs, ThrowPosition, DetonationPosition, Damage, EnemiesBlinded, TeammatesBlinded, Math.Round(BlindSeconds, 3));
    }

    public static IReadOnlyList<GrenadeSummary> Link(RoundRecord record, IReadOnlyDictionary<ulong, TeamSide> sides)
    {
        var pending = new Dictionary<(ulong, GrenadeType), Queue<GrenadeThrown>>();
        foreach (var thrown in record.GrenadesThrown.OrderBy(t => t.TimeMs))
        {
            var key = (thrown.Thrower, thrown.Type);
            if (!pending.TryGetValue(key, out var queue))
            {
                queue = new Queue<GrenadeThrown>();
                pending[key] = queue;
            }
            queue.Enqueue(thrown);
        }
        var grenades = new List<Grenade>();
        foreach (var detonation in record.GrenadesDetonated.OrderBy(d => d.TimeMs))
        {
            var thrown = pending.TryGetValue((detonation.Thrower, detonation.Type), out var queue) && queue.Count > 0 ? queue.Dequeue() : null;
            grenades.Add(new Grenade(detonation.Thrower, detonation.Type, thrown?.TimeMs, detonation.TimeMs, thrown?.Position, detonation.Position, detonation.EntityId));
        }
        foreach (var thrown in pending.Values.SelectMany(q => q))
        {
            grenades.Add(new Grenade(thrown.Thrower, thrown.Type, thrown.TimeMs, null, thrown.Position, null, null));
        }
        LinkBlinds(record.Blinds, sides, grenades);
        LinkDamage(record.Damages, sides, grenades);
        return grenades.OrderBy(g => g.SortKey).Select(g => g.Build()).ToList();
    }

    private static void LinkBlinds(IReadOnlyList<PlayerBlinded> blinds, IReadOnlyDictionary<ulong, TeamSide> sides, List<Grenade> grenades)
    {
        foreach (var blind in blinds)
        {
            if (blind.Seconds <= 0 || blind.Attacker is not { } attacker || attacker == blind.Victim
                || !sides.TryGetValue(attacker, out var attackerSide) || !sides.TryGetValue(blind.Victim, out var victimSide))
            {
                continue;
            }
            var flash = grenades.FirstOrDefault(g => g.Type == GrenadeType.Flash && g.Thrower == attacker && g.EntityId == blind.EntityId);
            if (flash is null)
            {
                continue;
            }
            if (attackerSide == victimSide)
            {
                flash.TeammatesBlinded++;
            }
            else
            {
                flash.EnemiesBlinded++;
                flash.BlindSeconds += blind.Seconds;
            }
        }
    }

    private static void LinkDamage(IReadOnlyList<DamageDealt> damages, IReadOnlyDictionary<ulong, TeamSide> sides, List<Grenade> grenades)
    {
        foreach (var hurt in damages)
        {
            if (hurt.VictimIsBot || hurt.Attacker is not { } attacker || !StatsWeapons.IsUtility(hurt.Weapon)
                || !sides.TryGetValue(attacker, out var attackerSide) || !sides.TryGetValue(hurt.Victim, out var victimSide) || attackerSide == victimSide)
            {
                continue;
            }
            var type = StatsWeapons.Normalize(hurt.Weapon) == "hegrenade" ? GrenadeType.He : GrenadeType.Molotov;
            var source = grenades
                .Where(g => g.Thrower == attacker && g.Type == type && g.DetonatedMs is { } at && at <= hurt.TimeMs)
                .MaxBy(g => g.DetonatedMs);
            if (source is not null)
            {
                source.Damage += Math.Clamp(hurt.DmgHealth, 0, 100);
            }
        }
    }
}
