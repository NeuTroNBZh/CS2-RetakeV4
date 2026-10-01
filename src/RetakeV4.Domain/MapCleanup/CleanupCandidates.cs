namespace RetakeV4.Domain.MapCleanup;

public static class CleanupCandidates
{
    // The engine lookup matches designer names by substring, so the same entity can come back from several class queries
    // and unrelated classes can slip in: keep exact candidate classes, once per entity index.
    public static IReadOnlyList<CleanupCandidate> Build(IEnumerable<(int Handle, EntityFacts Facts)> found)
    {
        var entities = found
            .Where(e => CleanupClassifier.CandidateClasses.Contains(e.Facts.ClassName, StringComparer.Ordinal))
            .DistinctBy(e => e.Handle)
            .ToList();
        var nameCounts = entities
            .Select(e => e.Facts.TargetName)
            .OfType<string>()
            .GroupBy(n => n, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        return entities
            .Select(e => new CleanupCandidate(e.Handle, e.Facts,
                EntityKey.For(e.Facts, e.Facts.TargetName is { } name && nameCounts.GetValueOrDefault(name) == 1)))
            .ToList();
    }
}
