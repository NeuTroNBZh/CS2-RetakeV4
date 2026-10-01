using RetakeV4.Domain.Geometry;

namespace RetakeV4.Domain.MapCleanup;

public sealed record EntityFacts(string ClassName, string? ModelName, string? TargetName, Vec3 Origin, int Health = 0);
