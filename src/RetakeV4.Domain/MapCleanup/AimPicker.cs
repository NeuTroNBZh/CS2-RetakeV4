using RetakeV4.Domain.Geometry;

namespace RetakeV4.Domain.MapCleanup;

// No engine ray trace without gamedata: the editor targets the candidate closest to the view axis inside a narrow cone.
public static class AimPicker
{
    public static IReadOnlyList<int> Ranked(Vec3 eye, Vec3 forward, IReadOnlyList<(int Handle, Vec3 Center)> candidates,
        float maxDegrees, float maxDistance, int take)
    {
        var axis = forward * (1f / Math.Max(forward.Length, float.Epsilon));
        var minCos = MathF.Cos(maxDegrees * MathF.PI / 180f);
        return candidates
            .Select(c => (c.Handle, To: c.Center - eye))
            .Where(c => c.To.Length > float.Epsilon && c.To.Length <= maxDistance)
            .Select(c => (c.Handle, Cos: Vec3.Dot(c.To, axis) / c.To.Length))
            .Where(c => c.Cos >= minCos)
            .OrderByDescending(c => c.Cos)
            .Take(take)
            .Select(c => c.Handle)
            .ToList();
    }

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
