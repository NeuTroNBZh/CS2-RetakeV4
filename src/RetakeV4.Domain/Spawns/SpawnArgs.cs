using RetakeV4.Domain.Common;

namespace RetakeV4.Domain.Spawns;

public static class SpawnArgs
{
    // V3 numeric forms (team 2/3, site 0/1) are still accepted.
    public static TeamSide? Team(string? value) => value?.Trim().ToUpperInvariant() switch
    {
        "T" or "2" => TeamSide.T,
        "CT" or "3" => TeamSide.CT,
        _ => null,
    };

    public static BombSite? Site(string? value) => value?.Trim().ToUpperInvariant() switch
    {
        "A" or "0" => BombSite.A,
        "B" or "1" => BombSite.B,
        _ => null,
    };

    public static bool IsPlantFlag(string? value) => value?.Trim().ToLowerInvariant() is "plant" or "c4" or "1" or "true";
}

// Force = null: cancel the current force.
public sealed record ForceSiteRequest(SiteForce? Force);

public static class ForceSiteCommand
{
    public static ForceSiteRequest? Parse(string? site, string? mode)
    {
        if (site?.Trim().ToLowerInvariant() is "off" or "none" or "clear")
        {
            return new ForceSiteRequest(null);
        }
        var parsedMode = mode?.Trim().ToLowerInvariant() switch
        {
            null or "" or "once" => ForceSiteMode.Once,
            "sticky" => ForceSiteMode.Sticky,
            _ => (ForceSiteMode?)null,
        };
        return SpawnArgs.Site(site) is { } parsed && parsedMode is { } forceMode
            ? new ForceSiteRequest(new SiteForce(parsed, forceMode))
            : null;
    }
}
