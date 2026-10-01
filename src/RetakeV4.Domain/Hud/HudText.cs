namespace RetakeV4.Domain.Hud;

// Key is resolved per player through the localizer; Literal is shown as is (weapon names, config values).
public sealed record HudText(string? Key, string? Literal, IReadOnlyList<object> Args)
{
    public static HudText Of(string key, params object[] args) => new(key, null, args);

    public static HudText Raw(string text) => new(null, text, Array.Empty<object>());
}
