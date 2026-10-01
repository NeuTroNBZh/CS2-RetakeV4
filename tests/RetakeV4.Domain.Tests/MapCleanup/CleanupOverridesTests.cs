using RetakeV4.Domain.MapCleanup;

namespace RetakeV4.Domain.Tests.MapCleanup;

public class CleanupOverridesTests
{
    [Fact]
    public void RoundTrip()
    {
        var list = new[] { new CleanupOverride("name:a", CleanupKind.Ignore, "func_door"), new CleanupOverride("pos:func_breakable@1,2,3", CleanupKind.Vent, "x") };
        var (parsed, issues, invalid) = CleanupOverridesFormat.Parse(CleanupOverridesFormat.Serialize(list));
        Assert.False(invalid);
        Assert.Empty(issues);
        Assert.Equal(list, parsed);
    }

    [Fact]
    public void UnknownKind_LineIgnored_OthersKept()
    {
        var json = "[{\"Key\":\"a\",\"Kind\":\"Door\",\"Note\":\"\"},{\"Key\":\"b\",\"Kind\":\"Banana\",\"Note\":\"\"},{\"Key\":\"\",\"Kind\":\"Vent\"}]";
        var (parsed, issues, invalid) = CleanupOverridesFormat.Parse(json);
        Assert.False(invalid);
        Assert.Equal(new[] { "a" }, parsed.Select(o => o.Key));
        Assert.Equal(2, issues.Count);
    }

    [Fact]
    public void NumericKind_IsRejected()
    {
        var (parsed, issues, _) = CleanupOverridesFormat.Parse("[{\"Key\":\"a\",\"Kind\":\"2\"}]");
        Assert.Empty(parsed);
        Assert.Single(issues);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"a\":1}")]
    public void CorruptFile_IsReported(string json)
    {
        var (parsed, _, invalid) = CleanupOverridesFormat.Parse(json);
        Assert.True(invalid);
        Assert.Empty(parsed);
    }

    [Fact]
    public void ToMap_LastWins()
    {
        var map = CleanupOverridesFormat.ToMap(new[] { new CleanupOverride("a", CleanupKind.Door, ""), new CleanupOverride("a", CleanupKind.Vent, "") });
        Assert.Equal(CleanupKind.Vent, map["a"]);
    }
}
