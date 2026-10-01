using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using Microsoft.Extensions.Logging;
using RetakeV4.Domain.Common;
using RetakeV4.Domain.Events;
using RetakeV4.Domain.Hud;
using RetakeV4.Domain.Rounds;
using RetakeV4.Localization;

namespace RetakeV4.Modules.Hud;

// Game thread only. Menus drawn in the center HTML panel (it replaces the info block while open): forward/back move the cursor;
// use (E) chooses, except for a living player in a live round where E defuses and opens doors: reload (R) chooses there.
internal sealed class CenterMenuHud
{
    private const string ChosenMark = "✔ ";

    private readonly HudConfig _config;
    private readonly ITextService _text;
    private readonly IEventBus _bus;
    private static readonly TimeSpan NoticeDuration = TimeSpan.FromSeconds(4);

    private readonly ILogger _logger;
    private readonly Func<RoundPhase> _phase;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Dictionary<int, MenuNavigator> _open = new();
    private readonly Dictionary<int, ulong> _held = new();
    private readonly Dictionary<int, (HudText Text, DateTimeOffset Until)> _notices = new();

    public CenterMenuHud(HudConfig config, ITextService text, IEventBus bus, ILogger logger, Func<RoundPhase> phase, Func<DateTimeOffset> clock)
    {
        _config = config;
        _text = text;
        _bus = bus;
        _logger = logger;
        _phase = phase;
        _clock = clock;
    }

    public void OnOpen(HudMenuOpen e)
    {
        var slot = e.Player.Slot;
        if (_open.TryGetValue(slot, out var current) && current.Root.Id == e.Menu.Id)
        {
            _open[slot] = current.Replace(e.Menu);
        }
        else if (!e.RefreshOnly)
        {
            _open[slot] = MenuNavigator.Open(e.Menu);
            // A key already held when the menu opens (moving, auto-open at spawn) is not a press.
            _held[slot] = Utilities.GetPlayerFromSlot(slot) is { IsValid: true } player ? HeldButtons(player) ?? 0 : 0;
        }
    }

    // The confirmation of a choice would be hidden under the menu: it is shown inside the panel for a few seconds.
    public void OnAlert(HudAlert e)
    {
        if (e.Player is { } target)
        {
            _notices[target.Slot] = (e.Text, _clock() + NoticeDuration);
        }
    }

    public void OnClose(HudMenuClose e)
    {
        if (_open.TryGetValue(e.Player.Slot, out var current) && current.Root.Id == e.MenuId)
        {
            Forget(e.Player.Slot);
        }
    }

    public void Forget(int slot)
    {
        _open.Remove(slot);
        _held.Remove(slot);
        _notices.Remove(slot);
    }

    public void CloseAll()
    {
        _open.Clear();
        _held.Clear();
        _notices.Clear();
    }

    public MenuNavigator? OpenNavigator(int slot) => _open.GetValueOrDefault(slot);

    // Every tick: CS2 sends no slot commands for the number keys and OnPlayerButtonsChanged does not fire on all servers,
    // so the held buttons are read directly. Forward/back move the cursor, use chooses, in every phase.
    public void PollButtons()
    {
        if (_open.Count == 0)
        {
            return;
        }
        foreach (var slot in _open.Keys.ToList())
        {
            if (Utilities.GetPlayerFromSlot(slot) is not { IsValid: true } player)
            {
                Forget(slot);
                continue;
            }
            if (HeldButtons(player) is not { } held)
            {
                continue;
            }
            var pressed = (PlayerButtons)ButtonEdges.Pressed(_held.GetValueOrDefault(slot), held);
            _held[slot] = held;
            if (pressed != 0 && _config.Debug)
            {
                _logger.LogInformation("Menu input: buttons {Pressed} pressed by slot {Slot}", pressed, slot);
            }
            Handle(slot, pressed, ChooseButton(player));
        }
    }

    // Reading the buttons needs a pawn (none while choosing a team or connecting): no pawn, no input this tick.
    private static ulong? HeldButtons(CCSPlayerController player) =>
        player.Pawn is { IsValid: true } pawn && pawn.Value is { IsValid: true } ? (ulong)player.Buttons : null;

    private PlayerButtons ChooseButton(CCSPlayerController player) => LiveAndAlive(player) ? PlayerButtons.Reload : PlayerButtons.Use;

    private bool LiveAndAlive(CCSPlayerController player) => _phase() == RoundPhase.Live && player.PawnIsAlive;

    private void Handle(int slot, PlayerButtons pressed, PlayerButtons choose)
    {
        if (!_open.TryGetValue(slot, out var navigator))
        {
            return;
        }
        if (Pressed(pressed, PlayerButtons.Forward))
        {
            _open[slot] = navigator.MoveWrapping(-1);
        }
        else if (Pressed(pressed, PlayerButtons.Back))
        {
            _open[slot] = navigator.MoveWrapping(1);
        }
        else if (Pressed(pressed, choose))
        {
            Apply(slot, navigator.ActivateCursor());
        }
    }

    // Called every tick by the center HUD: the open menu wins over the info block.
    public string? Html(CCSPlayerController player)
    {
        if (OpenNavigator(player.Slot) is not { } navigator)
        {
            return null;
        }
        var rows = navigator.Lines()
            .Select((line, index) => new CenterMenuRow(MenuLineLabel.Format(_text, player, line, ChosenMark), index == navigator.Cursor, TeamColor(line.Team)))
            .ToList();
        var hint = _text.For(player, LiveAndAlive(player) ? "hud.menu.hint_live" : "hud.menu.hint_move");
        var notice = _notices.TryGetValue(player.Slot, out var shown) && shown.Until > _clock()
            ? HudTextFormatter.Format(_text, player, shown.Text)
            : null;
        return CenterMenuHtml.Format(HudTextFormatter.Format(_text, player, navigator.Current.Title), rows, hint, _config.Theme.ToTheme(), notice);
    }

    // Menu opens and refreshes from the selection handler arrive on a later frame (HudModule defers them).
    private void Apply(int slot, (MenuNavigator Next, MenuOutcome Outcome) result)
    {
        if (result.Outcome is { Kind: MenuOutcomeKind.Selected, ItemId: { } itemId })
        {
            _bus.Publish(new HudMenuSelected(new PlayerId(slot), result.Next.Root.Id, itemId));
        }
        if (result.Outcome.Kind == MenuOutcomeKind.Closed)
        {
            _open.Remove(slot);
            return;
        }
        _open[slot] = result.Next;
    }

    private string? TeamColor(TeamSide? team) => team switch
    {
        TeamSide.T => _config.Theme.TeamT,
        TeamSide.CT => _config.Theme.TeamCt,
        _ => null,
    };

    private static bool Pressed(PlayerButtons pressed, PlayerButtons button) => (pressed & button) != 0;
}
