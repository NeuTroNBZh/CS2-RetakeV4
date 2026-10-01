namespace RetakeV4.Domain.Hud;

public sealed record AimMenuLayout
{
    public AimMenuLayout(int LineCount, float DistanceUnits, float LineHeightUnits, float FirstLineUpUnits, float HalfWidthUnits)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(LineCount, 1);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(DistanceUnits);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(LineHeightUnits);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(HalfWidthUnits);
        this.LineCount = LineCount;
        this.DistanceUnits = DistanceUnits;
        this.LineHeightUnits = LineHeightUnits;
        this.FirstLineUpUnits = FirstLineUpUnits;
        this.HalfWidthUnits = HalfWidthUnits;
    }

    public int LineCount { get; }
    public float DistanceUnits { get; }
    public float LineHeightUnits { get; }
    public float FirstLineUpUnits { get; }
    public float HalfWidthUnits { get; }
}
