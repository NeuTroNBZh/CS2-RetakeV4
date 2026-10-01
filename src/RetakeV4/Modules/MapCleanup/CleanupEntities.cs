using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using RetakeV4.Domain.Geometry;
using RetakeV4.Domain.MapCleanup;

namespace RetakeV4.Modules.MapCleanup;

// The only place that touches map entities: reads the five candidate classes and sends Open or Break, nothing else.
internal static class CleanupEntities
{
    public static IReadOnlyList<CleanupCandidate> Candidates() =>
        CleanupCandidates.Build(CleanupClassifier.CandidateClasses
            .SelectMany(Utilities.FindAllEntitiesByDesignerName<CBaseEntity>)
            .Where(e => e.IsValid)
            .Select(e => ((int)e.Index, Facts(e))));

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
        // The native call may hand back a wrapper around a null pointer for nodes without a skeleton.
        var skeleton = entity.CBodyComponent?.SceneNode?.GetSkeletonInstance();
        var model = skeleton is not null && skeleton.Handle != IntPtr.Zero ? skeleton.ModelState.ModelName : null;
        var name = entity.Entity?.Name;
        return new EntityFacts(entity.DesignerName, string.IsNullOrEmpty(model) ? null : model, string.IsNullOrEmpty(name) ? null : name,
            origin is null ? new Vec3(0, 0, 0) : new Vec3(origin.X, origin.Y, origin.Z));
    }
}
