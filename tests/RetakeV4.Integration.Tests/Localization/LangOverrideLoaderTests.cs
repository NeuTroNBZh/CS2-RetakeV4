using RetakeV4.Localization;

namespace RetakeV4.Integration.Tests.Localization;

public class LangOverrideLoaderTests : IDisposable
{
    private readonly TempDirectory _dir = new();
    private static readonly string PluginLang = Path.Combine(AppContext.BaseDirectory, "lang");

    public void Dispose() => _dir.Dispose();

    private void Write(string name, string content)
    {
        Directory.CreateDirectory(Path.Combine(_dir.Path, "lang"));
        File.WriteAllText(Path.Combine(_dir.Path, "lang", name), content);
    }

    [Fact]
    public void NoLangFolder_GivesNoOverrides_AndNoProblem()
    {
        var (overrides, problems) = LangOverrideLoader.Read(_dir.Path, PluginLang);
        Assert.Equal(0, overrides.Count);
        Assert.Empty(problems);
    }

    [Fact]
    public void ValidFile_WithBomAndComments_IsLoaded()
    {
        Write("fr.json", "\uFEFF{ // Agora\n \"core.prefix\": \"{gold}[Agora-Retake]{default}\", }");
        var (overrides, problems) = LangOverrideLoader.Read(_dir.Path, PluginLang);
        Assert.Empty(problems);
        Assert.Equal("{gold}[Agora-Retake]{default}", overrides.Format("fr-FR", "core.prefix", Array.Empty<object>()));
    }

    [Fact]
    public void InvalidJson_IsReported_AndOtherFilesStillLoad()
    {
        Write("en.json", "{ not json");
        Write("fr.json", "{ \"core.prefix\": \"[A]\" }");
        var (overrides, problems) = LangOverrideLoader.Read(_dir.Path, PluginLang);
        Assert.Contains(problems, p => p.Contains("en.json", StringComparison.Ordinal));
        Assert.Equal(1, overrides.Count);
    }

    [Fact]
    public void UnknownKey_IsReported_WithItsName()
    {
        Write("fr.json", "{ \"core.does_not_exist\": \"x\" }");
        var (_, problems) = LangOverrideLoader.Read(_dir.Path, PluginLang);
        Assert.Contains(problems, p => p.Contains("core.does_not_exist", StringComparison.Ordinal));
    }

    [Fact]
    public void MissingPluginTexts_AreReported_WithoutThrowing()
    {
        Write("fr.json", "{ \"core.prefix\": \"[A]\" }");
        var (overrides, problems) = LangOverrideLoader.Read(_dir.Path, Path.Combine(_dir.Path, "missing"));
        Assert.Equal(0, overrides.Count);
        Assert.Contains(problems, p => p.Contains("en.json", StringComparison.Ordinal));
    }
}
