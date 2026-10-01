using RetakeV4.Domain.Geometry;

namespace RetakeV4.Domain.MapCleanup;

// No engine ray trace without gamedata: the editor targets the candidate closest to the view axis inside a narrow cone.
public static class AimPicker
{
    public static int? Pick(Vec3 eye, Vec3 forward, IReadOnlyList<(int Handle, Vec3 Center)> candidates, float maxDegrees = 12f, float maxDistance = 1500f)
    {
        var axis = forward * (1f / Math.Max(forward.Length, float.Epsilon));
        int? best = null;
        var bestCos = MathF.Cos(maxDegrees * MathF.PI / 180f);
        foreach (var (handle, center) in candidates)
        {
            var to = center - eye;
            var distance = to.Length;
            if (distance <= float.Epsilon || distance > maxDistance)
            {
                continue;
            }
            var cos = Vec3.Dot(to, axis) / distance;
            if (cos >= bestCos)
            {
                bestCos = cos;
                best = handle;
            }
        }
        return best;
    }
}
