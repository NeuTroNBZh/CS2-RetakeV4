using RetakeV4.Domain.MapCleanup;
using RetakeV4.Modules.MapCleanup;

namespace RetakeV4.Integration.Tests.Modules.MapCleanup;

public sealed class CleanupOverrideStoreTests : IDisposable
{
    private readonly TempDirectory _dir = new();

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void MissingFileOrDirectory_NoOverride()
    {
        var (overrides, issues) = new CleanupOverrideStore(_dir.File("mapcleanup")).Load("de_dust2");
        Assert.Empty(overrides);
        Assert.Empty(issues);
    }

    [Fact]
    public void SaveThenLoad_AndPreviousVersionKept()
    {
        var store = new CleanupOverrideStore(_dir.File("mapcleanup"));
        store.Save("de_dust2", new[] { new CleanupOverride("name:a", CleanupKind.Ignore, "") });
        store.Save("de_dust2", new[] { new CleanupOverride("name:b", CleanupKind.Vent, "") });
        Assert.Equal(new[] { "name:b" }, store.Load("de_dust2").Overrides.Select(o => o.Key));
        Assert.Contains("name:a", File.ReadAllText(Path.Combine(_dir.Path, "mapcleanup", "de_dust2.json.bak")));
        Assert.Empty(Directory.GetFiles(Path.Combine(_dir.Path, "mapcleanup"), "*.tmp"));
    }

    [Fact]
    public void BadEntry_ReportedOthersKept()
    {
        File.WriteAllText(_dir.File("de_dust2.json"), "[{\"Key\":\"a\",\"Kind\":\"Door\"},{\"Key\":\"b\",\"Kind\":\"Banana\"}]");
        var (overrides, issues) = new CleanupOverrideStore(_dir.Path).Load("de_dust2");
        Assert.Equal(new[] { "a" }, overrides.Select(o => o.Key));
        Assert.Single(issues);
    }

    [Fact]
    public void CorruptFile_ReportedAndKeptAside()
    {
        File.WriteAllText(_dir.File("de_dust2.json"), "{ broken");
        var (overrides, issues) = new CleanupOverrideStore(_dir.Path).Load("de_dust2");
        Assert.Empty(overrides);
        Assert.NotEmpty(issues);
        Assert.Single(Directory.GetFiles(_dir.Path, "de_dust2.json.*.invalid.bak"));
        Assert.True(File.Exists(_dir.File("de_dust2.json")));
    }

    [Theory]
    [InlineData("../evil")]
    [InlineData("")]
    public void UnsafeMapName_IsRefused(string map)
    {
        var store = new CleanupOverrideStore(_dir.Path);
        Assert.Throws<ArgumentException>(() => store.Load(map));
        Assert.Throws<ArgumentException>(() => store.Save(map, Array.Empty<CleanupOverride>()));
    }
}
