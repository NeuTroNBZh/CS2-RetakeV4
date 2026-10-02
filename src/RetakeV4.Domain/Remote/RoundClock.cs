namespace RetakeV4.Domain.Remote;

public static class RoundClock
{
    // Only a live round has a meaningful countdown; the engine's round start includes the freeze time otherwise.
    public static int? TimeLeft(string phase, float roundStart, int roundTime, float now) =>
        phase == "Live" ? Math.Max(0, (int)Math.Ceiling(roundStart + roundTime - now)) : null;

    public static string Bomb(bool planted, bool defused) => defused ? "defused" : planted ? "planted" : "none";
}
