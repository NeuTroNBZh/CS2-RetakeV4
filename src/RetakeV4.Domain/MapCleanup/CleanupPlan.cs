using RetakeV4.Domain.Common;

namespace RetakeV4.Domain.MapCleanup;

public sealed record CleanupSettings(bool OpenDoors, int DoorOpenChancePercent, bool BreakWindows, bool BreakVents, int MaxEntities);

public sealed record CleanupCandidate(int Handle, EntityFacts Facts, string Key);

public enum CleanupAction
{
    Open,
    Break,
}

public sealed record CleanupTarget(int Handle, CleanupKind Kind, CleanupAction Action);

public static class CleanupPlan
{
    public static IReadOnlyList<CleanupTarget> Build(IReadOnlyList<CleanupCandidate> candidates,
        IReadOnlyDictionary<string, CleanupKind> overrides, CleanupSettings settings, IRandom random)
    {
        var targets = new List<CleanupTarget>();
        foreach (var candidate in candidates)
        {
            if (targets.Count >= settings.MaxEntities)
            {
                break;
            }
            if (TargetFor(candidate, overrides, settings, random) is { } target)
            {
                targets.Add(target);
            }
        }
        return targets;
    }

    private static CleanupTarget? TargetFor(CleanupCandidate candidate, IReadOnlyDictionary<string, CleanupKind> overrides,
        CleanupSettings settings, IRandom random)
    {
        var kind = CleanupClassifier.Classify(candidate.Facts, candidate.Key, overrides);
        return kind switch
        {
            CleanupKind.Door when settings.OpenDoors && DoorRoll.ShouldOpen(settings.DoorOpenChancePercent, random)
                => new CleanupTarget(candidate.Handle, kind, CleanupAction.Open),
            CleanupKind.Window when settings.BreakWindows => new CleanupTarget(candidate.Handle, kind, CleanupAction.Break),
            CleanupKind.Vent when settings.BreakVents => new CleanupTarget(candidate.Handle, kind, CleanupAction.Break),
            _ => null,
        };
    }
}
