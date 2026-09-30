namespace RetakeV4.Domain.Geometry;

// Source engine convention: positive pitch looks down, yaw 0 = +X, yaw 90 = +Y. Roll is ignored.
public static class ViewGeometry
{
    public static Vec3 Forward(ViewAngles angles)
    {
        var (sp, cp, sy, cy) = Trig(angles);
        return new Vec3(cp * cy, cp * sy, -sp);
    }

    public static Vec3 Right(ViewAngles angles)
    {
        var (_, _, sy, cy) = Trig(angles);
        return new Vec3(sy, -cy, 0f);
    }

    public static Vec3 Up(ViewAngles angles)
    {
        var (sp, cp, sy, cy) = Trig(angles);
        return new Vec3(sp * cy, sp * sy, cp);
    }

    public static Vec3 Offset(Vec3 origin, ViewAngles angles, float forward, float right, float up) =>
        origin + Forward(angles) * forward + Right(angles) * right + Up(angles) * up;

    public static ViewAngles AnglesOf(Vec3 direction)
    {
        var length = direction.Length;
        if (length <= float.Epsilon)
        {
            return new ViewAngles(0f, 0f);
        }
        var pitch = -AngleMath.ToDegrees(MathF.Asin(direction.Z / length));
        var yaw = AngleMath.ToDegrees(MathF.Atan2(direction.Y, direction.X));
        return new ViewAngles(pitch, yaw);
    }

    private static (float Sp, float Cp, float Sy, float Cy) Trig(ViewAngles angles)
    {
        var p = AngleMath.ToRadians(angles.Pitch);
        var y = AngleMath.ToRadians(angles.Yaw);
        return (MathF.Sin(p), MathF.Cos(p), MathF.Sin(y), MathF.Cos(y));
    }
}
