using RetakeV4.Modules.MapCleanup;

namespace RetakeV4.Integration.Tests.Modules.MapCleanup;

public class MapCleanupConfigValidatorTests
{
    private static readonly MapCleanupConfig Defaults = new();
    private readonly MapCleanupConfigValidator _validator = new();

    [Fact]
    public void Defaults_AreValid()
    {
        var result = _validator.Validate(Defaults, Defaults, "mapcleanup.json");
        Assert.Empty(result.Issues);
        Assert.Equal(100, result.Config.DoorOpenChancePercent);
        Assert.Equal(512, result.Config.MaxEntitiesPerRound);
        Assert.True(result.Config.FreezeEndCheck);
    }

    [Theory]
    [InlineData(-1, 512, 1)]
    [InlineData(101, 512, 1)]
    [InlineData(50, 0, 1)]
    [InlineData(50, 5000, 1)]
    [InlineData(-5, 0, 2)]
    [InlineData(0, 1, 0)]
    [InlineData(100, 4096, 0)]
    public void OutOfRange_ReplacedByDefault(int chance, int max, int issues)
    {
        var result = _validator.Validate(Defaults with { DoorOpenChancePercent = chance, MaxEntitiesPerRound = max }, Defaults, "mapcleanup.json");
        Assert.Equal(issues, result.Issues.Count);
        Assert.InRange(result.Config.DoorOpenChancePercent, 0, 100);
        Assert.InRange(result.Config.MaxEntitiesPerRound, 1, 4096);
    }

    [Fact]
    public void ToSettings_CopiesTheValues()
    {
        var settings = (Defaults with { BreakVents = false, DoorOpenChancePercent = 30 }).ToSettings();
        Assert.Equal(new RetakeV4.Domain.MapCleanup.CleanupSettings(true, 30, true, false, 512), settings);
    }
}
