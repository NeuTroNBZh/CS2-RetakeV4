using RetakeV4.Domain.Common;
using RetakeV4.Domain.Spawns;
using RetakeV4.Domain.Tests.TestDoubles;

namespace RetakeV4.Domain.Tests.Spawns;

public class SiteSelectorTests
{
    private static readonly BombSite[] Both = { BombSite.A, BombSite.B };

    [Fact]
    public void Random_UsesRandomIndexAmongAvailable()
    {
        var decision = SiteSelector.Choose(SiteHistory.Empty, null, 0, Both, new FixedRandom(1));
        Assert.Equal(BombSite.B, decision.Site);
        Assert.Equal(new SiteHistory(BombSite.B, 1), decision.History);
    }

    [Fact]
    public void OnlyOneSiteAvailable_IsAlwaysChosen()
    {
        var decision = SiteSelector.Choose(SiteHistory.Empty, null, 0, new[] { BombSite.B }, new FixedRandom(0));
        Assert.Equal(BombSite.B, decision.Site);
    }

    [Fact]
    public void NoSiteAvailable_FallsBackToBoth()
    {
        var decision = SiteSelector.Choose(SiteHistory.Empty, null, 0, Array.Empty<BombSite>(), new FixedRandom(1));
        Assert.Equal(BombSite.B, decision.Site);
    }

    [Fact]
    public void SameSite_IncrementsStreak()
    {
        var decision = SiteSelector.Choose(new SiteHistory(BombSite.A, 2), null, 0, Both, new FixedRandom(0));
        Assert.Equal(new SiteHistory(BombSite.A, 3), decision.History);
    }

    [Fact]
    public void MaxSameSiteInRow_ForcesTheOtherSite()
    {
        var decision = SiteSelector.Choose(new SiteHistory(BombSite.A, 2), null, 2, Both, new FixedRandom(0));
        Assert.Equal(BombSite.B, decision.Site);
        Assert.Equal(new SiteHistory(BombSite.B, 1), decision.History);
    }

    [Fact]
    public void MaxSameSiteInRow_IsIgnoredWhenOtherSiteUnavailable()
    {
        var decision = SiteSelector.Choose(new SiteHistory(BombSite.A, 5), null, 2, new[] { BombSite.A }, new FixedRandom(0));
        Assert.Equal(BombSite.A, decision.Site);
    }

    [Fact]
    public void ForceOnce_IsConsumed()
    {
        var decision = SiteSelector.Choose(SiteHistory.Empty, new SiteForce(BombSite.B, ForceSiteMode.Once), 0, Both, new FixedRandom(0));
        Assert.Equal(BombSite.B, decision.Site);
        Assert.Null(decision.Force);
    }

    [Fact]
    public void ForceSticky_IsKept_AndBeatsMaxInRow()
    {
        var force = new SiteForce(BombSite.A, ForceSiteMode.Sticky);
        var decision = SiteSelector.Choose(new SiteHistory(BombSite.A, 9), force, 2, Both, new FixedRandom(1));
        Assert.Equal(BombSite.A, decision.Site);
        Assert.Equal(force, decision.Force);
    }
}
