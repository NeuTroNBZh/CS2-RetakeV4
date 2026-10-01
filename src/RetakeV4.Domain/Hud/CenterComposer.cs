namespace RetakeV4.Domain.Hud;

public static class CenterComposer
{
    public static IReadOnlyList<HudLine> Compose(IEnumerable<(int Priority, IReadOnlyList<HudLine> Lines)> blocks, int maxLines) =>
        blocks
            .Where(b => b.Lines.Count > 0)
            .OrderByDescending(b => b.Priority)
            .SelectMany(b => b.Lines)
            .Take(Math.Max(0, maxLines))
            .ToList();
}
