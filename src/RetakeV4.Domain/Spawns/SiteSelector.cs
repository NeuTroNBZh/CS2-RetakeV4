using RetakeV4.Domain.Common;

namespace RetakeV4.Domain.Spawns;

public enum ForceSiteMode
{
    Once,
    Sticky,
}

public sealed record SiteForce(BombSite Site, ForceSiteMode Mode);

public sealed record SiteHistory(BombSite? Last, int Streak)
{
    public static SiteHistory Empty { get; } = new(null, 0);
}

public sealed record SiteDecision(BombSite Site, SiteHistory History, SiteForce? Force);

public static class SiteSelector
{
    private static readonly BombSite[] BothSites = { BombSite.A, BombSite.B };

    public static SiteDecision Choose(SiteHistory history, SiteForce? force, int maxSameSiteInRow, IReadOnlyCollection<BombSite> available, IRandom random)
    {
        if (force is not null)
        {
            return new SiteDecision(force.Site, Next(history, force.Site), force.Mode == ForceSiteMode.Sticky ? force : null);
        }
        var candidates = available.Count > 0 ? available.Distinct().ToList() : BothSites.ToList();
        if (maxSameSiteInRow > 0 && history.Last is { } last && history.Streak >= maxSameSiteInRow && candidates.Count > 1)
        {
            candidates.Remove(last);
        }
        var site = random.Pick(candidates);
        return new SiteDecision(site, Next(history, site), null);
    }

    private static SiteHistory Next(SiteHistory history, BombSite site) =>
        history.Last == site ? history with { Streak = history.Streak + 1 } : new SiteHistory(site, 1);
}
