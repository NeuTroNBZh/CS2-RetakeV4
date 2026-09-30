using RetakeV4.Domain.Geometry;

namespace RetakeV4.Domain.Hud;

public static class AimMenuGeometry
{
    public static Vec3 LinePosition(AimMenuLayout layout, Vec3 eye, ViewAngles opened, int lineIndex)
    {
        var up = layout.FirstLineUpUnits - lineIndex * layout.LineHeightUnits;
        return ViewGeometry.Offset(eye, opened, layout.DistanceUnits, 0f, up);
    }
}
