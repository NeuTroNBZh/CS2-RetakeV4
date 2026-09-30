namespace RetakeV4.Domain.Geometry;

public readonly record struct Vec3(float X, float Y, float Z)
{
    public static Vec3 operator +(Vec3 a, Vec3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vec3 operator -(Vec3 a, Vec3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vec3 operator *(Vec3 v, float scale) => new(v.X * scale, v.Y * scale, v.Z * scale);

    public float Length => MathF.Sqrt(Dot(this, this));

    public static float Dot(Vec3 a, Vec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
}
