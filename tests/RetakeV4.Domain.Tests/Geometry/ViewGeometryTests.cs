using RetakeV4.Domain.Geometry;

namespace RetakeV4.Domain.Tests.Geometry;

public class ViewGeometryTests
{
    private static void AssertVec(Vec3 expected, Vec3 actual)
    {
        Assert.Equal(expected.X, actual.X, 3);
        Assert.Equal(expected.Y, actual.Y, 3);
        Assert.Equal(expected.Z, actual.Z, 3);
    }

    [Fact]
    public void Forward_Yaw0_PointsAlongPositiveX() =>
        AssertVec(new Vec3(1, 0, 0), ViewGeometry.Forward(new ViewAngles(0, 0)));

    [Fact]
    public void Forward_Yaw90_PointsAlongPositiveY() =>
        AssertVec(new Vec3(0, 1, 0), ViewGeometry.Forward(new ViewAngles(0, 90)));

    [Fact]
    public void Forward_PositivePitch_LooksDown() =>
        AssertVec(new Vec3(0, 0, -1), ViewGeometry.Forward(new ViewAngles(90, 0)));

    [Fact]
    public void Forward_NegativePitch_LooksUp() =>
        AssertVec(new Vec3(0, 0, 1), ViewGeometry.Forward(new ViewAngles(-90, 0)));

    [Fact]
    public void Right_Yaw0_PointsAlongNegativeY() =>
        AssertVec(new Vec3(0, -1, 0), ViewGeometry.Right(new ViewAngles(0, 0)));

    [Fact]
    public void Up_Pitch0_PointsAlongPositiveZ() =>
        AssertVec(new Vec3(0, 0, 1), ViewGeometry.Up(new ViewAngles(0, 45)));

    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(30f, 45f)]
    [InlineData(-60f, 170f)]
    [InlineData(85f, -120f)]
    public void Basis_IsOrthonormal(float pitch, float yaw)
    {
        var angles = new ViewAngles(pitch, yaw);
        var f = ViewGeometry.Forward(angles);
        var r = ViewGeometry.Right(angles);
        var u = ViewGeometry.Up(angles);
        Assert.Equal(0f, Vec3.Dot(f, r), 3);
        Assert.Equal(0f, Vec3.Dot(f, u), 3);
        Assert.Equal(0f, Vec3.Dot(r, u), 3);
        Assert.Equal(1f, f.Length, 3);
        Assert.Equal(1f, u.Length, 3);
    }

    [Fact]
    public void Offset_CombinesForwardRightUp()
    {
        var result = ViewGeometry.Offset(new Vec3(10, 20, 30), new ViewAngles(0, 0), forward: 5, right: 2, up: 3);
        AssertVec(new Vec3(15, 18, 33), result);
    }

    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(25f, 60f)]
    [InlineData(-40f, -150f)]
    public void AnglesOf_InvertsForward(float pitch, float yaw)
    {
        var angles = ViewGeometry.AnglesOf(ViewGeometry.Forward(new ViewAngles(pitch, yaw)) * 42f);
        Assert.Equal(pitch, angles.Pitch, 2);
        Assert.Equal(yaw, angles.Yaw, 2);
    }

    [Fact]
    public void AnglesOf_ZeroVector_IsZeroAngles() =>
        Assert.Equal(new ViewAngles(0f, 0f), ViewGeometry.AnglesOf(new Vec3(0, 0, 0)));

    [Fact]
    public void VectorOperators_Work()
    {
        AssertVec(new Vec3(1, 2, 3), new Vec3(4, 6, 8) - new Vec3(3, 4, 5));
    }
}
