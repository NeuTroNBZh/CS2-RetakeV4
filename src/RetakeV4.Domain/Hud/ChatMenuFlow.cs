namespace RetakeV4.Domain.Hud;

public static class ChatMenuFlow
{
    // A chat menu cannot be redrawn in place: only navigation (submenu, back, page) prints the next level;
    // a choice or a close ends it, the way the V3 chat menus did.
    public static bool ShowsNextLevel(MenuOutcome outcome) => outcome.Kind == MenuOutcomeKind.None;
}
