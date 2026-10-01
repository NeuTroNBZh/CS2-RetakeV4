using RetakeV4.Configuration;

namespace RetakeV4.Integration.Tests.Configuration;

public class ConfigCheckTests : IDisposable
{
    private readonly TempDirectory _dir = new();
    private static readonly string PluginLang = Path.Combine(AppContext.BaseDirectory, "lang");

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void ExportedDefaults_HaveNoProblem()
    {
        ConfigExport.Run(_dir.Path, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
        Assert.Empty(ConfigCheck.Run(_dir.Path, null, PluginLang));
    }

    [Fact]
    public void InvalidModuleConfig_IsReported_AndTheDirectoryIsNotModified()
    {
        File.WriteAllText(_dir.File("announcements.json"), "{ \"Version\": 1, \"IntervalSeconds\": 1 }");
        var problems = ConfigCheck.Run(_dir.Path, null, PluginLang);
        Assert.Contains(problems, p => p.Contains("announcements.json", StringComparison.Ordinal));
        Assert.Equal(new[] { "announcements.json" }, Directory.GetFiles(_dir.Path).Select(Path.GetFileName));
    }

    [Fact]
    public void UnknownTextKey_IsReported()
    {
        Directory.CreateDirectory(Path.Combine(_dir.Path, "lang"));
        File.WriteAllText(Path.Combine(_dir.Path, "lang", "fr.json"), "{ \"core.gone\": \"x\" }");
        Assert.Contains(ConfigCheck.Run(_dir.Path, null, PluginLang), p => p.Contains("core.gone", StringComparison.Ordinal));
    }

    [Fact]
    public void BrokenSpawnFile_IsReported()
    {
        var spawns = Directory.CreateDirectory(Path.Combine(_dir.Path, "spawns")).FullName;
        File.WriteAllText(Path.Combine(spawns, "de_test.json"), "{ broken");
        Assert.Contains(ConfigCheck.Run(Path.Combine(_dir.Path, "none"), spawns, PluginLang), p => p.Contains("de_test", StringComparison.Ordinal));
    }
}
