using RetakeV4.Domain.Common;

namespace RetakeV4.Domain.Spawns;

public sealed record SpawnRequest(PlayerId Player, TeamSide Team);

public sealed record SpawnAssignment(PlayerId Player, SpawnPoint Spawn);

public sealed record PlacementResult(IReadOnlyList<SpawnAssignment> Assignments, PlayerId? Planter, IReadOnlyList<PlayerId> Unplaced);

public static class SpawnSelector
{
    public static PlacementResult Place(IReadOnlyList<SpawnRequest> players, IReadOnlyList<SpawnPoint> spawns, BombSite site, IRandom random)
    {
        var siteSpawns = spawns.Where(s => s.Site == site).ToList();
        var terrorists = players.Where(p => p.Team == TeamSide.T).ToList();
        PlayerId? planter = terrorists.Count > 0 ? random.Pick(terrorists).Player : null;
        var assignments = new List<SpawnAssignment>();
        var unplaced = new List<PlayerId>();
        var used = new HashSet<Guid>();

        if (planter is { } planterId)
        {
            var planterSpawn = PickPlanterSpawn(siteSpawns, random);
            AddOrMark(planterId, planterSpawn, assignments, unplaced, used);
        }
        foreach (var request in random.Shuffle(players.Where(p => p.Player != planter)))
        {
            var free = random.Shuffle(siteSpawns.Where(s => s.Team == request.Team && !used.Contains(s.Id)));
            AddOrMark(request.Player, free.Count > 0 ? free[0] : null, assignments, unplaced, used);
        }
        return new PlacementResult(assignments, planter, unplaced);
    }

    private static SpawnPoint? PickPlanterSpawn(IReadOnlyList<SpawnPoint> siteSpawns, IRandom random)
    {
        var plantable = siteSpawns.Where(s => s.Team == TeamSide.T && s.CanPlant).ToList();
        var pool = plantable.Count > 0 ? plantable : siteSpawns.Where(s => s.Team == TeamSide.T).ToList();
        return pool.Count > 0 ? random.Pick(pool) : null;
    }

    private static void AddOrMark(PlayerId player, SpawnPoint? spawn, List<SpawnAssignment> assignments, List<PlayerId> unplaced, HashSet<Guid> used)
    {
        if (spawn is null)
        {
            unplaced.Add(player);
            return;
        }
        used.Add(spawn.Id);
        assignments.Add(new SpawnAssignment(player, spawn));
    }
}
