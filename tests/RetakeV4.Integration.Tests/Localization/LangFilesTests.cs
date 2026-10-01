using System.Text.Json;
using System.Text.RegularExpressions;

namespace RetakeV4.Integration.Tests.Localization;

public partial class LangFilesTests
{
    private static readonly string LangDirectory = Path.Combine(AppContext.BaseDirectory, "lang");

    [GeneratedRegex(@"^[a-z]+(\.[a-z0-9_]+)+$")]
    private static partial Regex KeyPattern();

    [GeneratedRegex(@"\{(\d+)\}")]
    private static partial Regex PlaceholderPattern();

    private static Dictionary<string, string> Load(string language) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(LangDirectory, $"{language}.json")))
        ?? throw new InvalidDataException($"{language}.json is empty");

    [Fact]
    public void EnglishAndFrench_HaveTheSameKeys()
    {
        Assert.Equal(Load("en").Keys.Order(), Load("fr").Keys.Order());
    }

    [Theory]
    [InlineData("en")]
    [InlineData("fr")]
    public void Keys_FollowModuleSectionKeyConvention(string language)
    {
        Assert.All(Load(language).Keys, key => Assert.Matches(KeyPattern(), key));
    }

    [Fact]
    public void Placeholders_MatchAcrossLanguages()
    {
        var en = Load("en");
        var fr = Load("fr");
        foreach (var key in en.Keys)
        {
            var expected = PlaceholderPattern().Matches(en[key]).Select(m => m.Value).Order();
            var actual = PlaceholderPattern().Matches(fr[key]).Select(m => m.Value).Order();
            Assert.True(expected.SequenceEqual(actual), $"placeholder mismatch for '{key}'");
        }
    }

    [Fact]
    public void Phase1Keys_ArePresent()
    {
        var en = Load("en");
        Assert.Contains("core.prefix", en.Keys);
        Assert.Contains("core.info.version", en.Keys);
        Assert.Contains("core.warmup.forced_end", en.Keys);
    }
}
