using RetakeV4.Domain.Modules;

namespace RetakeV4.Domain.Tests.Modules;

public class ModuleLoadPlannerTests
{
    private static ModuleDescriptor M(string name, bool enabled = true, params string[] deps) => new(name, deps, enabled);

    [Fact]
    public void Plan_OrdersDependenciesFirst_StableOtherwise()
    {
        var plan = ModuleLoadPlanner.Plan(new[] { M("Hud", true, "Core"), M("Core"), M("Links") });
        Assert.Equal(new[] { "Core", "Hud", "Links" }, plan.Order);
        Assert.Empty(plan.Skipped);
    }

    [Fact]
    public void DisabledModule_IsSkipped_AndItsDependentsToo()
    {
        var plan = ModuleLoadPlanner.Plan(new[] { M("Core"), M("Spawns", false, "Core"), M("Plant", true, "Spawns") });
        Assert.Equal(new[] { "Core" }, plan.Order);
        Assert.Contains(plan.Skipped, s => s is { Name: "Spawns", Reason: SkipReason.Disabled });
        Assert.Contains(plan.Skipped, s => s is { Name: "Plant", Reason: SkipReason.MissingDependency });
    }

    [Fact]
    public void UnknownDependency_IsReported()
    {
        var plan = ModuleLoadPlanner.Plan(new[] { M("Hud", true, "Ghost") });
        var skipped = Assert.Single(plan.Skipped);
        Assert.Equal(SkipReason.MissingDependency, skipped.Reason);
        Assert.Contains("Ghost", skipped.Detail);
    }

    [Fact]
    public void Cycle_SkipsAllCyclicModules()
    {
        var plan = ModuleLoadPlanner.Plan(new[] { M("A", true, "B"), M("B", true, "A"), M("C") });
        Assert.Equal(new[] { "C" }, plan.Order);
        Assert.Equal(2, plan.Skipped.Count(s => s.Reason == SkipReason.DependencyCycle));
    }

    [Fact]
    public void DuplicateNames_Throw()
    {
        Assert.Throws<ArgumentException>(() => ModuleLoadPlanner.Plan(new[] { M("Core"), M("Core") }));
    }
}
