using RetakeV4.Domain.Localization;

namespace RetakeV4.Domain.Tests.Localization;

public class TextOverridesTests
{
    private static readonly Dictionary<string, string> Reference = new()
    {
        ["core.prefix"] = "{lightblue}[Retake]{default}",
        ["teams.queue.joined"] = "You are #{0} in the queue",
        ["allocation.menu.summary"] = "{0}: {1} / {2}",
    };

    private static (TextOverrides, IReadOnlyList<TextOverrideIssue>) Build(string language, Dictionary<string, string> values) =>
        TextOverrides.Build(new Dictionary<string, IReadOnlyDictionary<string, string>> { [language] = values }, Reference);

    [Fact]
    public void KnownKey_IsReplaced_AndPlaceholdersFilled()
    {
        var (overrides, issues) = Build("fr", new() { ["teams.queue.joined"] = "Tu es {0}e dans la file" });
        Assert.Empty(issues);
        Assert.Equal("Tu es 3e dans la file", overrides.Format("fr", "teams.queue.joined", new object[] { 3 }));
    }

    [Fact]
    public void ColorTagsAndBraces_AreLeftUntouched()
    {
        var (overrides, _) = Build("fr", new() { ["core.prefix"] = "{gold}[Agora-Retake]{default}" });
        Assert.Equal("{gold}[Agora-Retake]{default}", overrides.Format("fr", "core.prefix", Array.Empty<object>()));
    }

    [Fact]
    public void UnknownKey_EmptyValue_AndExtraPlaceholder_AreRejected()
    {
        var (overrides, issues) = Build("fr", new()
        {
            ["teams.nope"] = "x",
            ["core.prefix"] = "   ",
            ["teams.queue.joined"] = "{0} {1}",
        });
        Assert.Equal(0, overrides.Count);
        Assert.Equal(new[] { "core.prefix", "teams.nope", "teams.queue.joined" }, issues.Select(i => i.Key).Order());
    }

    [Fact]
    public void OmittedPlaceholder_IsAllowed()
    {
        var (overrides, issues) = Build("fr", new() { ["allocation.menu.summary"] = "{0}" });
        Assert.Empty(issues);
        Assert.Equal("Pistol", overrides.Format("fr", "allocation.menu.summary", new object[] { "Pistol", "Glock", "-" }));
    }

    [Theory]
    [InlineData("fr", true)]
    [InlineData("fr-FR", true)]
    [InlineData("FR", true)]
    [InlineData("pt-BR", false)]
    [InlineData("en", false)]
    public void Culture_FallsBackToItsLanguage(string culture, bool replaced)
    {
        var (overrides, _) = Build("fr", new() { ["core.prefix"] = "[A]" });
        Assert.Equal(replaced, overrides.Format(culture, "core.prefix", Array.Empty<object>()) is not null);
    }

    [Fact]
    public void Empty_ReplacesNothing()
    {
        Assert.Null(TextOverrides.Empty.Format("fr", "core.prefix", Array.Empty<object>()));
    }
}
