using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using Microsoft.Extensions.Logging;
using RetakeV4.Domain.Common;
using RetakeV4.Domain.Events;
using RetakeV4.Domain.Hud;
using RetakeV4.Localization;

namespace RetakeV4.Modules.Hud;

// Game thread only. Menus drawn in the center HTML panel (it replaces the info block while open): forward/back move, use chooses.
internal sealed class CenterMenuHud
{
    private readonly HudConfig _config;
    private readonly ITextService _text;
    private readonly IEventBus _bus;
    private readonly ILogger _logger;
    private readonly Dictionary<int, MenuNavigator> _open = new();
    private readonly Dictionary<int, ulong> _held = new();

    public CenterMenuHud(HudConfig config, ITextService text, IEventBus bus, ILogger logger)
    {
        _config = config;
        _text = text;
        _bus = bus;
        _logger = logger;
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
        }
    }

    public void OnClose(HudMenuClose e)
    {
        if (_open.TryGetValue(e.Player.Slot, out var current) && current.Root.Id == e.MenuId)
        {
            _open.Remove(e.Player.Slot);
        }
    }

    public void Forget(int slot)
    {
        _open.Remove(slot);
        _held.Remove(slot);
    }

    public void CloseAll()
    {
        _open.Clear();
        _held.Clear();
    }

    public MenuNavigator? OpenNavigator(int slot) => _open.GetValueOrDefault(slot);

    // Every tick: CS2 sends no slot commands for the number keys and OnPlayerButtonsChanged does not fire on all servers,
    // so the held buttons are read directly. Forward/back move the cursor, use chooses, in every phase.
    public void PollButtons()
    {
        foreach (var slot in _open.Keys.ToList())
        {
            if (Utilities.GetPlayerFromSlot(slot) is not { IsValid: true } player)
            {
                Forget(slot);
                continue;
            }
            var held = (ulong)player.Buttons;
            var pressed = (PlayerButtons)ButtonEdges.Pressed(_held.GetValueOrDefault(slot), held);
            _held[slot] = held;
            if (pressed != 0 && _config.Debug)
            {
                _logger.LogInformation("Menu input: buttons {Pressed} pressed by slot {Slot}", pressed, slot);
            }
            Handle(slot, pressed);
        }
    }

    private void Handle(int slot, PlayerButtons pressed)
    {
        if (!_open.TryGetValue(slot, out var navigator))
        {
            return;
        }
        if (Pressed(pressed, PlayerButtons.Forward))
        {
            _open[slot] = navigator.Move(-1);
        }
        else if (Pressed(pressed, PlayerButtons.Back))
        {
            _open[slot] = navigator.Move(1);
        }
        else if (Pressed(pressed, PlayerButtons.Use))
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
            .Select((line, index) => new CenterMenuRow(MenuLineLabel.Format(_text, player, line), index == navigator.Cursor))
            .ToList();
        var hint = _text.For(player, "hud.menu.hint_move");
        return CenterMenuHtml.Format(HudTextFormatter.Format(_text, player, navigator.Current.Title), rows, hint, _config.Theme.ToTheme());
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

    private static bool Pressed(PlayerButtons pressed, PlayerButtons button) => (pressed & button) != 0;
}
