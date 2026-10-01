using RetakeV4.Domain.Geometry;
using RetakeV4.Domain.Hud;

namespace RetakeV4.Domain.Tests.Hud;

public class AimLineResolverTests
{
    private static readonly AimMenuLayout Layout = new(
        LineCount: 6, DistanceUnits: 50f, LineHeightUnits: 4f, FirstLineUpUnits: 10f, HalfWidthUnits: 15f);

    private static float PitchForUp(float up) => -AngleMath.ToDegrees(MathF.Atan(up / Layout.DistanceUnits));

    private static float YawForSideways(float units) => AngleMath.ToDegrees(MathF.Atan(units / Layout.DistanceUnits));

    [Fact]
    public void AimingAtFirstLine_ReturnsZero()
    {
        var current = new ViewAngles(PitchForUp(10f), 0f);
        Assert.Equal(0, AimLineResolver.Resolve(Layout, new ViewAngles(0f, 0f), current));
    }

    [Fact]
    public void AimingAtFourthLine_ReturnsThree()
    {
        var current = new ViewAngles(PitchForUp(10f - 3 * 4f), 0f);
        Assert.Equal(3, AimLineResolver.Resolve(Layout, new ViewAngles(0f, 0f), current));
    }

    [Theory]
    [InlineData(12f)]
    [InlineData(20f)]
    public void AimingAboveMenu_ReturnsNull(float up)
    {
        Assert.Null(AimLineResolver.Resolve(Layout, new ViewAngles(0f, 0f), new ViewAngles(PitchForUp(up), 0f)));
    }

    [Fact]
    public void AimingBelowLastLine_ReturnsNull()
    {
        var current = new ViewAngles(PitchForUp(10f - 6 * 4f), 0f);
        Assert.Null(AimLineResolver.Resolve(Layout, new ViewAngles(0f, 0f), current));
    }

    [Fact]
    public void AimingTooFarSideways_ReturnsNull()
    {
        var current = new ViewAngles(PitchForUp(10f), YawForSideways(20f));
        Assert.Null(AimLineResolver.Resolve(Layout, new ViewAngles(0f, 0f), current));
    }

    [Fact]
    public void AimingSlightlySideways_StillSelects()
    {
        var current = new ViewAngles(PitchForUp(10f), YawForSideways(10f));
        Assert.Equal(0, AimLineResolver.Resolve(Layout, new ViewAngles(0f, 0f), current));
    }

    [Fact]
    public void YawWrapAround_IsHandled()
    {
        var opened = new ViewAngles(0f, 179f);
        var current = new ViewAngles(PitchForUp(10f), -179f);
        Assert.Equal(0, AimLineResolver.Resolve(Layout, opened, current));
    }

    [Fact]
    public void ExtremePitchDelta_ReturnsNull()
    {
        Assert.Null(AimLineResolver.Resolve(Layout, new ViewAngles(0f, 0f), new ViewAngles(85f, 0f)));
    }

    [Theory]
    [InlineData(20f, 45f, 2)]
    [InlineData(-35f, -120f, 5)]
    [InlineData(0f, 90f, 0)]
    public void AimingAtRenderedLinePosition_SelectsThatLine(float openPitch, float openYaw, int line)
    {
        var eye = new Vec3(100f, -50f, 64f);
        var opened = new ViewAngles(openPitch, openYaw);
        var target = AimMenuGeometry.LinePosition(Layout, eye, opened, line);
        var current = ViewGeometry.AnglesOf(target - eye);
        Assert.Equal(line, AimLineResolver.Resolve(Layout, opened, current));
    }

    [Fact]
    public void LinePosition_StacksLinesDownward()
    {
        var eye = new Vec3(0f, 0f, 0f);
        var opened = new ViewAngles(0f, 0f);
        var line0 = AimMenuGeometry.LinePosition(Layout, eye, opened, 0);
        var line1 = AimMenuGeometry.LinePosition(Layout, eye, opened, 1);
        Assert.Equal(50f, line0.X, 3);
        Assert.Equal(10f, line0.Z, 3);
        Assert.Equal(6f, line1.Z, 3);
    }

    [Theory]
    [InlineData(0, 50f, 4f, 15f)]
    [InlineData(3, 0f, 4f, 15f)]
    [InlineData(3, 50f, -1f, 15f)]
    [InlineData(3, 50f, 4f, 0f)]
    public void Layout_RejectsInvalidValues(int lines, float distance, float lineHeight, float halfWidth)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AimMenuLayout(lines, distance, lineHeight, 0f, halfWidth));
    }
}
