using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Menu;
using RetakeV4.Domain.Common;
using RetakeV4.Domain.Events;
using RetakeV4.Domain.Hud;
using RetakeV4.Localization;

namespace RetakeV4.Modules.Hud;

// The V3 way: each menu level is a CounterStrikeSharp chat menu, chosen with !1, !2... Same Domain menus as the HUD.
internal sealed class ChatMenuHud
{
    private readonly ITextService _text;
    private readonly IEventBus _bus;
    private readonly Action<string, Action> _nextFrame;
    private readonly Dictionary<int, MenuNavigator> _open = new();
    private readonly Dictionary<int, ChatMenu> _shown = new();

    public ChatMenuHud(ITextService text, IEventBus bus, Action<string, Action> nextFrame)
    {
        _text = text;
        _bus = bus;
        _nextFrame = nextFrame;
    }

    // A refresh only updates what the next printed level shows: reprinting would flood the chat.
    public void OnOpen(HudMenuOpen e)
    {
        var slot = e.Player.Slot;
        if (e.RefreshOnly)
        {
            if (_open.TryGetValue(slot, out var current) && current.Root.Id == e.Menu.Id)
            {
                _open[slot] = current.Replace(e.Menu);
            }
            return;
        }
        _open[slot] = MenuNavigator.Open(e.Menu);
        Show(slot);
    }

    public void OnClose(HudMenuClose e)
    {
        if (_open.TryGetValue(e.Player.Slot, out var current) && current.Root.Id == e.MenuId)
        {
            Close(e.Player.Slot);
        }
    }

    public void Forget(int slot)
    {
        _open.Remove(slot);
        _shown.Remove(slot);
    }

    public void CloseAll()
    {
        foreach (var slot in _open.Keys.ToList())
        {
            Close(slot);
        }
    }

    private void Close(int slot)
    {
        _open.Remove(slot);
        _shown.Remove(slot, out var shown);
        // Only our own menu is closed: another plugin's chat menu may be the active one.
        if (Utilities.GetPlayerFromSlot(slot) is { IsValid: true } player
            && MenuManager.GetActiveMenu(player) is BaseMenuInstance { Menu: var active } && ReferenceEquals(active, shown))
        {
            MenuManager.CloseActiveMenu(player);
        }
    }

    // Every item of the level is listed: the chat menu paginates by itself (6 per page with its own next/previous).
    private void Show(int slot)
    {
        if (Utilities.GetPlayerFromSlot(slot) is not { IsValid: true } player || !_open.TryGetValue(slot, out var navigator))
        {
            Forget(slot);
            return;
        }
        var menu = new ChatMenu(HudTextFormatter.Format(_text, player, navigator.Current.Title))
        {
            ExitButton = false,
            PostSelectAction = PostSelectAction.Close,
        };
        var rootId = navigator.Root.Id;
        foreach (var line in navigator.AllLines())
        {
            var lineId = line.Id;
            // Chat commands are processed with the client's messages: the choice runs on the next frame.
            menu.AddMenuOption(MenuLineLabel.Format(_text, player, line), (_, _) => _nextFrame("chat_menu", () => Choose(slot, rootId, lineId)), false);
        }
        _shown[slot] = menu;
        MenuManager.OpenChatMenu(player, menu);
    }

    // Resolved by line id against the current state, so a refresh between print and choice still applies the choice.
    private void Choose(int slot, string rootId, string lineId)
    {
        if (!_open.TryGetValue(slot, out var current) || current.Root.Id != rootId)
        {
            return;
        }
        var (next, outcome) = current.Choose(lineId);
        if (outcome is { Kind: MenuOutcomeKind.Selected, ItemId: { } itemId })
        {
            _bus.Publish(new HudMenuSelected(new PlayerId(slot), rootId, itemId));
        }
        if (!ChatMenuFlow.ShowsNextLevel(outcome))
        {
            Forget(slot);
            return;
        }
        _open[slot] = next;
        Show(slot);
    }
}
