using RetakeV4.Modules.Core;

namespace RetakeV4.Integration.Tests.Modules.Core;

public class CoreConfigValidatorTests
{
    private static readonly CoreConfig Defaults = new();
    private readonly CoreConfigValidator _validator = new();

    [Fact]
    public void Defaults_AreValid()
    {
        var result = _validator.Validate(Defaults, Defaults, "core.json");
        Assert.Empty(result.Issues);
        Assert.Equal(Defaults, result.Config);
    }

    [Theory]
    [InlineData("../../server.cfg")]
    [InlineData("RetakeV4/retake.cfg; quit")]
    [InlineData("C:/evil.cfg")]
    [InlineData("/etc/passwd")]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("RetakeV4/retake.cfg\n")]
    public void UnsafeExecConfig_FallsBackToDefault(string path)
    {
        var result = _validator.Validate(Defaults with { ExecConfig = path }, Defaults, "core.json");
        Assert.Equal(Defaults.ExecConfig, result.Config.ExecConfig);
        Assert.Contains(result.Issues, i => i.Key == nameof(CoreConfig.ExecConfig));
    }

    [Theory]
    [InlineData("RetakeV4/retake.cfg")]
    [InlineData("custom_retake.cfg")]
    [InlineData("my-server/retake_5v5.cfg")]
    public void SafeExecConfig_IsKept(string path)
    {
        var result = _validator.Validate(Defaults with { ExecConfig = path }, Defaults, "core.json");
        Assert.Equal(path, result.Config.ExecConfig);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void NegativeFallback_FallsBackToDefault()
    {
        var result = _validator.Validate(Defaults with { WarmupFallbackSeconds = -3f }, Defaults, "core.json");
        Assert.Equal(Defaults.WarmupFallbackSeconds, result.Config.WarmupFallbackSeconds);
        Assert.Single(result.Issues);
    }

    [Theory]
    [InlineData(0.01f)]
    [InlineData(10f)]
    public void OutOfRangeWatchdogInterval_FallsBackToDefault(float interval)
    {
        var result = _validator.Validate(Defaults with { WatchdogIntervalSeconds = interval }, Defaults, "core.json");
        Assert.Equal(Defaults.WatchdogIntervalSeconds, result.Config.WatchdogIntervalSeconds);
        Assert.Single(result.Issues);
    }
}
