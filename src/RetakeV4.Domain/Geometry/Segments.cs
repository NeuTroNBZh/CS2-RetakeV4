namespace RetakeV4.Domain.Geometry;

public static class Segments
{
    // Shortest distance from point to the segment [a, b].
    public static float DistanceToSegment(Vec3 point, Vec3 a, Vec3 b)
    {
        var ab = b - a;
        var lengthSquared = Vec3.Dot(ab, ab);
        if (lengthSquared <= float.Epsilon)
        {
            return (point - a).Length;
        }
        var t = Math.Clamp(Vec3.Dot(point - a, ab) / lengthSquared, 0f, 1f);
        return (point - (a + (ab * t))).Length;
    }
}
