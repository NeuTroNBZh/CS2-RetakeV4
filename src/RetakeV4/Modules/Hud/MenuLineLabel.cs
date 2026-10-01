using CounterStrikeSharp.API.Core;
using RetakeV4.Domain.Hud;
using RetakeV4.Localization;

namespace RetakeV4.Modules.Hud;

// Same wording in front of the player and in the chat: toggles show their state, the current choice is marked.
internal static class MenuLineLabel
{
    // chosenMark: prefix of the current choice ("> " in the world and chat menus, a check mark in the center panel).
    public static string Format(ITextService text, CCSPlayerController player, MenuLine line, string chosenMark = "> ")
    {
        var label = HudTextFormatter.Format(text, player, line.Label);
        return line.ItemKind switch
        {
            MenuItemKind.Toggle => $"{label} : {text.For(player, line.IsOn ? "hud.menu.on" : "hud.menu.off")}",
            MenuItemKind.Choice when line.IsOn => $"{chosenMark}{label}",
            _ => label,
        };
    }
}
