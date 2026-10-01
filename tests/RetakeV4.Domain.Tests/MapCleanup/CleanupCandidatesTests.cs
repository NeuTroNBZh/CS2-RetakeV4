using RetakeV4.Domain.Geometry;
using RetakeV4.Domain.MapCleanup;

namespace RetakeV4.Domain.Tests.MapCleanup;

public class CleanupCandidatesTests
{
    private static (int, EntityFacts) E(int index, string cls, string? name = null) => (index, new EntityFacts(cls, null, name, new Vec3(index, 0, 0)));

    // CounterStrikeSharp matches designer names by substring: "func_door" also returns every func_door_rotating.
    [Fact]
    public void SameEntityReturnedByTwoQueries_IsKeptOnce()
    {
        var found = new[] { E(5, "func_door_rotating"), E(5, "func_door_rotating"), E(7, "func_shatterglass"), E(7, "func_shatterglass") };
        Assert.Equal(new[] { 5, 7 }, CleanupCandidates.Build(found).Select(c => c.Handle));
    }

    [Fact]
    public void NonCandidateClasses_AreDropped()
    {
        var found = new[] { E(1, "func_door"), E(2, "func_doorway_custom"), E(3, "prop_physics") };
        Assert.Equal(new[] { 1 }, CleanupCandidates.Build(found).Select(c => c.Handle));
    }

    [Fact]
    public void NameKey_OnlyWhenUnique_AfterDeduplication()
    {
        var found = new[] { E(1, "func_door", "mid"), E(1, "func_door", "mid"), E(2, "func_door", "ab"), E(3, "func_door", "ab") };
        var keys = CleanupCandidates.Build(found).ToDictionary(c => c.Handle, c => c.Key);
        Assert.Equal("name:mid", keys[1]);
        Assert.StartsWith("pos:", keys[2]);
        Assert.StartsWith("pos:", keys[3]);
    }
}
