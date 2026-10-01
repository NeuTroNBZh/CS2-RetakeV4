using RetakeV4.Domain.Geometry;
using RetakeV4.Domain.MapCleanup;
using RetakeV4.Domain.Tests.TestDoubles;

namespace RetakeV4.Domain.Tests.MapCleanup;

public class CleanupPlanTests
{
    private static readonly CleanupSettings All = new(true, 100, true, true, 512);

    private static CleanupCandidate C(int handle, string cls, string? model = null) =>
        new(handle, new EntityFacts(cls, model, null, new Vec3(handle, 0, 0)), $"k{handle}");

    [Fact]
    public void DoorsOpen_WindowsAndVentsBreak_RestIgnored()
    {
        var plan = CleanupPlan.Build(new[] { C(1, "func_door"), C(2, "func_breakable_surf"), C(3, "func_breakable", "vent"), C(4, "func_breakable", "crate") },
            new Dictionary<string, CleanupKind>(), All, new FixedRandom());
        Assert.Equal(new[] { (1, CleanupAction.Open), (2, CleanupAction.Break), (3, CleanupAction.Break) }, plan.Select(t => (t.Handle, t.Action)));
    }

    [Fact]
    public void DisabledCategories_AreSkipped_AndZeroChanceOpensNoDoor()
    {
        var settings = All with { BreakVents = false, DoorOpenChancePercent = 0 };
        var plan = CleanupPlan.Build(new[] { C(1, "func_door"), C(2, "func_breakable_surf"), C(3, "func_breakable", "vent") },
            new Dictionary<string, CleanupKind>(), settings, new FixedRandom());
        Assert.Equal(new[] { 2 }, plan.Select(t => t.Handle));
    }

    [Fact]
    public void Overrides_AreApplied()
    {
        var plan = CleanupPlan.Build(new[] { C(1, "func_door"), C(2, "func_breakable", "crate") },
            new Dictionary<string, CleanupKind> { ["k1"] = CleanupKind.Ignore, ["k2"] = CleanupKind.Window }, All, new FixedRandom());
        Assert.Equal(new[] { (2, CleanupAction.Break) }, plan.Select(t => (t.Handle, t.Action)));
    }

    [Fact]
    public void MaxEntities_CapsThePass()
    {
        var plan = CleanupPlan.Build(Enumerable.Range(1, 10).Select(i => C(i, "func_breakable_surf")).ToList(),
            new Dictionary<string, CleanupKind>(), All with { MaxEntities = 3 }, new FixedRandom());
        Assert.Equal(3, plan.Count);
    }

    [Fact]
    public void NoCandidates_EmptyPlan()
    {
        Assert.Empty(CleanupPlan.Build(Array.Empty<CleanupCandidate>(), new Dictionary<string, CleanupKind>(), All, new FixedRandom()));
    }

    [Theory]
    [InlineData(0, 0.0, false)]
    [InlineData(100, 0.999, true)]
    [InlineData(50, 0.49, true)]
    [InlineData(50, 0.5, false)]
    public void DoorRoll_UsesTheChance(int chance, double draw, bool expected)
    {
        Assert.Equal(expected, DoorRoll.ShouldOpen(chance, new FixedRandom { DoubleValue = draw }));
    }
}
