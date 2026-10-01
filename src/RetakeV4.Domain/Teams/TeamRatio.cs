namespace RetakeV4.Domain.Teams;

public static class TeamRatio
{
    public static (int Ct, int T) Compute(int total, double tRatio)
    {
        if (total <= 0)
        {
            return (0, 0);
        }
        if (total == 1)
        {
            return (0, 1);
        }
        var t = (int)Math.Round(total * tRatio, MidpointRounding.AwayFromZero);
        t = Math.Clamp(t, 1, total - 1);
        return (total - t, t);
    }
}
