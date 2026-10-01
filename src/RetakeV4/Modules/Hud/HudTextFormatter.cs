using CounterStrikeSharp.API.Core;
using RetakeV4.Domain.Hud;
using RetakeV4.Localization;

namespace RetakeV4.Modules.Hud;

internal static class HudTextFormatter
{
    public static string Format(ITextService text, CCSPlayerController player, HudText value) =>
        value.Key is { } key ? text.For(player, key, value.Args.ToArray()) : value.Literal ?? string.Empty;
}
