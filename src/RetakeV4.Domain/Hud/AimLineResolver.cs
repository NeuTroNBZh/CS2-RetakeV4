using RetakeV4.Domain.Geometry;

namespace RetakeV4.Domain.Hud;

public static class AimLineResolver
{
    private const float MaxAbsDeltaDegrees = 80f;

    public static int? Resolve(AimMenuLayout layout, ViewAngles opened, ViewAngles current)
    {
        var deltaPitch = AngleMath.NormalizeDegrees(current.Pitch - opened.Pitch);
        var deltaYaw = AngleMath.NormalizeDegrees(current.Yaw - opened.Yaw);
        if (MathF.Abs(deltaPitch) > MaxAbsDeltaDegrees || MathF.Abs(deltaYaw) > MaxAbsDeltaDegrees)
        {
            return null;
        }

        var sideways = MathF.Tan(AngleMath.ToRadians(deltaYaw)) * layout.DistanceUnits;
        if (MathF.Abs(sideways) > layout.HalfWidthUnits)
        {
            return null;
        }

        var aimedUp = -MathF.Tan(AngleMath.ToRadians(deltaPitch)) * layout.DistanceUnits;
        var raw = (layout.FirstLineUpUnits - aimedUp) / layout.LineHeightUnits;
        var index = (int)MathF.Round(raw, MidpointRounding.AwayFromZero);
        return index >= 0 && index < layout.LineCount ? index : null;
    }
}
