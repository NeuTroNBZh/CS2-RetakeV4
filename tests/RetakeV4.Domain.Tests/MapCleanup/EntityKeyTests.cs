using RetakeV4.Domain.Geometry;
using RetakeV4.Domain.MapCleanup;

namespace RetakeV4.Domain.Tests.MapCleanup;

public class EntityKeyTests
{
    [Fact]
    public void UniqueName_IsUsed()
    {
        Assert.Equal("name:mid_door", EntityKey.For(new EntityFacts("func_door", null, "mid_door", new Vec3(1, 2, 3)), nameIsUnique: true));
    }

    [Fact]
    public void NoNameOrSharedName_FallsBackToClassAndRoundedPosition()
    {
        var facts = new EntityFacts("func_breakable", null, "window", new Vec3(10.4f, -3.6f, 64.5f));
        Assert.Equal("pos:func_breakable@10,-4,65", EntityKey.For(facts, nameIsUnique: false));
        Assert.Equal("pos:func_breakable@10,-4,65", EntityKey.For(facts with { TargetName = null }, nameIsUnique: true));
    }

    [Fact]
    public void NegativeZero_IsWrittenAsZero()
    {
        Assert.Equal("pos:func_door@0,0,0", EntityKey.For(new EntityFacts("func_door", null, null, new Vec3(-0.2f, 0, 0)), false));
    }

    // Two unnamed entities of the same class at the same rounded spot share a key: a correction applies to both.
    [Fact]
    public void SameRoundedSpot_SharesTheKey()
    {
        var a = new EntityFacts("func_breakable", null, null, new Vec3(10.2f, 0, 0));
        var b = new EntityFacts("func_breakable", null, null, new Vec3(9.8f, 0, 0));
        Assert.Equal(EntityKey.For(a, false), EntityKey.For(b, false));
    }
}
