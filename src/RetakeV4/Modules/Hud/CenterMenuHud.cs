using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using Microsoft.Extensions.Logging;
using RetakeV4.Domain.Common;
using RetakeV4.Domain.Events;
using RetakeV4.Domain.Hud;
using RetakeV4.Localization;

namespace RetakeV4.Modules.Hud;

// Game thread only. Menus drawn in the center HTML panel (it replaces the info block while open). The whole level is listed and
// the panel scrolls (CenterVisibleLines); forward/back move the cursor, use (E) chooses, in every phase.
internal sealed class CenterMenuHud
{
    private const string ChosenMark = "✔ ";
    private static readonly TimeSpan NoticeDuration = TimeSpan.FromSeconds(4);

    private readonly HudConfig _config;
    private readonly ITextService _text;
    private readonly IEventBus _bus;
    private readonly ILogger _logger;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Dictionary<int, MenuNavigator> _open = new();
    private readonly Dictionary<int, int> _cursor = new();
    private readonly Dictionary<int, ulong> _held = new();
    private readonly Dictionary<int, (HudText Text, DateTimeOffset Until)> _notices = new();
    private readonly HashSet<int> _frozen = new();

    public CenterMenuHud(HudConfig config, ITextService text, IEventBus bus, ILogger logger, Func<DateTimeOffset> clock)
    {
        _config = config;
        _text = text;
        _bus = bus;
        _logger = logger;
        _clock = clock;
    }

    public void OnOpen(HudMenuOpen e)
    {
        var slot = e.Player.Slot;
        if (_open.TryGetValue(slot, out var current) && current.Root.Id == e.Menu.Id)
        {
            var replaced = current.Replace(e.Menu);
            _open[slot] = replaced;
            _cursor[slot] = Math.Clamp(_cursor.GetValueOrDefault(slot), 0, replaced.AllLines().Count - 1);
        }
        else if (!e.RefreshOnly)
        {
            _open[slot] = MenuNavigator.Open(e.Menu);
            _cursor[slot] = 0;
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
        Unfreeze(slot);
        _open.Remove(slot);
        _cursor.Remove(slot);
        _held.Remove(slot);
        _notices.Remove(slot);
    }

    public void CloseAll()
    {
        foreach (var slot in _frozen.ToList())
        {
            Unfreeze(slot);
        }
        _open.Clear();
        _cursor.Clear();
        _held.Clear();
        _notices.Clear();
    }

    // Every tick: CS2 sends no slot commands for the number keys and OnPlayerButtonsChanged does not fire on all servers,
    // so the held buttons are read directly.
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
            Freeze(player);
            var pressed = (PlayerButtons)ButtonEdges.Pressed(_held.GetValueOrDefault(slot), held);
            _held[slot] = held;
            if (pressed != 0 && _config.Debug)
            {
                _logger.LogInformation("Menu input: buttons {Pressed} pressed by slot {Slot}", pressed, slot);
            }
            Handle(slot, pressed);
        }
    }

    // Called every tick by the center HUD: the open menu wins over the info block.
    public string? Html(CCSPlayerController player)
    {
        if (OpenNavigator(player.Slot) is not { } navigator)
        {
            return null;
        }
        var cursor = _cursor.GetValueOrDefault(player.Slot);
        var rows = navigator.AllLines()
            .Select((line, index) => new CenterMenuRow(MenuLineLabel.Format(_text, player, line, ChosenMark), index == cursor, TeamColor(line.Team)))
            .ToList();
        var notice = _notices.TryGetValue(player.Slot, out var shown) && shown.Until > _clock()
            ? HudTextFormatter.Format(_text, player, shown.Text)
            : null;
        return CenterMenuHtml.Format(HudTextFormatter.Format(_text, player, navigator.Current.Title), rows,
            _text.For(player, "hud.menu.hint_move"), _config.Theme.ToTheme(), notice, _config.Menu.CenterVisibleLines);
    }

    public MenuNavigator? OpenNavigator(int slot) => _open.GetValueOrDefault(slot);

    // Forward / back move the cursor: like MenuManager, the pawn's speed is held at 0 every tick while the menu is open.
    private void Freeze(CCSPlayerController player)
    {
        if (_config.Menu.FreezeWhileOpen && player.PlayerPawn.Value is { IsValid: true } pawn)
        {
            pawn.VelocityModifier = 0f;
            _frozen.Add(player.Slot);
        }
    }

    private void Unfreeze(int slot)
    {
        if (_frozen.Remove(slot) && Utilities.GetPlayerFromSlot(slot) is { IsValid: true } player
            && player.PlayerPawn.Value is { IsValid: true } pawn)
        {
            pawn.VelocityModifier = 1f;
        }
    }

    // Reading the buttons needs a pawn (none while choosing a team or connecting): no pawn, no input this tick.
    private static ulong? HeldButtons(CCSPlayerController player) =>
        player.Pawn is { IsValid: true } pawn && pawn.Value is { IsValid: true } ? (ulong)player.Buttons : null;

    private void Handle(int slot, PlayerButtons pressed)
    {
        if (!_open.TryGetValue(slot, out var navigator))
        {
            return;
        }
        var count = navigator.AllLines().Count;
        var cursor = _cursor.GetValueOrDefault(slot);
        if (Pressed(pressed, PlayerButtons.Forward))
        {
            _cursor[slot] = (cursor - 1 + count) % count;
        }
        else if (Pressed(pressed, PlayerButtons.Back))
        {
            _cursor[slot] = (cursor + 1) % count;
        }
        else if (Pressed(pressed, PlayerButtons.Use))
        {
            Choose(slot, navigator, navigator.AllLines()[Math.Clamp(cursor, 0, count - 1)].Id);
        }
    }

    // Menu opens and refreshes from the selection handler arrive on a later frame (HudModule defers them).
    private void Choose(int slot, MenuNavigator navigator, string lineId)
    {
        var (next, outcome) = navigator.Choose(lineId);
        if (outcome is { Kind: MenuOutcomeKind.Selected, ItemId: { } itemId })
        {
            _bus.Publish(new HudMenuSelected(new PlayerId(slot), navigator.Root.Id, itemId));
        }
        if (outcome.Kind == MenuOutcomeKind.Closed)
        {
            Forget(slot);
            return;
        }
        _open[slot] = next;
        _cursor[slot] = CursorAfter(navigator, next, _cursor.GetValueOrDefault(slot));
    }

    // Same level: the cursor stays. Back to a parent (back line, or a choice in a submenu): on the entry that was left. Deeper: top.
    private static int CursorAfter(MenuNavigator before, MenuNavigator after, int cursor)
    {
        if (after.Path.Count == before.Path.Count)
        {
            return Math.Clamp(cursor, 0, after.AllLines().Count - 1);
        }
        if (after.Path.Count < before.Path.Count)
        {
            var left = before.Path[after.Path.Count];
            return Math.Max(0, after.AllLines().ToList().FindIndex(l => l.Id == left));
        }
        return 0;
    }

    private string? TeamColor(TeamSide? team) => team switch
    {
        TeamSide.T => _config.Theme.TeamT,
        TeamSide.CT => _config.Theme.TeamCt,
        _ => null,
    };

    private static bool Pressed(PlayerButtons pressed, PlayerButtons button) => (pressed & button) != 0;
}
