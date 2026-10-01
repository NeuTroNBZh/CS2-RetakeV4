using System.Collections.Immutable;
using RetakeV4.Domain.Common;

namespace RetakeV4.Domain.Hud;

public sealed record HudAlertEntry(PlayerId? Target, HudText Text, DateTimeOffset Until);

public sealed record AlertsWidget(ImmutableList<HudAlertEntry> Entries)
{
    public const int MaxVisible = 3;
    private const int MaxStored = 64;

    public static AlertsWidget Empty { get; } = new(ImmutableList<HudAlertEntry>.Empty);

    public AlertsWidget Push(PlayerId? target, HudText text, DateTimeOffset now, TimeSpan duration)
    {
        var live = Entries.RemoveAll(e => e.Until <= now).Add(new HudAlertEntry(target, text, now + duration));
        return new AlertsWidget(live.Count > MaxStored ? live.RemoveRange(0, live.Count - MaxStored) : live);
    }

    public IReadOnlyList<HudLine> Render(PlayerId player, DateTimeOffset now) =>
        Entries
            .Where(e => e.Until > now && (e.Target is null || e.Target == player))
            .TakeLast(MaxVisible)
            .Select(e => new HudLine(e.Text, HudStyle.Accent))
            .ToList();
}
