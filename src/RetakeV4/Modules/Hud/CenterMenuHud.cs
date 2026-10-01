using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using RetakeV4.Domain.Common;
using RetakeV4.Domain.Events;
using RetakeV4.Domain.Hud;
using RetakeV4.Domain.Rounds;
using RetakeV4.Localization;

namespace RetakeV4.Modules.Hud;

// Game thread only. Menus drawn in the center HTML panel (it replaces the info block while open): W/S move, E chooses, 1-9 too.
internal sealed class CenterMenuHud
{
    private static readonly TimeSpan SlotKeyMute = TimeSpan.FromSeconds(1);

    private readonly HudConfig _config;
    private readonly ITextService _text;
    private readonly IEventBus _bus;
    private readonly Func<RoundPhase> _phase;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Dictionary<int, MenuNavigator> _open = new();
    private KeyMute _keyMute = KeyMute.Empty;

    public CenterMenuHud(HudConfig config, ITextService text, IEventBus bus, Func<RoundPhase> phase, Func<DateTimeOffset> clock)
    {
        _config = config;
        _text = text;
        _bus = bus;
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
        }
    }

    public void OnClose(HudMenuClose e)
    {
        if (_open.TryGetValue(e.Player.Slot, out var current) && current.Root.Id == e.MenuId)
        {
            _open.Remove(e.Player.Slot);
        }
    }

    // Right after a loadout the game may echo the number key used to pick a weapon: it must not choose a menu line.
    public void OnLoadoutApplied(LoadoutApplied e) => _keyMute = _keyMute.Mute(e.Player.Slot, _clock(), SlotKeyMute);

    public void Forget(int slot)
    {
        _open.Remove(slot);
        _keyMute = _keyMute.Forget(slot);
    }

    public void CloseAll() => _open.Clear();

    public MenuNavigator? OpenNavigator(int slot) => _open.GetValueOrDefault(slot);

    public string? KeyRefusal(CCSPlayerController? player, int key)
    {
        if (player is not { IsValid: true })
        {
            return "invalid player";
        }
        if (OpenNavigator(player.Slot) is not { } navigator)
        {
            return "no open menu";
        }
        if (key > navigator.Lines().Count)
        {
            return $"key beyond the {navigator.Lines().Count} lines";
        }
        return _keyMute.IsMuted(player.Slot, _clock()) ? "muted right after a loadout" : null;
    }

    public MenuNavigator? ClaimKey(CCSPlayerController? player, int key) =>
        KeyRefusal(player, key) is null ? OpenNavigator(player!.Slot) : null;

    public void PressKey(int slot, int key, MenuNavigator claimed)
    {
        if (Current(slot, claimed) is { } navigator)
        {
            Apply(slot, navigator.Activate(key - 1));
        }
    }

    public void OnButtons(int slot, PlayerButtons pressed, MenuNavigator claimed)
    {
        if (Current(slot, claimed) is not { } navigator
            || Utilities.GetPlayerFromSlot(slot) is not { IsValid: true } player || !Controls(player).Movement)
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
        var movement = Controls(player).Movement;
        var rows = navigator.Lines()
            .Select((line, index) => new CenterMenuRow(MenuLineLabel.Format(_text, player, line), movement && index == navigator.Cursor))
            .ToList();
        var hint = _text.For(player, movement ? "hud.menu.hint_move" : "hud.menu.hint_keys");
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

    // A menu opened, closed or moved since the input was claimed wins: the input is dropped.
    private MenuNavigator? Current(int slot, MenuNavigator claimed) =>
        _open.TryGetValue(slot, out var navigator) && ReferenceEquals(navigator, claimed) ? navigator : null;

    private MenuControls Controls(CCSPlayerController player) => MenuInput.For(_phase(), player.PawnIsAlive, _config.Menu.Input);

    private static bool Pressed(PlayerButtons pressed, PlayerButtons button) => (pressed & button) != 0;
}
