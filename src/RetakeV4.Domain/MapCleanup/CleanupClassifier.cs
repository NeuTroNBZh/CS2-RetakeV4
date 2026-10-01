namespace RetakeV4.Domain.MapCleanup;

// Conservative on purpose: anything not clearly a door, a window or a vent is never touched (the old plugin broke physics
// props and map brushes by guessing from class names).
public static class CleanupClassifier
{
    public static IReadOnlyList<string> CandidateClasses { get; } = new[]
    {
        "func_door", "func_door_rotating", "prop_door_rotating", "func_breakable", "func_shatterglass",
    };

    private static readonly string[] DoorClasses = { "func_door", "func_door_rotating", "prop_door_rotating" };
    private static readonly string[] WindowTokens = { "glass", "window" };
    private static readonly string[] VentTokens = { "vent", "grate" };

    public static CleanupKind Classify(EntityFacts facts, string key, IReadOnlyDictionary<string, CleanupKind> overrides) =>
        overrides.TryGetValue(key, out var kind) ? kind : Detect(facts);

    public static CleanupKind Detect(EntityFacts facts)
    {
        var cls = facts.ClassName.ToLowerInvariant();
        if (DoorClasses.Contains(cls))
        {
            return CleanupKind.Door;
        }
        if (cls == "func_shatterglass")
        {
            return CleanupKind.Window;
        }
        if (cls != "func_breakable" || string.IsNullOrEmpty(facts.ModelName))
        {
            return CleanupKind.Ignore;
        }
        var model = facts.ModelName.ToLowerInvariant();
        if (WindowTokens.Any(model.Contains))
        {
            return CleanupKind.Window;
        }
        return VentTokens.Any(model.Contains) ? CleanupKind.Vent : CleanupKind.Ignore;
    }
}
