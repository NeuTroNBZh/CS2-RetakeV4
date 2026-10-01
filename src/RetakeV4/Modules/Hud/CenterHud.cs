using CounterStrikeSharp.API.Core;
using RetakeV4.Adapters;
using RetakeV4.Domain.Common;
using RetakeV4.Domain.Events;
using RetakeV4.Domain.Hud;
using RetakeV4.Domain.Teams;
using RetakeV4.Localization;

namespace RetakeV4.Modules.Hud;

// Game thread only. Widgets are recomputed every CenterRefreshMs; the last HTML is re-sent every tick because center HTML fades quickly.
internal sealed class CenterHud
{
    private const int AlertsPriority = 30;
    private const int QueuePriority = 20;
    private const int RoundInfoPriority = 10;

    private readonly HudConfig _config;
    private readonly ITextService _text;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Func<CCSPlayerController, string?> _menu;
    private readonly Dictionary<int, string> _html = new();
    private RoundInfoWidget _roundInfo = RoundInfoWidget.Empty;
    private QueueStatusWidget _queue = QueueStatusWidget.Empty;
    private AlertsWidget _alerts = AlertsWidget.Empty;
    private TeamState _teams = TeamState.Empty;
    private DateTimeOffset _nextRefresh = DateTimeOffset.MinValue;

    // menu: the HTML of an open center menu for a player, shown instead of the info block (null when none).
    public CenterHud(HudConfig config, ITextService text, Func<DateTimeOffset> clock, Func<CCSPlayerController, string?>? menu = null)
    {
        _config = config;
        _text = text;
        _clock = clock;
        _menu = menu ?? (_ => null);
    }

    public void OnRoundPrepared(RoundPrepared e)
    {
        var info = new RoundInfo(e.Context.RoundType ?? "?", e.Context.Site, _teams.Ct.Count, _teams.T.Count);
        _roundInfo = _roundInfo.Show(info, _clock(), TimeSpan.FromSeconds(_config.Widgets.RoundInfo.ShowSeconds));
        _nextRefresh = DateTimeOffset.MinValue;
    }

    public void OnTeams(TeamStateChanged e)
    {
        _teams = e.State;
        _queue = QueueStatusWidget.From(e.State);
        _roundInfo = _roundInfo.WithStreak(e.State.TWinStreak);
        _nextRefresh = DateTimeOffset.MinValue;
    }

    public void OnAlert(HudAlert e)
    {
        _alerts = _alerts.Push(e.Player, e.Text, _clock(), TimeSpan.FromSeconds(_config.Widgets.Alerts.ShowSeconds));
        _nextRefresh = DateTimeOffset.MinValue;
    }

    public void Tick()
    {
        var now = _clock();
        var players = PlayerQueries.Humans();
        if (now >= _nextRefresh)
        {
            Refresh(players, now);
            _nextRefresh = now + TimeSpan.FromMilliseconds(_config.CenterRefreshMs);
        }
        foreach (var player in players)
        {
            if (_menu(player) is { } menu)
            {
                player.PrintToCenterHtml(menu);
            }
            else if (_html.TryGetValue(player.Slot, out var html))
            {
                player.PrintToCenterHtml(html);
            }
        }
    }

    private void Refresh(IReadOnlyList<CCSPlayerController> players, DateTimeOffset now)
    {
        _html.Clear();
        foreach (var player in players)
        {
            if (Compose(player, now) is { } html)
            {
                _html[player.Slot] = html;
            }
        }
    }

    private string? Compose(CCSPlayerController player, DateTimeOffset now)
    {
        var id = new PlayerId(player.Slot);
        var widgets = _config.Widgets;
        var blocks = new List<(int Priority, IReadOnlyList<HudLine> Lines)>();
        if (widgets.Alerts.Enabled)
        {
            blocks.Add((AlertsPriority, _alerts.Render(id, now)));
        }
        if (widgets.QueueStatus.Enabled)
        {
            blocks.Add((QueuePriority, _queue.Render(id)));
        }
        if (widgets.RoundInfo.Enabled)
        {
            blocks.Add((RoundInfoPriority, _roundInfo.Render(now)));
        }
        var lines = CenterComposer.Compose(blocks, _config.CenterMaxLines)
            .Select(l => (HudTextFormatter.Format(_text, player, l.Text), l.Style))
            .ToList();
        return CenterHtml.Format(lines, _config.Theme.ToTheme());
    }
}
