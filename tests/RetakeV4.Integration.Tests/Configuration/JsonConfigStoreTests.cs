using RetakeV4.Configuration;

namespace RetakeV4.Integration.Tests.Configuration;

public enum SampleMode
{
    Alpha,
    Beta,
}

public sealed record SampleConfig : ModuleConfig
{
    public SampleConfig() => Version = 2;

    public int Count { get; init; } = 3;

    public SampleMode Mode { get; init; } = SampleMode.Alpha;
}

public sealed class NonNegativeCountValidator : IConfigValidator<SampleConfig>
{
    public ValidationResult<SampleConfig> Validate(SampleConfig config, SampleConfig defaults, string file) =>
        config.Count >= 0
            ? new ValidationResult<SampleConfig>(config, Array.Empty<ConfigIssue>())
            : new ValidationResult<SampleConfig>(
                config with { Count = defaults.Count },
                new[] { new ConfigIssue(file, nameof(SampleConfig.Count), "must be >= 0; using default") });
}

public sealed class JsonConfigStoreTests : IDisposable
{
    private readonly TempDirectory _dir = new();
    private readonly JsonConfigStore _store;

    public JsonConfigStoreTests() => _store = new JsonConfigStore(_dir.Path);

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void MissingFile_WritesDefaults()
    {
        var result = _store.Load("sample.json", new SampleConfig());
        Assert.True(result.CreatedDefault);
        Assert.Empty(result.Issues);
        var written = File.ReadAllText(_dir.File("sample.json"));
        Assert.Contains("\"Count\": 3", written);
        Assert.Contains("\"Mode\": \"Alpha\"", written);
    }

    [Fact]
    public void PartialFile_UsesDefaultsForMissingKeys()
    {
        File.WriteAllText(_dir.File("sample.json"), """{ "Version": 2, "Count": 7 }""");
        var result = _store.Load("sample.json", new SampleConfig());
        Assert.Equal(7, result.Config.Count);
        Assert.Equal(SampleMode.Alpha, result.Config.Mode);
        Assert.True(result.Config.Enabled);
        Assert.Empty(result.Issues);
        Assert.False(result.CreatedDefault);
    }

    [Fact]
    public void MalformedJson_UsesDefaults_AndLeavesFileUntouched()
    {
        const string broken = """{ "Count": """;
        File.WriteAllText(_dir.File("sample.json"), broken);
        var result = _store.Load("sample.json", new SampleConfig());
        Assert.Equal(3, result.Config.Count);
        Assert.Single(result.Issues);
        Assert.Equal(broken, File.ReadAllText(_dir.File("sample.json")));
    }

    [Fact]
    public void UnknownEnumValue_UsesDefaults_AndLeavesFileUntouched()
    {
        const string content = """{ "Version": 2, "Mode": "Gamma" }""";
        File.WriteAllText(_dir.File("sample.json"), content);
        var result = _store.Load("sample.json", new SampleConfig());
        Assert.Equal(SampleMode.Alpha, result.Config.Mode);
        var issue = Assert.Single(result.Issues);
        Assert.Contains("Mode", issue.Key);
        Assert.Equal(content, File.ReadAllText(_dir.File("sample.json")));
    }

    [Fact]
    public void CommentsAndTrailingCommas_AreAccepted()
    {
        File.WriteAllText(_dir.File("sample.json"), "{\n // comment\n \"Version\": 2, \"Mode\": \"Beta\",\n}");
        var result = _store.Load("sample.json", new SampleConfig());
        Assert.Equal(SampleMode.Beta, result.Config.Mode);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void OlderVersion_IsReported()
    {
        File.WriteAllText(_dir.File("sample.json"), """{ "Version": 1 }""");
        var result = _store.Load("sample.json", new SampleConfig());
        var issue = Assert.Single(result.Issues);
        Assert.Equal("Version", issue.Key);
    }

    [Fact]
    public void NullDocument_UsesDefaults()
    {
        File.WriteAllText(_dir.File("sample.json"), "null");
        var result = _store.Load("sample.json", new SampleConfig());
        Assert.Equal(3, result.Config.Count);
        Assert.Single(result.Issues);
    }

    [Fact]
    public void Validator_SanitizesAndReports()
    {
        File.WriteAllText(_dir.File("sample.json"), """{ "Version": 2, "Count": -4 }""");
        var result = _store.Load("sample.json", new SampleConfig(), new NonNegativeCountValidator());
        Assert.Equal(3, result.Config.Count);
        Assert.Equal("Count", Assert.Single(result.Issues).Key);
    }
}
