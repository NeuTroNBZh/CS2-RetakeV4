using CounterStrikeSharp.API.Core;
using RetakeV4.Domain.Hud;
using RetakeV4.Localization;

namespace RetakeV4.Modules.Hud;

internal static class HudTextFormatter
{
    // An argument may itself be a HudText (a localized label inside a sentence): it is resolved for the same player.
    public static string Format(ITextService text, CCSPlayerController player, HudText value) =>
        value.Key is { } key
            ? text.For(player, key, value.Args.Select(a => a is HudText inner ? Format(text, player, inner) : a).ToArray())
            : value.Literal ?? string.Empty;
}
