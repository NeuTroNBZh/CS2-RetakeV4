using System.Reflection;
using CounterStrikeSharp.API.Core;
using Microsoft.Extensions.Logging;
using RetakeV4.Domain.Hud;

namespace RetakeV4.Modules.Admin;

// Optional CS2-SimpleAdmin integration without a compile-time dependency: methods are resolved on the public interface
// ICS2_SimpleAdminApi by exact signature, so an incompatible version fails loudly at registration instead of mid-game.
internal sealed class SimpleAdminBridge
{
    private const string ApiTypeName = "CS2_SimpleAdminApi.ICS2_SimpleAdminApi";
    private const string CategoryId = "retakev4";

    private readonly object _api;
    private readonly Type _apiType;
    private readonly ILogger _logger;
    private readonly List<string> _menus = new();

    private SimpleAdminBridge(object api, Type apiType, ILogger logger)
    {
        _api = api;
        _apiType = apiType;
        _logger = logger;
    }

    public static SimpleAdminBridge? TryFind(ILogger logger)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var apiType = assembly.GetType(ApiTypeName);
            if (apiType is null)
            {
                continue;
            }
            try
            {
                var capability = apiType.GetField("PluginCapability", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                var api = capability?.GetType().GetMethod("Get", Type.EmptyTypes)?.Invoke(capability, null);
                if (api is not null)
                {
                    return new SimpleAdminBridge(api, apiType, logger);
                }
            }
            catch (TargetInvocationException ex)
            {
                logger.LogWarning(ex.InnerException ?? ex, "CS2-SimpleAdmin API found but not available");
            }
        }
        return null;
    }

    // Root actions become SimpleAdmin menu entries; a root submenu becomes a SimpleAdmin menu with one option per child.
    public void Register(Menu menu, string permission, Func<HudText, string> label, Action<CCSPlayerController, string> select)
    {
        var title = label(menu.Title);
        Invoke("RegisterMenuCategory", new[] { typeof(string), typeof(string), typeof(string) }, CategoryId, title, permission);
        foreach (var item in menu.Items)
        {
            Func<CCSPlayerController, object> factory = item.Submenu is { } submenu
                ? admin => SubmenuFor(admin, submenu, label, select)
                : admin =>
                {
                    select(admin, item.Id);
                    return CreateMenuWithBack(title, admin);
                };
            Invoke(
                "RegisterMenu",
                new[] { typeof(string), typeof(string), typeof(string), typeof(Func<CCSPlayerController, object>), typeof(string), typeof(string) },
                CategoryId, item.Id, label(item.Label), factory, permission, null);
            _menus.Add(item.Id);
        }
    }

    public void Unregister()
    {
        foreach (var menuId in _menus)
        {
            try
            {
                Invoke("UnregisterMenu", new[] { typeof(string), typeof(string) }, CategoryId, menuId);
            }
            catch (Exception ex) when (ex is TargetInvocationException or MissingMethodException)
            {
                _logger.LogWarning(ex, "Could not unregister SimpleAdmin menu {Menu}", menuId);
            }
        }
        _menus.Clear();
    }

    private object SubmenuFor(CCSPlayerController admin, Menu submenu, Func<HudText, string> label, Action<CCSPlayerController, string> select)
    {
        var menu = CreateMenuWithBack(label(submenu.Title), admin);
        foreach (var child in submenu.Items)
        {
            Action<CCSPlayerController> action = player => select(player, child.Id);
            Invoke(
                "AddMenuOption",
                new[] { typeof(object), typeof(string), typeof(Action<CCSPlayerController>), typeof(bool), typeof(string) },
                menu, label(child.Label), action, false, null);
        }
        return menu;
    }

    private object CreateMenuWithBack(string title, CCSPlayerController admin) =>
        Invoke("CreateMenuWithBack", new[] { typeof(string), typeof(string), typeof(CCSPlayerController) }, title, CategoryId, admin)
        ?? throw new InvalidOperationException("SimpleAdmin returned no menu");

    private object? Invoke(string name, Type[] signature, params object?[] args) =>
        (_apiType.GetMethod(name, signature) ?? throw new MissingMethodException(_apiType.FullName, name)).Invoke(_api, args);
}
