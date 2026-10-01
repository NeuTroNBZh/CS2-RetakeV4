using RetakeV4.Domain.Common;

namespace RetakeV4.Domain.Hud;

public sealed record RoundInfo(string RoundType, BombSite? Site, int CtCount, int TCount);

public sealed record RoundInfoWidget(RoundInfo? Info, DateTimeOffset ShownUntil, int TWinStreak)
{
    public static RoundInfoWidget Empty { get; } = new(null, DateTimeOffset.MinValue, 0);

    public RoundInfoWidget Show(RoundInfo info, DateTimeOffset now, TimeSpan duration) =>
        this with { Info = info, ShownUntil = now + duration };

    public RoundInfoWidget WithStreak(int tWinStreak) => this with { TWinStreak = tWinStreak };

    public IReadOnlyList<HudLine> Render(DateTimeOffset now)
    {
        if (Info is null || now >= ShownUntil)
        {
            return Array.Empty<HudLine>();
        }
        var lines = new List<HudLine>
        {
            new(HudText.Of("hud.round.title", Info.RoundType, Info.Site?.ToString() ?? "?"), HudStyle.Accent),
            new(HudText.Of("hud.round.teams", Info.CtCount, Info.TCount), HudStyle.Text),
        };
        if (TWinStreak > 0)
        {
            lines.Add(new HudLine(HudText.Of("hud.round.streak", TWinStreak), HudStyle.Muted));
        }
        return lines;
    }
}
