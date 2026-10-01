using RetakeV4.Modules;

namespace RetakeV4.Integration.Tests.Modules;

public class ModuleCatalogTests
{
    [Fact]
    public void Catalog_ListsEveryModuleOnce()
    {
        var names = ModuleCatalog.CreateAll().Select(m => m.Name).ToList();
        Assert.Equal(
            new[] { "Core", "Hud", "RoundTypes", "Teams", "Spawns", "Allocation", "Plant", "InstaDefuse", "Admin", "Links", "Announcements", "Api" },
            names);
    }

    [Fact]
    public void Catalog_CreatesFreshInstances() =>
        Assert.NotSame(ModuleCatalog.CreateAll()[0], ModuleCatalog.CreateAll()[0]);
}
