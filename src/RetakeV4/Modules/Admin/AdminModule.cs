using System.Reflection;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using Microsoft.Extensions.Logging;
using RetakeV4.Configuration;
using RetakeV4.Domain.Admin;
using RetakeV4.Domain.Common;
using RetakeV4.Domain.Events;
using RetakeV4.Domain.Hud;

namespace RetakeV4.Modules.Admin;

// Checks the permission, then hands every action to the module that owns it through the bus.
public sealed class AdminModule : IRetakeModule
{
    private const string AdminFlag = "@retakev4/admin";

    private AdminConfig _config = new();
    private ModuleContext? _context;
    private SimpleAdminBridge? _bridge;

    public string Name => "Admin";

    public IReadOnlyList<string> DependsOn { get; } = new[] { "Core" };

    private ModuleContext Context => _context ?? throw new InvalidOperationException("Admin module is not loaded");

    public ModuleConfig LoadConfig(JsonConfigStore store, ILogger logger)
    {
        var result = store.Load("admin.json", new AdminConfig());
        ConfigLogging.Report(logger, result.Issues);
        _config = result.Config;
        return _config;
    }

    public void Load(ModuleContext context)
    {
        _context = context;
        var hooks = context.Hooks;
        hooks.Command("css_retake", "Retake admin menu: css_retake [edit]", OnRetakeCommand);
        hooks.OnBus<HudMenuSelected>(OnMenuSelected);
        if (_config.SimpleAdminBridge)
        {
            hooks.OnBus<AllPluginsLoaded>(_ => RegisterBridge());
        }
    }

    public void Unload()
    {
        _bridge?.Unregister();
        _bridge = null;
        _context = null;
    }

    internal static bool IsAdmin(CCSPlayerController player) =>
        player.IsValid && AdminManager.PlayerHasPermissions(player, AdminFlag);

    internal void Execute(CCSPlayerController player, AdminSelection selection)
    {
        var id = new PlayerId(player.Slot);
        switch (selection.Action)
        {
            case AdminAction.SpawnEditor:
                Context.Bus.Publish(new SpawnEditorRequested(id));
                break;
            case AdminAction.Scramble:
                Context.Bus.Publish(new ScrambleRequested(id));
                break;
            case AdminAction.ForceSite when selection.Force is { } request:
                Context.Bus.Publish(new ForceSiteRequested(id, request));
                break;
        }
    }

    internal string ServerText(HudText text) =>
        text.Key is { } key ? Context.Text.Server(key, text.Args.ToArray()) : text.Literal ?? string.Empty;

    private void OnRetakeCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player is not { IsValid: true })
        {
            Server.PrintToConsole(Context.Text.Server("admin.player_only"));
            return;
        }
        if (!IsAdmin(player))
        {
            Context.Text.ChatAlert(player, "admin.no_permission");
            return;
        }
        switch (RetakeCommand.Parse(command.ArgCount > 1 ? command.GetArg(1) : null))
        {
            case RetakeCommandKind.Menu:
                Context.Bus.Publish(new HudMenuOpen(new PlayerId(player.Slot), AdminMenu.Build()));
                break;
            case RetakeCommandKind.Editor:
                Execute(player, new AdminSelection(AdminAction.SpawnEditor));
                break;
            default:
                Context.Text.ChatHelp(player, "admin.usage");
                break;
        }
    }

    private void OnMenuSelected(HudMenuSelected e)
    {
        if (e.MenuId != AdminMenu.MenuId || AdminMenu.Parse(e.ItemId) is not { } selection)
        {
            return;
        }
        var player = Utilities.GetPlayerFromSlot(e.Player.Slot);
        if (player is not { IsValid: true })
        {
            return;
        }
        if (!IsAdmin(player))
        {
            Context.Text.ChatAlert(player, "admin.no_permission");
            return;
        }
        Execute(player, selection);
    }

    private void RegisterBridge()
    {
        _bridge?.Unregister();
        _bridge = SimpleAdminBridge.TryFind(Context.Logger);
        if (_bridge is null)
        {
            Context.Logger.LogInformation("CS2-SimpleAdmin not found: the Retake admin menu is available with !retake");
            return;
        }
        try
        {
            _bridge.Register(AdminMenu.Build(), AdminFlag, ServerText, OnBridgeSelection);
            Context.Logger.LogInformation("Retake admin entries registered in CS2-SimpleAdmin");
        }
        catch (Exception ex) when (ex is TargetInvocationException or MissingMethodException or ArgumentException or InvalidOperationException)
        {
            Context.Logger.LogWarning(ex, "CS2-SimpleAdmin API is not compatible: Retake entries were not added to its menu");
            _bridge.Unregister();
            _bridge = null;
        }
    }

    // Called by CS2-SimpleAdmin's menu: guarded like any other handler of this module.
    private void OnBridgeSelection(CCSPlayerController admin, string itemId) =>
        _context?.Guard.Run(Name, "simpleadmin", () =>
        {
            if (AdminMenu.Parse(itemId) is { } selection && IsAdmin(admin))
            {
                Execute(admin, selection);
            }
        });
}
