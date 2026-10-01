using RetakeV4.Domain.Geometry;

namespace RetakeV4.Domain.Tests.Geometry;

public class AngleMathTests
{
    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(190f, -170f)]
    [InlineData(-190f, 170f)]
    [InlineData(180f, 180f)]
    [InlineData(-180f, 180f)]
    [InlineData(540f, 180f)]
    [InlineData(725f, 5f)]
    public void NormalizeDegrees_WrapsIntoMinus180Exclusive180Inclusive(float input, float expected)
    {
        Assert.Equal(expected, AngleMath.NormalizeDegrees(input), 3);
    }

    [Fact]
    public void RadiansRoundTrip()
    {
        Assert.Equal(37.5f, AngleMath.ToDegrees(AngleMath.ToRadians(37.5f)), 3);
    }
}
