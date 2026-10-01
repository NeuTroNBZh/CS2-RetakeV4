using System.Drawing;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using Microsoft.Extensions.Logging;
using RetakeV4.Adapters;
using RetakeV4.Domain.Common;
using RetakeV4.Domain.Events;
using RetakeV4.Domain.Hud;
using RetakeV4.Domain.Rounds;
using RetakeV4.Localization;

namespace RetakeV4.Modules.Hud;

// Game thread only. One session per player slot. Entities are never created between round_prestart and the frame after
// round_start (CS2 cleans the map in between): sessions survive and are rendered again when the window reopens.
internal sealed class MenuHud
{
    private static readonly TimeSpan SlotKeyMute = TimeSpan.FromSeconds(1);

    private readonly HudConfig _config;
    private readonly ITextService _text;
    private readonly IEventBus _bus;
    private readonly ILogger _logger;
    private readonly Func<RoundPhase> _phase;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Color _accent;
    private readonly Color _normal;
    private readonly Color _muted;
    private readonly Dictionary<int, MenuSession> _sessions = new();
    private bool _entitiesAllowed = true;
    private KeyMute _keyMute = KeyMute.Empty;

    public MenuHud(HudConfig config, ITextService text, IEventBus bus, ILogger logger, Func<RoundPhase> phase, Func<DateTimeOffset> clock)
    {
        _config = config;
        _text = text;
        _bus = bus;
        _logger = logger;
        _phase = phase;
        _clock = clock;
        _accent = ColorTranslator.FromHtml(config.Theme.Accent);
        _normal = ColorTranslator.FromHtml(config.Theme.Text);
        _muted = ColorTranslator.FromHtml(config.Theme.Muted);
    }

    public void OnOpen(HudMenuOpen e)
    {
        var slot = e.Player.Slot;
        if (_sessions.TryGetValue(slot, out var session) && session.Navigator.Root.Id == e.Menu.Id)
        {
            session.Navigator = session.Navigator.Replace(e.Menu);
        }
        else if (e.RefreshOnly)
        {
            return;
        }
        else
        {
            Close(slot);
            _sessions[slot] = new MenuSession(MenuNavigator.Open(e.Menu));
        }
        Render(slot);
    }

    public void OnLoadoutApplied(LoadoutApplied e) => _keyMute = _keyMute.Mute(e.Player.Slot, _clock(), SlotKeyMute);

    public void Forget(int slot)
    {
        Close(slot);
        _keyMute = _keyMute.Forget(slot);
    }

    public void OnClose(HudMenuClose e)
    {
        if (_sessions.TryGetValue(e.Player.Slot, out var session) && session.Navigator.Root.Id == e.MenuId)
        {
            Close(e.Player.Slot);
        }
    }

    public void Close(int slot)
    {
        if (_sessions.Remove(slot, out var session))
        {
            session.Detach();
        }
    }

    public void CloseAll()
    {
        foreach (var slot in _sessions.Keys.ToList())
        {
            Close(slot);
        }
    }

    public void Reset()
    {
        CloseAll();
        _entitiesAllowed = true;
    }

    public void SuspendEntities()
    {
        _entitiesAllowed = false;
        foreach (var session in _sessions.Values)
        {
            session.Detach();
        }
    }

    public void ResumeEntities()
    {
        _entitiesAllowed = true;
        foreach (var slot in _sessions.Keys.ToList())
        {
            Render(slot);
        }
    }

    public void Tick()
    {
        foreach (var (slot, session) in _sessions.ToList())
        {
            var player = Utilities.GetPlayerFromSlot(slot);
            if (player is not { IsValid: true })
            {
                Close(slot);
            }
            else if (session.View is null)
            {
                Render(slot);
            }
            else
            {
                Follow(player, session);
            }
        }
    }

    public void OnButtons(int slot, PlayerButtons pressed, MenuNavigator claimed)
    {
        if (Current(slot, claimed) is not var (player, session))
        {
            Trace("buttons {Pressed} of slot {Slot} dropped: the menu changed or closed since the press", pressed, slot);
            return;
        }
        var controls = Controls(player);
        Trace("buttons {Pressed} of slot {Slot}: controls {Controls}, aimed line {Aimed}", pressed, slot, controls, session.Aimed);
        if (controls.Aim && Pressed(pressed, PlayerButtons.Attack) && session.Aimed is { } aimed)
        {
            Activate(player, session, aimed);
        }
        else if (controls.Movement && Pressed(pressed, PlayerButtons.Forward))
        {
            Move(player, session, -1);
        }
        else if (controls.Movement && Pressed(pressed, PlayerButtons.Back))
        {
            Move(player, session, 1);
        }
        else if (controls.Movement && Pressed(pressed, PlayerButtons.Use))
        {
            Activate(player, session, session.Navigator.Cursor);
        }
    }

    // Input is claimed while the client's messages are processed and applied on the next frame: the navigator seen at claim
    // time identifies the menu, so an input never lands on a menu opened, closed or moved in between.
    public MenuNavigator? OpenNavigator(int slot) =>
        _sessions.TryGetValue(slot, out var session) && session.View is not null ? session.Navigator : null;

    public MenuNavigator? ClaimKey(CCSPlayerController? player, int key) =>
        KeyRefusal(player, key) is null ? OpenNavigator(player!.Slot) : null;

    // Diagnostic (hud.json Debug): why a number key is left to the game instead of driving the menu.
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
        if (!Controls(player).Keys)
        {
            return "keys disabled in this phase";
        }
        if (key > navigator.Lines().Count)
        {
            return $"key beyond the {navigator.Lines().Count} lines";
        }
        return _keyMute.IsMuted(player.Slot, _clock()) ? "muted right after a loadout" : null;
    }

    public void PressKey(int slot, int key, MenuNavigator claimed)
    {
        if (Current(slot, claimed) is not var (player, session) || !Controls(player).Keys)
        {
            Trace("key {Key} of slot {Slot} dropped: the menu changed or closed since the press", key, slot);
            return;
        }
        Activate(player, session, key - 1);
    }

    private (CCSPlayerController Player, MenuSession Session)? Current(int slot, MenuNavigator claimed)
    {
        var player = Utilities.GetPlayerFromSlot(slot);
        return player is { IsValid: true } && _sessions.TryGetValue(slot, out var session) && session.View is not null
            && ReferenceEquals(session.Navigator, claimed)
                ? (player, session)
                : null;
    }

    public void OnCheckTransmit(CCheckTransmitInfoList infoList)
    {
        if (_sessions.Count == 0)
        {
            return;
        }
        var owned = _sessions
            .Where(s => s.Value.View is not null)
            .Select(s => (Slot: s.Key, Entities: s.Value.View!.Entities))
            .ToList();
        foreach ((CCheckTransmitInfo info, CCSPlayerController? viewer) in infoList)
        {
            if (viewer is null)
            {
                continue;
            }
            foreach (var (_, entities) in owned.Where(o => o.Slot != viewer.Slot))
            {
                Hide(info, entities);
            }
        }
    }

    private static void Hide(CCheckTransmitInfo info, IReadOnlyList<CPointWorldText> entities)
    {
        foreach (var entity in entities.Where(e => e.IsValid))
        {
            info.TransmitEntities.Remove(entity);
        }
    }

    private void Render(int slot)
    {
        if (!_entitiesAllowed || !_sessions.TryGetValue(slot, out var session))
        {
            return;
        }
        var player = Utilities.GetPlayerFromSlot(slot);
        if (player is not { IsValid: true } || PlayerView.Of(player) is not { } view)
        {
            return;
        }
        session.Opened ??= view.Angles;
        session.View ??= new WorldTextMenuView(_config);
        if (!session.View.Show(Lines(player, session)))
        {
            _logger.LogWarning("Could not create HUD menu entities for slot {Slot}; closing the menu", slot);
            Close(slot);
            return;
        }
        session.View.Position(view, session.Opened.Value, Layout(session));
    }

    private void Follow(CCSPlayerController player, MenuSession session)
    {
        if (session.View is null || session.Opened is not { } opened || PlayerView.Of(player) is not { } view)
        {
            return;
        }
        if (session.View.Entities.Any(e => !e.IsValid))
        {
            // Removed behind our back (other plugin, ent_remove, parent gone): rebuild next tick instead of failing every tick.
            session.Detach();
            return;
        }
        var layout = Layout(session);
        if (_config.Menu.FollowMode == MenuFollowMode.Tick)
        {
            session.View.Position(view, opened, layout);
        }
        var aimed = Controls(player).Aim ? AimLineResolver.Resolve(layout, opened, view.Angles) : null;
        if (aimed != session.Aimed)
        {
            session.Aimed = aimed;
            session.View.Show(Lines(player, session));
        }
        if (aimed is not null && _config.Menu.BlockAttackWhileAiming)
        {
            PlayerView.DelayAttack(player);
        }
    }

    private void Move(CCSPlayerController player, MenuSession session, int delta)
    {
        session.Navigator = session.Navigator.Move(delta);
        session.View?.Show(Lines(player, session));
    }

    // The navigation is updated and shown before the selection is published: the owner may refresh the menu in its handler.
    private void Trace(string message, params object?[] args)
    {
        if (_config.Debug)
        {
#pragma warning disable CA2254 // Templates are constant strings passed by this class only.
            _logger.LogInformation("Menu input: " + message, args);
#pragma warning restore CA2254
        }
    }

    private void Activate(CCSPlayerController player, MenuSession session, int lineIndex)
    {
        Trace("slot {Slot} activates line {Line} of {Menu}", player.Slot, lineIndex, session.Navigator.Current.Id);
        var menuId = session.Navigator.Root.Id;
        var (next, outcome) = session.Navigator.Activate(lineIndex);
        session.Navigator = next;
        if (outcome.Kind == MenuOutcomeKind.Closed)
        {
            Close(player.Slot);
            return;
        }
        Render(player.Slot);
        if (outcome is { Kind: MenuOutcomeKind.Selected, ItemId: { } itemId })
        {
            _bus.Publish(new HudMenuSelected(new PlayerId(player.Slot), menuId, itemId));
        }
    }

    private MenuControls Controls(CCSPlayerController player) => MenuInput.For(_phase(), player.PawnIsAlive, _config.Menu.Input);

    private AimMenuLayout Layout(MenuSession session) => AimMenuLayout.Centered(
        session.Navigator.Lines().Count, _config.Menu.DistanceUnits, _config.Menu.LineHeightUnits, _config.Menu.HalfWidthUnits);

    private IReadOnlyList<WorldTextLine> Lines(CCSPlayerController player, MenuSession session)
    {
        var highlighted = session.Aimed ?? session.Navigator.Cursor;
        var title = new WorldTextLine(HudTextFormatter.Format(_text, player, session.Navigator.Current.Title), _muted);
        return session.Navigator.Lines()
            .Select((line, index) => new WorldTextLine($"{index + 1}. {Label(player, line)}", index == highlighted ? _accent : _normal))
            .Prepend(title)
            .ToList();
    }

    private string Label(CCSPlayerController player, MenuLine line) => MenuLineLabel.Format(_text, player, line);

    private static bool Pressed(PlayerButtons pressed, PlayerButtons button) => (pressed & button) != 0;
}
