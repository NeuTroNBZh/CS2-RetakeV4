using RetakeV4.Domain.Hud;
using RetakeV4.Modules.Hud;

namespace RetakeV4.Integration.Tests.Modules.Hud;

public class HudConfigValidatorTests
{
    private static readonly HudConfig Defaults = new();
    private readonly HudConfigValidator _validator = new();

    private ValidationResultView Validate(HudConfig config)
    {
        var result = _validator.Validate(config, Defaults, "hud.json");
        return new ValidationResultView(result.Config, result.Issues.Select(i => i.Key).ToList());
    }

    private sealed record ValidationResultView(HudConfig Config, IReadOnlyList<string> Keys);

    [Fact]
    public void Defaults_AreValid() => Assert.Empty(Validate(Defaults).Keys);

    [Theory]
    [InlineData("red")]
    [InlineData("#12345")]
    [InlineData("#12345G")]
    [InlineData("#FFFFFF' onload='x")]
    public void InvalidColor_FallsBackToTheDefault(string color)
    {
        var result = Validate(Defaults with { Theme = Defaults.Theme with { Accent = color } });
        Assert.Equal("#4FC3F7", result.Config.Theme.Accent);
        Assert.Equal(new[] { "Theme.Accent" }, result.Keys);
    }

    [Fact]
    public void OutOfRangeValues_FallBack()
    {
        var config = Defaults with
        {
            CenterRefreshMs = 10,
            Menu = Defaults.Menu with { DistanceUnits = 0f, Orientation = 3, WorldUnitsPerPx = float.NaN },
            Widgets = Defaults.Widgets with { Alerts = Defaults.Widgets.Alerts with { ShowSeconds = 0 } },
        };
        var result = Validate(config);
        Assert.Equal(250, result.Config.CenterRefreshMs);
        Assert.Equal(60f, result.Config.Menu.DistanceUnits);
        Assert.Equal(0, result.Config.Menu.Orientation);
        Assert.Equal(0.1f, result.Config.Menu.WorldUnitsPerPx);
        Assert.Equal(4, result.Config.Widgets.Alerts.ShowSeconds);
        Assert.Equal(5, result.Keys.Count);
    }

    [Fact]
    public void UndefinedEnums_FallBack()
    {
        var result = Validate(Defaults with { Menu = Defaults.Menu with { Input = (MenuInputSetting)7, FollowMode = (MenuFollowMode)9 } });
        Assert.Equal(MenuInputSetting.AimAndKeys, result.Config.Menu.Input);
        Assert.Equal(MenuFollowMode.Tick, result.Config.Menu.FollowMode);
        Assert.Equal(new[] { "Menu.Input", "Menu.FollowMode" }, result.Keys);
    }

    [Fact]
    public void MissingSections_FallBackToDefaults()
    {
        var result = Validate(Defaults with { Theme = null!, Widgets = null!, Menu = null! });
        Assert.Equal(Defaults.Theme, result.Config.Theme);
        Assert.Equal(Defaults.Widgets, result.Config.Widgets);
        Assert.Equal(Defaults.Menu, result.Config.Menu);
        Assert.Equal(3, result.Keys.Count);
    }

    [Fact]
    public void MissingWidget_FallsBackToItsDefault()
    {
        var result = Validate(Defaults with { Widgets = Defaults.Widgets with { QueueStatus = null! } });
        Assert.True(result.Config.Widgets.QueueStatus.Enabled);
        Assert.Equal(new[] { "Widgets.QueueStatus" }, result.Keys);
    }

    [Fact]
    public void MenuDisplay_DefaultsToWorldText() => Assert.Equal(MenuDisplay.WorldText, Defaults.Menu.Display);

    [Fact]
    public void UndefinedMenuDisplay_FallsBackToWorldText()
    {
        var result = Validate(Defaults with { Menu = Defaults.Menu with { Display = (MenuDisplay)5 } });
        Assert.Equal(MenuDisplay.WorldText, result.Config.Menu.Display);
        Assert.Equal(new[] { "Menu.Display" }, result.Keys);
    }

    [Fact]
    public void CenterHtmlDisplay_IsValid()
    {
        var result = Validate(Defaults with { Menu = Defaults.Menu with { Display = MenuDisplay.CenterHtml } });
        Assert.Equal(MenuDisplay.CenterHtml, result.Config.Menu.Display);
        Assert.Empty(result.Keys);
    }
}
