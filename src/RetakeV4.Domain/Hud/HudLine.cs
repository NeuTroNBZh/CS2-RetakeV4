namespace RetakeV4.Domain.Hud;

public enum HudStyle
{
    Text,
    Accent,
    Muted,
}

public sealed record HudLine(HudText Text, HudStyle Style);

public sealed record HudTheme(string Accent, string Text, string Muted)
{
    public string ColorOf(HudStyle style) => style switch
    {
        HudStyle.Accent => Accent,
        HudStyle.Muted => Muted,
        _ => Text,
    };
}
