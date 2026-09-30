namespace RetakeV4.Domain.Common;

public static class Chance
{
    public static bool Roll(double percent, IRandom random)
    {
        if (percent <= 0d)
        {
            return false;
        }
        if (percent >= 100d)
        {
            return true;
        }
        return random.NextDouble() * 100d <= percent;
    }
}
