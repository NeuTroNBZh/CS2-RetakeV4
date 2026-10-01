using RetakeV4.Domain.Common;

namespace RetakeV4.Domain.Loadouts;

// Knives are never part of a loadout: the game (or a skin plugin) owns them. Every model name contains "knife" except the bayonet.
public static class Knives
{
    public static bool IsKnife(string designerName) =>
        designerName.Contains("knife", StringComparison.Ordinal) || designerName.Contains("bayonet", StringComparison.Ordinal);

    public static bool HasKnife(IEnumerable<string> designerNames) => designerNames.Any(IsKnife);

    public static string DefaultFor(TeamSide team) => team == TeamSide.T ? "weapon_knife_t" : "weapon_knife";
}
