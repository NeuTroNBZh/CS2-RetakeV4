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
        LoadCenter(context);
        if (_config.Menu.Display == MenuDisplay.Chat)
        {
            LoadChatMenus(context);
        }
        else
        {
            LoadMenus(context);
        }
    }

    public void Unload()
    {
        _menus?.CloseAll();
        _menus = null;
        _chatMenus?.CloseAll();
        _chatMenus = null;
        _context = null;
    }

    private void LoadCenter(ModuleContext context)
    {
        var center = new CenterHud(_config, context.Text, () => DateTimeOffset.UtcNow);
        var hooks = context.Hooks;
        hooks.OnBus<RoundPrepared>(center.OnRoundPrepared);
        hooks.OnBus<TeamStateChanged>(center.OnTeams);
        hooks.OnBus<HudAlert>(center.OnAlert);
        hooks.OnTick("center_tick", center.Tick);
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

    private void LoadMenus(ModuleContext context)
    {
        var menus = new MenuHud(_config, context.Text, context.Bus, context.Logger, () => context.Rounds.State.Phase, () => DateTimeOffset.UtcNow);
        _menus = menus;
        var hooks = context.Hooks;
        // Opening, closing and activating a menu can take long the first time (JIT, entities): run them on the next frame,
        // outside the processing of the client's messages, which the engine aborts with a kick past ~500 ms.
        hooks.OnBus<HudMenuOpen>(e => NextFrame("menu_open", () => _menus?.OnOpen(e)));
        hooks.OnBus<HudMenuClose>(e => NextFrame("menu_close", () => _menus?.OnClose(e)));
        hooks.OnBus<LoadoutApplied>(menus.OnLoadoutApplied);
        hooks.OnBus<MapStarted>(_ => menus.Reset());
        hooks.OnTick("menu_tick", menus.Tick);
        hooks.OnCheckTransmit("menu_transmit", menus.OnCheckTransmit);
        hooks.OnPlayerButtons("menu_buttons", (player, pressed, _) =>
        {
            var slot = player.Slot;
            if (menus.OpenNavigator(slot) is { } claimed)
            {
                NextFrame("menu_buttons", () => _menus?.OnButtons(slot, pressed, claimed));
            }
        });
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
        for (var key = 1; key <= MenuNavigator.MaxLines; key++)
        {
            var slotKey = key;
            hooks.CommandListener($"slot{slotKey}", (player, _) =>
            {
                if (menus.ClaimKey(player, slotKey) is not { } claimed)
                {
                    return HookResult.Continue;
                }
                var slot = player!.Slot;
                NextFrame("menu_key", () => _menus?.PressKey(slot, slotKey, claimed));
                return HookResult.Handled;
            }, HookMode.Pre);
        }
    }

    private void NextFrame(string stage, Action action) =>
        Server.NextFrame(() => _context?.Guard.Run(Name, stage, action));
}
