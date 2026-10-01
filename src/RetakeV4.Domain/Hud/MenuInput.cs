using RetakeV4.Domain.Rounds;

namespace RetakeV4.Domain.Hud;

public enum MenuInputSetting
{
    AimAndKeys,
    Keys,
}

public sealed record MenuControls(bool Aim, bool Movement, bool Keys);

public static class MenuInput
{
    // A living player in a live round keeps movement and shooting: only the number keys drive the menu.
    public static MenuControls For(RoundPhase phase, bool alive, MenuInputSetting setting) =>
        phase == RoundPhase.Live && alive
            ? new MenuControls(false, false, true)
            : new MenuControls(setting == MenuInputSetting.AimAndKeys, true, true);
}
