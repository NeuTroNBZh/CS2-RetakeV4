using System.Collections.Immutable;
using RetakeV4.Domain.Geometry;

namespace RetakeV4.Domain.InstaDefuse;

public sealed record ThreatState(int HeInFlight, int MolotovInFlight, ImmutableHashSet<int> NearInfernos)
{
    private const string WeaponPrefix = "weapon_";

    public static ThreatState Empty { get; } = new(0, 0, ImmutableHashSet<int>.Empty);

    public ThreatState GrenadeThrown(string weapon) => Normalize(weapon) switch
    {
        "hegrenade" => this with { HeInFlight = HeInFlight + 1 },
        "molotov" or "incgrenade" => this with { MolotovInFlight = MolotovInFlight + 1 },
        _ => this,
    };

    public ThreatState HeDetonated() => this with { HeInFlight = Math.Max(0, HeInFlight - 1) };

    public ThreatState MolotovDetonated() => this with { MolotovInFlight = Math.Max(0, MolotovInFlight - 1) };

    public ThreatState InfernoStarted(int id, Vec3 fire, Vec3? bomb, float maxDistance) =>
        bomb is { } bombPosition && (fire - bombPosition).Length <= maxDistance
            ? this with { NearInfernos = NearInfernos.Add(id) }
            : this;

    public ThreatState InfernoEnded(int id) => this with { NearInfernos = NearInfernos.Remove(id) };

    private static string Normalize(string weapon) =>
        weapon.StartsWith(WeaponPrefix, StringComparison.Ordinal) ? weapon[WeaponPrefix.Length..] : weapon;
}
