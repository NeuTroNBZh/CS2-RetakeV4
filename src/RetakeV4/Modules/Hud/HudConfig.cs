using RetakeV4.Configuration;
using RetakeV4.Domain.Hud;

namespace RetakeV4.Modules.Hud;

public enum MenuFollowMode
{
    Tick,
    Parent,
}

public enum MenuDisplay
{
    WorldText,
    Chat,
    CenterHtml,
}

public sealed record HudThemeConfig
{
    public string Accent { get; init; } = "#4FC3F7";

    public string Text { get; init; } = "#FFFFFF";

    public string Muted { get; init; } = "#9E9E9E";

    public float FontSize { get; init; } = 24f;

    // Team entries in the center panel (CenterHtml).
    public string TeamT { get; init; } = "#EAB54F";

    public string TeamCt { get; init; } = "#5D9CEC";

    public HudTheme ToTheme() => new(Accent, Text, Muted);
}

public sealed record HudWidgetConfig
{
    public bool Enabled { get; init; } = true;

    public int ShowSeconds { get; init; } = 6;
}

public sealed record HudWidgetsConfig
{
    public HudWidgetConfig RoundInfo { get; init; } = new();

    public HudWidgetConfig QueueStatus { get; init; } = new();

    public HudWidgetConfig Alerts { get; init; } = new() { ShowSeconds = 4 };
}

// Orientation and FollowMode select the formulas tested by the HUD prototype (docs/spikes/hud-probe-findings.md).
public sealed record HudMenuConfig
{
    // WorldText: menus in front of the player (point_worldtext). Chat: numbered chat menus chosen with !1, !2... (V3 style).
    // CenterHtml: an HTML panel in the center of the screen, forward/back to move, E to choose.
    public MenuDisplay Display { get; init; } = MenuDisplay.CenterHtml;

    // CenterHtml: lines shown at once; longer lists scroll with the cursor.
    public int CenterVisibleLines { get; init; } = 6;

    // CenterHtml: the player cannot move while a menu is open (forward / back drive the cursor), like MenuManager menus.
    public bool FreezeWhileOpen { get; init; } = true;

    public MenuInputSetting Input { get; init; } = MenuInputSetting.AimAndKeys;

    public float DistanceUnits { get; init; } = 60f;

    public float LineHeightUnits { get; init; } = 4f;

    public float HalfWidthUnits { get; init; } = 18f;

    public float WorldUnitsPerPx { get; init; } = 0.1f;

    public int Orientation { get; init; }

    public MenuFollowMode FollowMode { get; init; } = MenuFollowMode.Tick;

    public bool BlockAttackWhileAiming { get; init; } = true;
}

public sealed record HudConfig : ModuleConfig
{
    public HudConfig() => Version = 1;

    public HudThemeConfig Theme { get; init; } = new();

    public int CenterRefreshMs { get; init; } = 250;

    public int CenterMaxLines { get; init; } = 6;

    public HudWidgetsConfig Widgets { get; init; } = new();

    public HudMenuConfig Menu { get; init; } = new();
}
