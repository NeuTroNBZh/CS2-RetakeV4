using RetakeV4.Domain.Geometry;
using RetakeV4.Domain.Hud;

namespace RetakeV4.Modules.Hud;

internal sealed class MenuSession
{
    public MenuSession(MenuNavigator navigator) => Navigator = navigator;

    public MenuNavigator Navigator { get; set; }

    public ViewAngles? Opened { get; set; }

    public int? Aimed { get; set; }

    public WorldTextMenuView? View { get; set; }

    // The menu stays open; it is anchored again in front of the player when its entities are recreated.
    public void Detach()
    {
        View?.Destroy();
        View = null;
        Opened = null;
        Aimed = null;
    }
}
