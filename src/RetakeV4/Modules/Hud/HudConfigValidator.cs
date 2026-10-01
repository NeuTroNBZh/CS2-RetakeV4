using System.Text.RegularExpressions;
using RetakeV4.Configuration;

namespace RetakeV4.Modules.Hud;

public sealed partial class HudConfigValidator : IConfigValidator<HudConfig>
{
    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
    private static partial Regex HexColor();

    public ValidationResult<HudConfig> Validate(HudConfig config, HudConfig defaults, string file)
    {
        var issues = new List<ConfigIssue>();
        var validated = config with
        {
            Theme = ValidateTheme(config.Theme ?? Missing(defaults.Theme, nameof(HudConfig.Theme), file, issues), defaults.Theme, file, issues),
            CenterRefreshMs = InRange(config.CenterRefreshMs, 50, 2000, defaults.CenterRefreshMs, nameof(HudConfig.CenterRefreshMs), file, issues),
            CenterMaxLines = InRange(config.CenterMaxLines, 1, 10, defaults.CenterMaxLines, nameof(HudConfig.CenterMaxLines), file, issues),
            Widgets = ValidateWidgets(config.Widgets ?? Missing(defaults.Widgets, nameof(HudConfig.Widgets), file, issues), defaults.Widgets, file, issues),
            Menu = ValidateMenu(config.Menu ?? Missing(defaults.Menu, nameof(HudConfig.Menu), file, issues), defaults.Menu, file, issues),
        };
        return new ValidationResult<HudConfig>(validated, issues);
    }

    private static HudThemeConfig ValidateTheme(HudThemeConfig theme, HudThemeConfig defaults, string file, List<ConfigIssue> issues) => theme with
    {
        Accent = Color(theme.Accent, defaults.Accent, "Theme.Accent", file, issues),
        Text = Color(theme.Text, defaults.Text, "Theme.Text", file, issues),
        Muted = Color(theme.Muted, defaults.Muted, "Theme.Muted", file, issues),
        FontSize = InRange(theme.FontSize, 8f, 128f, defaults.FontSize, "Theme.FontSize", file, issues),
    };

    private static HudWidgetsConfig ValidateWidgets(HudWidgetsConfig widgets, HudWidgetsConfig defaults, string file, List<ConfigIssue> issues) => widgets with
    {
        RoundInfo = Widget(widgets.RoundInfo, defaults.RoundInfo, "Widgets.RoundInfo", file, issues),
        QueueStatus = Widget(widgets.QueueStatus, defaults.QueueStatus, "Widgets.QueueStatus", file, issues),
        Alerts = Widget(widgets.Alerts, defaults.Alerts, "Widgets.Alerts", file, issues),
    };

    private static HudWidgetConfig Widget(HudWidgetConfig? widget, HudWidgetConfig defaults, string key, string file, List<ConfigIssue> issues)
    {
        var present = widget ?? Missing(defaults, key, file, issues);
        return present with { ShowSeconds = InRange(present.ShowSeconds, 1, 60, defaults.ShowSeconds, $"{key}.ShowSeconds", file, issues) };
    }

    private static HudMenuConfig ValidateMenu(HudMenuConfig menu, HudMenuConfig defaults, string file, List<ConfigIssue> issues) => menu with
    {
        Input = Defined(menu.Input, defaults.Input, "Menu.Input", file, issues),
        FollowMode = Defined(menu.FollowMode, defaults.FollowMode, "Menu.FollowMode", file, issues),
        DistanceUnits = InRange(menu.DistanceUnits, 10f, 200f, defaults.DistanceUnits, "Menu.DistanceUnits", file, issues),
        LineHeightUnits = InRange(menu.LineHeightUnits, 1f, 20f, defaults.LineHeightUnits, "Menu.LineHeightUnits", file, issues),
        HalfWidthUnits = InRange(menu.HalfWidthUnits, 2f, 100f, defaults.HalfWidthUnits, "Menu.HalfWidthUnits", file, issues),
        WorldUnitsPerPx = InRange(menu.WorldUnitsPerPx, 0.01f, 1f, defaults.WorldUnitsPerPx, "Menu.WorldUnitsPerPx", file, issues),
        Orientation = InRange(menu.Orientation, 0, 2, defaults.Orientation, "Menu.Orientation", file, issues),
    };

    private static T Missing<T>(T defaults, string key, string file, List<ConfigIssue> issues)
    {
        issues.Add(new ConfigIssue(file, key, "missing; using defaults"));
        return defaults;
    }

    private static string Color(string? value, string fallback, string key, string file, List<ConfigIssue> issues)
    {
        if (value is not null && HexColor().IsMatch(value))
        {
            return value;
        }
        issues.Add(new ConfigIssue(file, key, "must be a #RRGGBB color; using default"));
        return fallback;
    }

    // NaN compares below any minimum, so it falls back too.
    private static T InRange<T>(T value, T min, T max, T fallback, string key, string file, List<ConfigIssue> issues) where T : IComparable<T>
    {
        if (value.CompareTo(min) >= 0 && value.CompareTo(max) <= 0)
        {
            return value;
        }
        issues.Add(new ConfigIssue(file, key, $"must be between {min} and {max}; using default"));
        return fallback;
    }

    private static T Defined<T>(T value, T fallback, string key, string file, List<ConfigIssue> issues) where T : struct, Enum
    {
        if (Enum.IsDefined(value))
        {
            return value;
        }
        issues.Add(new ConfigIssue(file, key, "unknown value; using default"));
        return fallback;
    }
}
