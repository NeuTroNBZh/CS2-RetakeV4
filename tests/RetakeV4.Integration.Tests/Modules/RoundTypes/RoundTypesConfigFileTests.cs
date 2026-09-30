using RetakeV4.Configuration;
using RetakeV4.Modules.RoundTypes;

namespace RetakeV4.Integration.Tests.Modules.RoundTypes;

public sealed class RoundTypesConfigFileTests : IDisposable
{
    private readonly TempDirectory _dir = new();

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void DefaultFile_RoundTripsWithoutIssues()
    {
        var store = new JsonConfigStore(_dir.Path);
        store.Load("roundtypes.json", new RoundTypesConfig(), new RoundTypesConfigValidator());
        var reloaded = store.Load("roundtypes.json", new RoundTypesConfig(), new RoundTypesConfigValidator());
        Assert.False(reloaded.CreatedDefault);
        Assert.Empty(reloaded.Issues);
        var definitions = reloaded.Config.ToDefinitions();
        Assert.Equal(new RoundTypesConfig().ToDefinitions()["FullBuy"].DefaultCT, definitions["FullBuy"].DefaultCT);
        Assert.Equal(34.44444, definitions["Pistol"].DefuseKit.Chance);
    }
}
