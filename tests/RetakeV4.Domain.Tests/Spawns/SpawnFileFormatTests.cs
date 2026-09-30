using RetakeV4.Domain.Common;
using RetakeV4.Domain.Geometry;
using RetakeV4.Domain.Spawns;

namespace RetakeV4.Domain.Tests.Spawns;

public class SpawnFileFormatTests
{
    private const string LegacyMirage = """
        [
          {"SpawnId":"c9616622-dfa0-4de3-b2a0-c7ef9ad372e5","Team":2,"BombSite":0,"IsInBombZone":true,"PositionX":-401.6623,"PositionY":-2394.9688,"PositionZ":-102.42038,"QAngleX":0,"QAngleY":106.08686,"QAngleZ":0},
          {"SpawnId":"44230977-093e-45d5-84a9-8f516dc0c153","Team":3,"BombSite":1,"IsInBombZone":false,"PositionX":1176.5684,"PositionY":-556.2163,"PositionZ":-135.40727,"QAngleX":0,"QAngleY":-92.24271,"QAngleZ":0}
        ]
        """;

    [Fact]
    public void Legacy_IsConverted()
    {
        var result = SpawnFileFormat.Parse(LegacyMirage);
        Assert.True(result.IsLegacyFormat);
        Assert.Empty(result.Issues);
        Assert.Equal(2, result.Spawns.Count);
        var t = result.Spawns[0];
        Assert.Equal(Guid.Parse("c9616622-dfa0-4de3-b2a0-c7ef9ad372e5"), t.Id);
        Assert.Equal(TeamSide.T, t.Team);
        Assert.Equal(BombSite.A, t.Site);
        Assert.True(t.CanPlant);
        Assert.Equal(new Vec3(-401.6623f, -2394.9688f, -102.42038f), t.Position);
        Assert.Equal(106.08686f, t.Angle.Yaw, 4);
        Assert.Equal(TeamSide.CT, result.Spawns[1].Team);
        Assert.Equal(BombSite.B, result.Spawns[1].Site);
    }

    [Theory]
    [InlineData("""[{"SpawnId":"c9616622-dfa0-4de3-b2a0-c7ef9ad372e5","Team":5,"BombSite":0}]""")]
    [InlineData("""[{"SpawnId":"c9616622-dfa0-4de3-b2a0-c7ef9ad372e5","Team":2,"BombSite":-1}]""")]
    public void Legacy_InvalidTeamOrSite_IsSkippedWithIssue(string json)
    {
        var result = SpawnFileFormat.Parse(json);
        Assert.Empty(result.Spawns);
        Assert.Single(result.Issues);
    }

    [Fact]
    public void Legacy_EmptyId_GetsANewId()
    {
        var result = SpawnFileFormat.Parse("""[{"SpawnId":"00000000-0000-0000-0000-000000000000","Team":3,"BombSite":0}]""");
        Assert.NotEqual(Guid.Empty, Assert.Single(result.Spawns).Id);
    }

    [Fact]
    public void V2_RoundTrips()
    {
        var spawns = SpawnFileFormat.Parse(LegacyMirage).Spawns;
        var json = SpawnFileFormat.Serialize("de_mirage", spawns);
        var result = SpawnFileFormat.Parse(json);
        Assert.False(result.IsLegacyFormat);
        Assert.Empty(result.Issues);
        Assert.Equal(spawns, result.Spawns);
        Assert.Contains("\"SchemaVersion\": 2", json);
        Assert.Contains("\"Team\": \"CT\"", json);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{ not json")]
    [InlineData("""{ "SchemaVersion": 2, "Map": "x", "Spawns": [ { "Team": "Z" } ] }""")]
    public void InvalidContent_ReturnsEmptyWithIssue(string json)
    {
        var result = SpawnFileFormat.Parse(json);
        Assert.Empty(result.Spawns);
        Assert.Single(result.Issues);
    }

    [Fact]
    public void FutureSchemaVersion_IsRejected()
    {
        var result = SpawnFileFormat.Parse("""{ "SchemaVersion": 3, "Map": "x", "Spawns": [] }""");
        Assert.Empty(result.Spawns);
        Assert.Contains("schema", Assert.Single(result.Issues));
    }

    [Fact]
    public void DuplicateIds_KeepFirst()
    {
        var spawn = SpawnFileFormat.Parse(LegacyMirage).Spawns[0];
        var json = SpawnFileFormat.Serialize("x", new[] { spawn, spawn with { Site = BombSite.B } });
        var result = SpawnFileFormat.Parse(json);
        Assert.Equal(BombSite.A, Assert.Single(result.Spawns).Site);
        Assert.Single(result.Issues);
    }
}
