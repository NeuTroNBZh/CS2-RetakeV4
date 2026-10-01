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
