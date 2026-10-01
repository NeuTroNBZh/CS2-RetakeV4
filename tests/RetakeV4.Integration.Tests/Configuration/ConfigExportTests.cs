using System.Text.Json;
using RetakeV4.Configuration;

namespace RetakeV4.Integration.Tests.Configuration;

public sealed class ConfigExportTests : IDisposable
{
    private readonly TempDirectory _dir = new();

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void Export_WritesOneValidJsonPerModuleConfig()
    {
        var files = ConfigExport.Run(_dir.Path, new ListLogger());
        Assert.Equal(
            new[]
            {
                "admin.json", "allocation.json", "api.json", "core.json", "grenades.json", "hud.json", "instadefuse.json",
                "links.json", "plant.json", "roundtypes.json", "spawns.json", "teams.json",
            },
            files);
        Assert.All(files, file => JsonDocument.Parse(File.ReadAllText(_dir.File(file))).Dispose());
    }

    [Fact]
    public void Export_IsIdempotent()
    {
        ConfigExport.Run(_dir.Path, new ListLogger());
        File.WriteAllText(_dir.File("core.json"), "{ \"Version\": 1, \"Debug\": true }");
        ConfigExport.Run(_dir.Path, new ListLogger());
        Assert.Contains("\"Debug\": true", File.ReadAllText(_dir.File("core.json")));
    }
}
