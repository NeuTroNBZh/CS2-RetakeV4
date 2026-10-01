using RetakeV4.Domain.Common;
using RetakeV4.Domain.Geometry;
using RetakeV4.Domain.Spawns;
using RetakeV4.Modules.Spawns;

namespace RetakeV4.Integration.Tests.Spawns;

public sealed class SpawnFileStoreTests : IDisposable
{
    private const string LegacyJson = """
        [{"SpawnId":"0b8d5c2e-3f0a-4b1e-9c55-111111111111","Team":2,"BombSite":0,"IsInBombZone":true,
          "PositionX":1,"PositionY":2,"PositionZ":3,"QAngleX":0,"QAngleY":90,"QAngleZ":0}]
        """;

    private readonly TempDirectory _dir = new();

    public void Dispose() => _dir.Dispose();

    private SpawnFileStore Store() => new(_dir.Path);

    private static SpawnPoint Spawn(TeamSide team, BombSite site) =>
        new(Guid.NewGuid(), team, site, false, new Vec3(1f, 2f, 3f), new ViewAngles(0f, 90f));

    [Fact]
    public void Load_MissingFile_IsNotFound()
    {
        var loaded = Store().Load("de_mirage");
        Assert.False(loaded.Found);
        Assert.Empty(loaded.Spawns);
    }

    [Fact]
    public void Save_ThenLoad_RoundTrips()
    {
        var spawns = new[] { Spawn(TeamSide.T, BombSite.A), Spawn(TeamSide.CT, BombSite.B) };
        Store().Save("de_mirage", spawns);
        var loaded = Store().Load("de_mirage");
        Assert.True(loaded.Found);
        Assert.False(loaded.IsLegacy);
        Assert.Equal(spawns, loaded.Spawns);
    }

    [Fact]
    public void Save_WritesThroughATempFile_AndKeepsABackup()
    {
        var first = new[] { Spawn(TeamSide.T, BombSite.A) };
        Store().Save("de_dust2", first);
        Store().Save("de_dust2", new[] { Spawn(TeamSide.CT, BombSite.B) });
        Assert.False(File.Exists(_dir.File("de_dust2.json.tmp")));
        Assert.Equal(first, SpawnFileFormat.Parse(File.ReadAllText(_dir.File("de_dust2.json.bak"))).Spawns);
    }

    [Fact]
    public void Save_OverALegacyFile_KeepsAV3Backup()
    {
        File.WriteAllText(_dir.File("de_nuke.json"), LegacyJson);
        Store().Save("de_nuke", Store().Load("de_nuke").Spawns);
        Assert.Equal(LegacyJson, File.ReadAllText(_dir.File("de_nuke.json.v3.bak")));
        var reloaded = Store().Load("de_nuke");
        Assert.False(reloaded.IsLegacy);
        Assert.True(Assert.Single(reloaded.Spawns).CanPlant);
    }

    [Theory]
    [InlineData("../x")]
    [InlineData("de/../x")]
    [InlineData("")]
    public void UnsafeMapNames_AreRejected(string map)
    {
        Assert.Throws<ArgumentException>(() => Store().Load(map));
        Assert.Throws<ArgumentException>(() => Store().Save(map, Array.Empty<SpawnPoint>()));
        Assert.Empty(Directory.GetFiles(_dir.Path, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public void Save_OverAnInvalidFile_KeepsTheOriginalForever()
    {
        const string broken = "{ \"SchemaVersion\": 2, \"Spawns\": [ , ] }";
        File.WriteAllText(_dir.File("de_inferno.json"), broken);
        Store().Save("de_inferno", new[] { Spawn(TeamSide.T, BombSite.A) });
        Store().Save("de_inferno", new[] { Spawn(TeamSide.CT, BombSite.B) });
        var kept = Directory.GetFiles(_dir.Path, "de_inferno.json.*.invalid.bak");
        Assert.Equal(broken, File.ReadAllText(Assert.Single(kept)));
    }
}
