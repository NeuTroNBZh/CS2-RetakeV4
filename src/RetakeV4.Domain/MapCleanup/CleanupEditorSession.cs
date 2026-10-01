namespace RetakeV4.Domain.MapCleanup;

// The HUD does not report a closed menu, so an editor that closed the panel or opened another menu would otherwise keep
// the automatic cleanup paused for the whole map.
public sealed record CleanupEditorSession(int Slot, DateTimeOffset LastActivity)
{
    public static readonly TimeSpan IdleLimit = TimeSpan.FromMinutes(3);

    public CleanupEditorSession Touched(DateTimeOffset now) => this with { LastActivity = now };

    public bool IsIdle(DateTimeOffset now) => now - LastActivity >= IdleLimit;

    public bool IsReplacedBy(int slot, string menuId, bool refreshOnly) =>
        slot == Slot && !refreshOnly && menuId != CleanupEditorMenu.MenuId;
}
