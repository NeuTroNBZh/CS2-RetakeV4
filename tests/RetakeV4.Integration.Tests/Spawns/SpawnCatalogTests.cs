using Microsoft.Extensions.Logging;
using RetakeV4.Domain.Common;
using RetakeV4.Domain.Geometry;
using RetakeV4.Domain.Spawns;
using RetakeV4.Modules.Spawns;

namespace RetakeV4.Integration.Tests.Spawns;

public sealed class SpawnCatalogTests : IDisposable
{
    private readonly TempDirectory _dir = new();
    private readonly ListLogger _logger = new();

    public void Dispose() => _dir.Dispose();

    private SpawnCatalog Catalog() => new(new SpawnFileStore(_dir.Path), _logger);

    private static SpawnPoint Spawn() => new(Guid.NewGuid(), TeamSide.T, BombSite.A, true, new Vec3(1f, 2f, 3f), new ViewAngles(0f, 90f));

    [Fact]
    public void MissingFile_GivesAnEmptySet_AndAWarning()
    {
        var catalog = Catalog();
        catalog.Load("de_mirage");
        Assert.Empty(catalog.Set.Spawns);
        Assert.False(catalog.FileFound);
        Assert.Equal("de_mirage", catalog.MapName);
        Assert.Contains(_logger.Entries, e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public void Save_ThenReload_KeepsTheEdits()
    {
        var catalog = Catalog();
        catalog.Load("de_mirage");
        var spawn = Spawn();
        catalog.Replace(catalog.Set.Add(spawn));
        Assert.True(catalog.Set.Dirty);
        Assert.Null(catalog.Save());
        Assert.False(catalog.Set.Dirty);
        Assert.True(catalog.FileFound);
        catalog.Reload();
        Assert.Equal(spawn, Assert.Single(catalog.Set.Spawns));
    }

    [Fact]
    public void Reload_DiscardsUnsavedEdits()
    {
        var catalog = Catalog();
        catalog.Load("de_mirage");
        catalog.Replace(catalog.Set.Add(Spawn()));
        catalog.Reload();
        Assert.Empty(catalog.Set.Spawns);
        Assert.False(catalog.Set.Dirty);
    }

    [Fact]
    public void Save_WithoutAMap_ReturnsAnError() => Assert.NotNull(Catalog().Save());

    [Fact]
    public void UnsafeMapName_IsNotRead()
    {
        var catalog = Catalog();
        catalog.Load("../secret");
        Assert.Empty(catalog.Set.Spawns);
        Assert.NotNull(catalog.Save());
    }
}
