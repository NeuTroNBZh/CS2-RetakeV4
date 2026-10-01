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
        LoadMenus(context);
    }

    public void Unload()
    {
        _menus?.CloseAll();
        _menus = null;
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

    private void LoadMenus(ModuleContext context)
    {
        var menus = new MenuHud(_config, context.Text, context.Bus, context.Logger, () => context.Rounds.State.Phase);
        _menus = menus;
        var hooks = context.Hooks;
        hooks.OnBus<HudMenuOpen>(menus.OnOpen);
        hooks.OnBus<HudMenuClose>(menus.OnClose);
        hooks.OnBus<MapStarted>(_ => menus.Reset());
        hooks.OnTick("menu_tick", menus.Tick);
        hooks.OnCheckTransmit("menu_transmit", menus.OnCheckTransmit);
        hooks.OnPlayerButtons("menu_buttons", menus.OnButtons);
        hooks.OnEvent<EventRoundPrestart>("round_prestart", _ => menus.SuspendEntities());
        hooks.OnEvent<EventRoundStart>("round_start", _ =>
            Server.NextFrame(() => _context?.Guard.Run(Name, "menu_resume", () => _menus?.ResumeEntities())));
        hooks.OnEvent<EventPlayerDisconnect>("player_disconnect", e =>
        {
            if (e.Userid is { } player)
            {
                menus.Close(player.Slot);
            }
        });
        for (var key = 1; key <= MenuNavigator.MaxLines; key++)
        {
            var slotKey = key;
            hooks.CommandListener($"slot{slotKey}", (player, _) => menus.OnSlotKey(player, slotKey), HookMode.Pre);
        }
    }
}
