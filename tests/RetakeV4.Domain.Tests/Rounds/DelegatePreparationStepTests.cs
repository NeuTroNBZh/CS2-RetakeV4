using RetakeV4.Domain.Common;
using RetakeV4.Domain.Rounds;

namespace RetakeV4.Domain.Tests.Rounds;

public class DelegatePreparationStepTests
{
    [Fact]
    public void Execute_DelegatesAndExposesNameAndOrder()
    {
        var step = new DelegatePreparationStep("site", PreparationOrder.Site, c => c with { Site = BombSite.B });
        Assert.Equal("site", step.Name);
        Assert.Equal(20, step.Order);
        Assert.Equal(BombSite.B, step.Execute(new PreparationContext(1)).Site);
    }

    [Fact]
    public void Orders_AreStrictlyIncreasingInPipelineOrder()
    {
        var orders = new[] { PreparationOrder.RoundType, PreparationOrder.Site, PreparationOrder.Teams, PreparationOrder.Placement, PreparationOrder.Loadout };
        Assert.Equal(orders.Order(), orders);
        Assert.Equal(orders.Length, orders.Distinct().Count());
    }

    [Fact]
    public void Constructor_RejectsBlankName()
    {
        Assert.Throws<ArgumentException>(() => new DelegatePreparationStep(" ", 1, c => c));
    }
}
