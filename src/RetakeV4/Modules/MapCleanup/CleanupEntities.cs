using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using RetakeV4.Domain.Geometry;
using RetakeV4.Domain.MapCleanup;

namespace RetakeV4.Modules.MapCleanup;

// The only place that touches map entities: reads the five candidate classes and sends Open or Break, nothing else.
internal static class CleanupEntities
{
    public static IReadOnlyList<CleanupCandidate> Candidates()
    {
        var entities = CleanupClassifier.CandidateClasses
            .SelectMany(Utilities.FindAllEntitiesByDesignerName<CBaseEntity>)
            .Where(e => e.IsValid)
            .Select(e => (Index: (int)e.Index, Facts: Facts(e)))
            .ToList();
        var nameCounts = entities
            .Select(e => e.Facts.TargetName)
            .OfType<string>()
            .Where(n => n.Length > 0)
            .GroupBy(n => n, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        return entities
            .Select(e => new CleanupCandidate(e.Index, e.Facts,
                EntityKey.For(e.Facts, e.Facts.TargetName is { } name && nameCounts.GetValueOrDefault(name) == 1)))
            .ToList();
    }

    // Sends the input only if the slot still holds an entity of the planned class: an index freed by a broken window
    // may be reused by something else before the freeze-end check.
    public static bool Apply(CleanupTarget target, string expectedClass)
    {
        var entity = Utilities.GetEntityFromIndex<CBaseEntity>(target.Handle);
        if (entity is not { IsValid: true } || !string.Equals(entity.DesignerName, expectedClass, StringComparison.Ordinal))
        {
            return false;
        }
        entity.AcceptInput(target.Action == CleanupAction.Open ? "Open" : "Break");
        return true;
    }

    private static EntityFacts Facts(CBaseEntity entity)
    {
        var origin = entity.AbsOrigin;
        var model = entity.CBodyComponent?.SceneNode?.GetSkeletonInstance()?.ModelState.ModelName;
        var name = entity.Entity?.Name;
        return new EntityFacts(entity.DesignerName, string.IsNullOrEmpty(model) ? null : model, string.IsNullOrEmpty(name) ? null : name,
            origin is null ? new Vec3(0, 0, 0) : new Vec3(origin.X, origin.Y, origin.Z));
    }
}
