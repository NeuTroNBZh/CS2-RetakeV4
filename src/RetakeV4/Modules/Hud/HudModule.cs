using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using Microsoft.Extensions.Logging;
using RetakeV4.Configuration;
using RetakeV4.Domain.Events;
using RetakeV4.Domain.Hud;

namespace RetakeV4.Modules.Hud;

public sealed class HudModule : IRetakeModule
{
    private HudConfig _config = new();
    private ModuleContext? _context;
    private MenuHud? _menus;
    private ChatMenuHud? _chatMenus;
    private CenterMenuHud? _centerMenus;
    private ChatMenuHud? _chatOnlyMenus;

    public string Name => "Hud";

    public IReadOnlyList<string> DependsOn { get; } = new[] { "Core" };

    public ModuleConfig LoadConfig(JsonConfigStore store, ILogger logger)
    {
        var result = store.Load("hud.json", new HudConfig(), new HudConfigValidator());
        ConfigLogging.Report(logger, result.Issues);
        _config = result.Config;
        return _config;
    }

    public void Load(ModuleContext context)
    {
        _context = context;
        switch (_config.Menu.Display)
        {
            case MenuDisplay.Chat:
                LoadCenter(context, null);
                LoadChatMenus(context);
                break;
            case MenuDisplay.CenterHtml:
                var centerMenus = new CenterMenuHud(_config, context.Text, context.Bus, context.Logger, () => DateTimeOffset.UtcNow);
                _centerMenus = centerMenus;
                LoadCenter(context, centerMenus.Html);
                LoadCenterMenus(context, centerMenus);
                LoadChatOnlyMenus(context);
                break;
            default:
                LoadCenter(context, null);
                LoadMenus(context);
                LoadChatOnlyMenus(context);
                break;
        }
    }

    public void Unload()
    {
        _menus?.CloseAll();
        _menus = null;
        _chatMenus?.CloseAll();
        _chatMenus = null;
        _centerMenus?.CloseAll();
        _centerMenus = null;
        _chatOnlyMenus?.CloseAll();
        _chatOnlyMenus = null;
        _context = null;
    }

    private void LoadCenter(ModuleContext context, Func<CCSPlayerController, string?>? menu)
    {
        var center = new CenterHud(_config, context.Text, () => DateTimeOffset.UtcNow, menu);
        var hooks = context.Hooks;
        hooks.OnBus<RoundPrepared>(center.OnRoundPrepared);
        hooks.OnBus<TeamStateChanged>(center.OnTeams);
        hooks.OnBus<HudAlert>(center.OnAlert);
        hooks.OnTick("center_tick", center.Tick);
    }

    // Drawn by the center HUD every tick; input goes through the same deferral as the world-text menus.
    private void LoadCenterMenus(ModuleContext context, CenterMenuHud menus)
    {
        var hooks = context.Hooks;
        hooks.OnBus<HudMenuOpen>(e =>
        {
            if (e.Menu.ChatOnly)
            {
                return;
            }
            if (_config.Debug)
            {
                context.Logger.LogInformation("Menu input: menu {Menu} opened for slot {Slot} (refresh: {Refresh})", e.Menu.Id, e.Player.Slot, e.RefreshOnly);
            }
            NextFrame("menu_open", () => _centerMenus?.OnOpen(e));
        });
        hooks.OnBus<HudMenuClose>(e => NextFrame("menu_close", () => _centerMenus?.OnClose(e)));
        hooks.OnBus<MapStarted>(_ => menus.CloseAll());
        hooks.OnTick("menu_input", menus.PollButtons);
        hooks.OnBus<HudAlert>(menus.OnAlert);
        hooks.OnEvent<EventPlayerDisconnect>("player_disconnect", e =>
        {
            if (e.Userid is { } player)
            {
                menus.Forget(player.Slot);
            }
        });
    }

    // Chat menus need none of the world-text machinery: no entities, no aim, no slot-key interception.
    private void LoadChatMenus(ModuleContext context)
    {
        var menus = new ChatMenuHud(context.Text, context.Bus, NextFrame);
        _chatMenus = menus;
        var hooks = context.Hooks;
        hooks.OnBus<HudMenuOpen>(e => NextFrame("menu_open", () => _chatMenus?.OnOpen(e)));
        hooks.OnBus<HudMenuClose>(e => NextFrame("menu_close", () => _chatMenus?.OnClose(e)));
        hooks.OnBus<MapStarted>(_ => menus.CloseAll());
        hooks.OnEvent<EventPlayerDisconnect>("player_disconnect", e =>
        {
            if (e.Userid is { } player)
            {
                menus.Forget(player.Slot);
            }
        });
    }

    // Menus flagged ChatOnly (map vote) stay in the chat whatever the configured display: the center and world-text menus
    // are driven by the movement keys and freeze the player.
    private void LoadChatOnlyMenus(ModuleContext context)
    {
        var menus = new ChatMenuHud(context.Text, context.Bus, NextFrame);
        _chatOnlyMenus = menus;
        var hooks = context.Hooks;
        hooks.OnBus<HudMenuOpen>(e =>
        {
            if (e.Menu.ChatOnly)
            {
                NextFrame("chat_only_open", () => _chatOnlyMenus?.OnOpen(e));
            }
        });
        hooks.OnBus<HudMenuClose>(e => NextFrame("chat_only_close", () => _chatOnlyMenus?.OnClose(e)));
        hooks.OnBus<MapStarted>(_ => menus.CloseAll());
        hooks.OnEvent<EventPlayerDisconnect>("chat_only_disconnect", e =>
        {
            if (e.Userid is { } player)
            {
                menus.Forget(player.Slot);
            }
        });
    }

    private void LoadMenus(ModuleContext context)
    {
        var menus = new MenuHud(_config, context.Text, context.Bus, context.Logger, () => context.Rounds.State.Phase, () => DateTimeOffset.UtcNow);
        _menus = menus;
        var hooks = context.Hooks;
        // Opening, closing and activating a menu can take long the first time (JIT, entities): run them on the next frame,
        // outside the processing of the client's messages, which the engine aborts with a kick past ~500 ms.
        hooks.OnBus<HudMenuOpen>(e =>
        {
            if (!e.Menu.ChatOnly)
            {
                NextFrame("menu_open", () => _menus?.OnOpen(e));
            }
        });
        hooks.OnBus<HudMenuClose>(e => NextFrame("menu_close", () => _menus?.OnClose(e)));
        hooks.OnBus<MapStarted>(_ => menus.Reset());
        hooks.OnTick("menu_tick", menus.Tick);
        hooks.OnCheckTransmit("menu_transmit", menus.OnCheckTransmit);
        hooks.OnTick("menu_buttons", menus.PollButtons);
        hooks.OnEvent<EventRoundPrestart>("round_prestart", _ => menus.SuspendEntities());
        hooks.OnEvent<EventRoundStart>("round_start", _ =>
            NextFrame("menu_resume", () => _menus?.ResumeEntities()));
        hooks.OnEvent<EventPlayerDisconnect>("player_disconnect", e =>
        {
            if (e.Userid is { } player)
            {
                menus.Forget(player.Slot);
            }
        });
    }

    private void NextFrame(string stage, Action action) =>
        Server.NextFrame(() => _context?.Guard.Run(Name, stage, action));
}
