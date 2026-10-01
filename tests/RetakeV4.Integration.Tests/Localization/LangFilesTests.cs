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

    [Theory]
    [InlineData("spawns.round.announce")]
    [InlineData("teams.queue.joined")]
    [InlineData("teams.switch.refused")]
    [InlineData("teams.move.switched_after_ct_win")]
    [InlineData("teams.move.entered_from_queue")]
    [InlineData("teams.move.scrambled")]
    [InlineData("teams.move.balanced")]
    [InlineData("teams.scramble.requested")]
    [InlineData("teams.round.t_streak")]
    [InlineData("teams.inconsistent")]
    [InlineData("teams.no_permission")]
    public void Phase2aKeys_ArePresent(string key)
    {
        Assert.Contains(key, Load("en").Keys);
    }

    [Theory]
    [InlineData("plant.fast.instructions")]
    [InlineData("plant.failed")]
    public void Phase2bPlantKeys_ArePresent(string key)
    {
        Assert.Contains(key, Load("en").Keys);
    }

    [Theory]
    [InlineData("instadefuse.blocked.he")]
    [InlineData("instadefuse.blocked.molotov")]
    [InlineData("instadefuse.blocked.inferno")]
    [InlineData("instadefuse.not_enough_time")]
    [InlineData("instadefuse.success")]
    public void Phase2bInstaDefuseKeys_ArePresent(string key)
    {
        Assert.Contains(key, Load("en").Keys);
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
