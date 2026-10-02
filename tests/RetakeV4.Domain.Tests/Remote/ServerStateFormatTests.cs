using System.Text.Json;
using RetakeV4.Domain.Remote;

namespace RetakeV4.Domain.Tests.Remote;

public class ServerStateFormatTests
{
    private static ServerStateSnapshot Sample(string name = "NeuTroNBZh") => new(
        "de_nuke", "Live", false, false, 12, 30, 5, 6, "A", "FullBuy",
        new[] { new RemotePlayer(3, "76561199051460419", name, "CT", true, false, 87), new RemotePlayer(7, "", "BOT Kim", "T", false, true, 0) },
        1, false, "de_mirage", false, true, new RemoteForce("B", true), true, 42, "planted");

    [Fact]
    public void Format_IsPrefixedSingleLineJsonWithTheSpecFields()
    {
        var line = ServerStateFormat.Format(Sample());
        Assert.StartsWith("RETAKE_STATE {", line);
        var root = JsonDocument.Parse(line[ServerStateFormat.Prefix.Length..]).RootElement;
        Assert.Equal(2, root.GetProperty("v").GetInt32());
        Assert.Equal("de_nuke", root.GetProperty("map").GetString());
        Assert.Equal("Live", root.GetProperty("phase").GetString());
        Assert.False(root.GetProperty("warmup").GetBoolean());
        Assert.False(root.GetProperty("paused").GetBoolean());
        Assert.Equal(12, root.GetProperty("round").GetInt32());
        Assert.Equal(30, root.GetProperty("maxRounds").GetInt32());
        Assert.Equal(5, root.GetProperty("score").GetProperty("t").GetInt32());
        Assert.Equal(6, root.GetProperty("score").GetProperty("ct").GetInt32());
        Assert.Equal("A", root.GetProperty("site").GetString());
        Assert.Equal("FullBuy", root.GetProperty("roundType").GetString());
        var player = root.GetProperty("players")[0];
        Assert.Equal(3, player.GetProperty("userId").GetInt32());
        Assert.Equal("CT", player.GetProperty("team").GetString());
        Assert.True(player.GetProperty("alive").GetBoolean());
        Assert.True(root.GetProperty("players")[1].GetProperty("bot").GetBoolean());
        Assert.Equal(1, root.GetProperty("queue").GetInt32());
        Assert.False(root.GetProperty("vote").GetProperty("open").GetBoolean());
        Assert.Equal("de_mirage", root.GetProperty("vote").GetProperty("nextMap").GetString());
        Assert.False(root.GetProperty("editors").GetProperty("spawns").GetBoolean());
        Assert.True(root.GetProperty("editors").GetProperty("cleanup").GetBoolean());
        Assert.Equal("76561199051460419", player.GetProperty("steamId").GetString());
        Assert.Equal(87, player.GetProperty("health").GetInt32());
        Assert.Equal("", root.GetProperty("players")[1].GetProperty("steamId").GetString());
        Assert.Equal("B", root.GetProperty("force").GetProperty("site").GetString());
        Assert.True(root.GetProperty("force").GetProperty("sticky").GetBoolean());
        Assert.True(root.GetProperty("scramblePending").GetBoolean());
        Assert.Equal(42, root.GetProperty("clock").GetProperty("timeLeft").GetInt32());
        Assert.Equal("planted", root.GetProperty("clock").GetProperty("bomb").GetString());
    }

    [Fact]
    public void NoForceAndNoTimer_AreJsonNull()
    {
        var line = ServerStateFormat.Format(Sample() with { Force = null, TimeLeft = null });
        var root = JsonDocument.Parse(line[ServerStateFormat.Prefix.Length..]).RootElement;
        Assert.Equal(JsonValueKind.Null, root.GetProperty("force").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("clock").GetProperty("timeLeft").ValueKind);
    }

    // Review Focus 1: hostile names stay inside one JSON line.
    [Fact]
    public void HostileName_StaysOneValidLine()
    {
        var name = "a\"b\nc;css_ban #1 0\u00e9\u4e2d";
        var line = ServerStateFormat.Format(Sample(name));
        Assert.DoesNotContain('\n', line);
        Assert.DoesNotContain('\r', line);
        var root = JsonDocument.Parse(line[ServerStateFormat.Prefix.Length..]).RootElement;
        Assert.Equal(name, root.GetProperty("players")[0].GetProperty("name").GetString());
    }

    // Non-ASCII names are written as UTF-8, not as six-byte escapes: a full server stays well under console line limits.
    [Fact]
    public void NonAsciiName_IsNotEscaped()
    {
        var line = ServerStateFormat.Format(Sample("Éric中"));
        Assert.Contains("Éric中", line);
    }

    [Fact]
    public void NullSiteAndRoundType_AreJsonNull()
    {
        var line = ServerStateFormat.Format(Sample() with { Site = null, RoundType = null, NextMap = null });
        var root = JsonDocument.Parse(line[ServerStateFormat.Prefix.Length..]).RootElement;
        Assert.Equal(JsonValueKind.Null, root.GetProperty("site").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("roundType").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("vote").GetProperty("nextMap").ValueKind);
    }
}
