using RetakeV4.Domain.Geometry;
using RetakeV4.Domain.MapCleanup;

namespace RetakeV4.Domain.Tests.MapCleanup;

public class AimPickerTests
{
    private static readonly Vec3 Eye = new(0, 0, 0);
    private static readonly Vec3 Forward = new(1, 0, 0);

    [Fact]
    public void PicksTheEntityClosestToTheViewAxis()
    {
        var pick = AimPicker.Pick(Eye, Forward, new[] { (1, new Vec3(500, 80, 0)), (2, new Vec3(800, 10, 0)) });
        Assert.Equal(2, pick);
    }

    [Fact]
    public void OutsideTheConeOrTooFarOrBehind_IsNotPicked()
    {
        Assert.Null(AimPicker.Pick(Eye, Forward, new[] { (1, new Vec3(100, 100, 0)), (2, new Vec3(2000, 0, 0)), (3, new Vec3(-300, 0, 0)) }));
    }

    [Fact]
    public void NoCandidates_NothingPicked()
    {
        Assert.Null(AimPicker.Pick(Eye, Forward, Array.Empty<(int, Vec3)>()));
    }
}

public class AimPickerRankedTests
{
    [Fact]
    public void Ranked_OrdersByAngleAndKeepsOnlyTheCone()
    {
        var ranked = AimPicker.Ranked(new Vec3(0, 0, 0), new Vec3(1, 0, 0),
            new[] { (1, new Vec3(500, 80, 0)), (2, new Vec3(800, 10, 0)), (3, new Vec3(-50, 0, 0)), (4, new Vec3(300, 300, 0)) }, 30f, 1500f, 5);
        Assert.Equal(new[] { 2, 1 }, ranked);
    }

    [Fact]
    public void Ranked_TakesAtMostTheLimit()
    {
        var many = Enumerable.Range(1, 10).Select(i => (i, new Vec3(100 * i, 0, 0))).ToList();
        Assert.Equal(3, AimPicker.Ranked(new Vec3(0, 0, 0), new Vec3(1, 0, 0), many, 12f, 1500f, 3).Count);
    }
}

public class AimPickerNearestTests
{
    [Fact]
    public void Nearest_IgnoresDirection_AndRespectsTheRange()
    {
        var candidates = new[] { (1, new Vec3(-300, 0, 0)), (2, new Vec3(0, 200, 0)), (3, new Vec3(900, 0, 0)) };
        Assert.Equal(2, AimPicker.Nearest(new Vec3(0, 0, 0), candidates, 600f));
        Assert.Null(AimPicker.Nearest(new Vec3(0, 0, 0), candidates, 100f));
    }
}
