namespace RetakeV4.Domain.Modules;

public static class ModuleLoadPlanner
{
    public static ModuleLoadPlan Plan(IReadOnlyList<ModuleDescriptor> modules)
    {
        EnsureUniqueNames(modules);
        var unavailable = FindUnavailable(modules);
        var candidates = modules.Where(m => !unavailable.ContainsKey(m.Name)).ToList();
        var (order, cyclic) = TopologicalOrder(candidates);
        var skipped = unavailable.Values
            .Concat(cyclic.Select(name => new SkippedModule(name, SkipReason.DependencyCycle, "dependency cycle")))
            .ToList();
        return new ModuleLoadPlan(order, skipped);
    }

    private static void EnsureUniqueNames(IReadOnlyList<ModuleDescriptor> modules)
    {
        var duplicate = modules.GroupBy(m => m.Name, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            throw new ArgumentException($"Duplicate module name '{duplicate.Key}'", nameof(modules));
        }
    }

    private static Dictionary<string, SkippedModule> FindUnavailable(IReadOnlyList<ModuleDescriptor> modules)
    {
        var known = modules.Select(m => m.Name).ToHashSet(StringComparer.Ordinal);
        var skipped = modules
            .Where(m => !m.Enabled)
            .ToDictionary(m => m.Name, m => new SkippedModule(m.Name, SkipReason.Disabled, "disabled in config"), StringComparer.Ordinal);
        bool changed;
        do
        {
            changed = false;
            foreach (var module in modules.Where(m => !skipped.ContainsKey(m.Name)))
            {
                var missing = module.DependsOn.FirstOrDefault(d => !known.Contains(d) || skipped.ContainsKey(d));
                if (missing is null)
                {
                    continue;
                }
                var detail = known.Contains(missing) ? $"dependency '{missing}' is not loaded" : $"dependency '{missing}' does not exist";
                skipped[module.Name] = new SkippedModule(module.Name, SkipReason.MissingDependency, detail);
                changed = true;
            }
        }
        while (changed);
        return skipped;
    }

    private static (List<string> Order, List<string> Cyclic) TopologicalOrder(List<ModuleDescriptor> candidates)
    {
        var remaining = candidates.ToList();
        var order = new List<string>();
        var placed = new HashSet<string>(StringComparer.Ordinal);
        while (remaining.Count > 0)
        {
            var next = remaining.FirstOrDefault(m => m.DependsOn.All(placed.Contains));
            if (next is null)
            {
                break;
            }
            order.Add(next.Name);
            placed.Add(next.Name);
            remaining.Remove(next);
        }
        return (order, remaining.Select(m => m.Name).ToList());
    }
}
