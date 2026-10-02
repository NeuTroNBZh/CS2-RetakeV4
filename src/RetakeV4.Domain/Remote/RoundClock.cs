namespace RetakeV4.Domain.Remote;

public static class RoundClock
{
    // Only a live round has a meaningful countdown; the engine's round start includes the freeze time otherwise.
    // With a planted bomb (always the case in a retake) the round ends on the explosion, not on the round timer.
    public static int? TimeLeft(string phase, float roundStart, int roundTime, float now, float? bombBlow = null)
    {
        if (phase != "Live")
        {
            return null;
        }
        var end = bombBlow ?? roundStart + roundTime;
        return Math.Max(0, (int)Math.Ceiling(end - now));
    }

    public static string Bomb(bool planted, bool defused) => defused ? "defused" : planted ? "planted" : "none";
}
