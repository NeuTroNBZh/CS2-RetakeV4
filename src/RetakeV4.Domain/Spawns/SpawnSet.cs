using RetakeV4.Domain.Common;
using RetakeV4.Domain.Geometry;

namespace RetakeV4.Domain.Spawns;

public sealed record SpawnChange(TeamSide? Team = null, BombSite? Site = null, bool? CanPlant = null);

// The spawns being played or edited. Dirty = changed since the last load or save.
public sealed record SpawnSet(IReadOnlyList<SpawnPoint> Spawns, bool Dirty)
{
    public static SpawnSet Loaded(IReadOnlyList<SpawnPoint> spawns) => new(spawns, false);

    public SpawnSet Add(SpawnPoint spawn) => new(Spawns.Append(spawn).ToList(), true);

    public SpawnSet Remove(Guid id) =>
        IndexOf(id) < 0 ? this : new SpawnSet(Spawns.Where(s => s.Id != id).ToList(), true);

    public SpawnSet Update(Guid id, SpawnChange change) =>
        IndexOf(id) < 0 ? this : new SpawnSet(Spawns.Select(s => s.Id == id ? Apply(s, change) : s).ToList(), true);

    public SpawnSet MarkSaved() => this with { Dirty = false };

    public SpawnPoint? Nearest(Vec3 position, float maxDistance) =>
        Spawns
            .Select(s => (Spawn: s, Distance: (s.Position - position).Length))
            .Where(c => c.Distance <= maxDistance)
            .OrderBy(c => c.Distance)
            .Select(c => c.Spawn)
            .FirstOrDefault();

    public int IndexOf(Guid id) => Spawns.Select(s => s.Id).ToList().IndexOf(id);

    public string Label(SpawnPoint spawn) =>
        $"[{spawn.Team}][{spawn.Site}]{(spawn.CanPlant ? "[C4]" : string.Empty)} #{IndexOf(spawn.Id) + 1:D2}";

    public int Count(TeamSide team) => Spawns.Count(s => s.Team == team);

    private static SpawnPoint Apply(SpawnPoint spawn, SpawnChange change) => spawn with
    {
        Team = change.Team ?? spawn.Team,
        Site = change.Site ?? spawn.Site,
        CanPlant = change.CanPlant ?? spawn.CanPlant,
    };
}
