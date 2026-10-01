using RetakeV4.Domain.Common;
using RetakeV4.Domain.Teams;

namespace RetakeV4.Domain.Hud;

public sealed record QueueStatusWidget(IReadOnlyList<QueuedPlayer> Ordered)
{
    public static QueueStatusWidget Empty { get; } = new(Array.Empty<QueuedPlayer>());

    public static QueueStatusWidget From(TeamState state) => new(state.OrderedQueue());

    public IReadOnlyList<HudLine> Render(PlayerId player)
    {
        var index = Ordered.Select(q => q.Player).ToList().IndexOf(player);
        if (index < 0)
        {
            return Array.Empty<HudLine>();
        }
        var position = new HudLine(HudText.Of("hud.queue.position", index + 1, Ordered.Count), HudStyle.Accent);
        return Ordered[index].Priority > 0
            ? new[] { position, new HudLine(HudText.Of("hud.queue.priority"), HudStyle.Muted) }
            : new[] { position };
    }
}
