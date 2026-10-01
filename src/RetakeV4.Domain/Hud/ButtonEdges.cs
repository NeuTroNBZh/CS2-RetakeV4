namespace RetakeV4.Domain.Hud;

// Menus poll the held buttons every tick: only buttons held now and not on the previous tick count as a press.
public static class ButtonEdges
{
    public static ulong Pressed(ulong previous, ulong current) => current & ~previous;
}
