namespace RetakeV4.Domain.Geometry;

public static class AngleMath
{
    public static float NormalizeDegrees(float degrees)
    {
        var wrapped = degrees % 360f;
        if (wrapped > 180f)
        {
            wrapped -= 360f;
        }
        if (wrapped <= -180f)
        {
            wrapped += 360f;
        }
        return wrapped;
    }

    public static float ToRadians(float degrees) => degrees * MathF.PI / 180f;

    public static float ToDegrees(float radians) => radians * 180f / MathF.PI;
}
