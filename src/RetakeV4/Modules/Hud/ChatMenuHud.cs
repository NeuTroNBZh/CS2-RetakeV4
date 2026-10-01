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

    public void Forget(int slot) => _open.Remove(slot);

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
        if (Utilities.GetPlayerFromSlot(slot) is { IsValid: true } player)
        {
            MenuManager.CloseActiveMenu(player);
        }
    }

    private void Show(int slot)
    {
        if (Utilities.GetPlayerFromSlot(slot) is not { IsValid: true } player || !_open.TryGetValue(slot, out var navigator))
        {
            _open.Remove(slot);
            return;
        }
        var menu = new ChatMenu(HudTextFormatter.Format(_text, player, navigator.Current.Title))
        {
            ExitButton = false,
            PostSelectAction = PostSelectAction.Close,
        };
        var lines = navigator.Lines();
        for (var index = 0; index < lines.Count; index++)
        {
            var lineIndex = index;
            // Chat commands are processed with the client's messages: the choice runs on the next frame.
            menu.AddMenuOption(MenuLineLabel.Format(_text, player, lines[index]),
                (_, _) => _nextFrame("chat_menu", () => Choose(slot, navigator, lineIndex)), false);
        }
        MenuManager.OpenChatMenu(player, menu);
    }

    private void Choose(int slot, MenuNavigator shown, int lineIndex)
    {
        // A menu opened, closed or refreshed since this one was printed wins.
        if (!_open.TryGetValue(slot, out var current) || !ReferenceEquals(current, shown))
        {
            return;
        }
        var (next, outcome) = current.Activate(lineIndex);
        if (outcome is { Kind: MenuOutcomeKind.Selected, ItemId: { } itemId })
        {
            _bus.Publish(new HudMenuSelected(new PlayerId(slot), current.Root.Id, itemId));
        }
        if (!ChatMenuFlow.ShowsNextLevel(outcome))
        {
            _open.Remove(slot);
            return;
        }
        _open[slot] = next;
        Show(slot);
    }
}
