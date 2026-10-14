using RetakeV4.Domain.Geometry;

namespace RetakeV4.Domain.Tests.Geometry;

public class SegmentsTests
{
    [Fact]
    public void APointOnTheSegment_IsAtZero() =>
        Assert.Equal(0f, Segments.DistanceToSegment(new Vec3(5, 0, 0), new Vec3(0, 0, 0), new Vec3(10, 0, 0)));

    [Fact]
    public void BesideTheSegment_IsTheDistanceToTheLine() =>
        Assert.Equal(72f, Segments.DistanceToSegment(new Vec3(0, 72, 0), new Vec3(-300, 0, 0), new Vec3(300, 0, 0)));

    [Fact]
    public void BeyondAnEnd_IsTheDistanceToThatEnd() =>
        Assert.Equal(5f, Segments.DistanceToSegment(new Vec3(13, 4, 0), new Vec3(0, 0, 0), new Vec3(10, 0, 0)));

    [Fact]
    public void ADegenerateSegment_IsAPoint() =>
        Assert.Equal(5f, Segments.DistanceToSegment(new Vec3(3, 4, 0), new Vec3(0, 0, 0), new Vec3(0, 0, 0)));
}
