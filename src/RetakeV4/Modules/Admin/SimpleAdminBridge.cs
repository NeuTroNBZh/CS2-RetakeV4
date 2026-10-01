using System.Reflection;
using CounterStrikeSharp.API.Core;
using Microsoft.Extensions.Logging;
using RetakeV4.Domain.Hud;

namespace RetakeV4.Modules.Admin;

// Optional CS2-SimpleAdmin integration without a compile-time dependency. Every method the bridge will ever call is resolved
// on the public interface ICS2_SimpleAdminApi by exact signature before anything is registered, so an incompatible version
// is rejected at startup instead of failing inside SimpleAdmin's menu later.
internal sealed class SimpleAdminBridge
{
    private const string ApiTypeName = "CS2_SimpleAdminApi.ICS2_SimpleAdminApi";
    private const string CategoryId = "retakev4";

    private readonly object _api;
    private readonly ILogger _logger;
    private readonly MethodInfo _registerCategory;
    private readonly MethodInfo _registerMenu;
    private readonly MethodInfo _unregisterMenu;
    private readonly MethodInfo _createMenuWithBack;
    private readonly MethodInfo _addMenuOption;
    private readonly List<string> _menus = new();

    private SimpleAdminBridge(object api, ILogger logger, IReadOnlyDictionary<string, MethodInfo> methods)
    {
        _api = api;
        _logger = logger;
        _registerCategory = methods["RegisterMenuCategory"];
        _registerMenu = methods["RegisterMenu"];
        _unregisterMenu = methods["UnregisterMenu"];
        _createMenuWithBack = methods["CreateMenuWithBack"];
        _addMenuOption = methods["AddMenuOption"];
    }

    private static IReadOnlyDictionary<string, Type[]> Signatures { get; } = new Dictionary<string, Type[]>
    {
        ["RegisterMenuCategory"] = new[] { typeof(string), typeof(string), typeof(string) },
        ["RegisterMenu"] = new[] { typeof(string), typeof(string), typeof(string), typeof(Func<CCSPlayerController, object>), typeof(string), typeof(string) },
        ["UnregisterMenu"] = new[] { typeof(string), typeof(string) },
        ["CreateMenuWithBack"] = new[] { typeof(string), typeof(string), typeof(CCSPlayerController) },
        ["AddMenuOption"] = new[] { typeof(object), typeof(string), typeof(Action<CCSPlayerController>), typeof(bool), typeof(string) },
    };

    public static SimpleAdminBridge? TryFind(ILogger logger) =>
        AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType(ApiTypeName))
            .OfType<Type>()
            .Select(apiType => TryCreate(apiType, logger))
            .FirstOrDefault(bridge => bridge is not null);

    public static SimpleAdminBridge? TryCreate(Type apiType, ILogger logger)
    {
        object? api;
        try
        {
            var capability = apiType.GetField("PluginCapability", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            api = capability?.GetType().GetMethod("Get", Type.EmptyTypes)?.Invoke(capability, null);
        }
        catch (TargetInvocationException ex)
        {
            logger.LogWarning(ex.InnerException ?? ex, "CS2-SimpleAdmin API found but not available");
            return null;
        }
        if (api is null)
        {
            return null;
        }
        var methods = Signatures.ToDictionary(s => s.Key, s => apiType.GetMethod(s.Key, s.Value));
        if (methods.FirstOrDefault(m => m.Value is null).Key is { } missing)
        {
            logger.LogWarning("CS2-SimpleAdmin API has no compatible {Method} method: Retake entries are not added to its menu", missing);
            return null;
        }
        return new SimpleAdminBridge(api, logger, methods.ToDictionary(m => m.Key, m => m.Value!));
    }

    // Labels are computed now: SimpleAdmin may call the factories after this module is gone. Root actions become entries;
    // a root submenu becomes a SimpleAdmin menu with one option per child.
    public void Register(Menu menu, string permission, Func<HudText, string> label, Action<CCSPlayerController, string> select)
    {
        var title = label(menu.Title);
        _registerCategory.Invoke(_api, new object?[] { CategoryId, title, permission });
        foreach (var item in menu.Items)
        {
            var name = label(item.Label);
            Func<CCSPlayerController, object> factory = item.Submenu is { } submenu
                ? SubmenuFactory(label(submenu.Title), submenu.Items.Select(c => (c.Id, label(c.Label))).ToList(), select, title)
                : admin => Safe(title, admin, () =>
                {
                    select(admin, item.Id);
                    return CreateMenuWithBack(title, admin);
                });
            _registerMenu.Invoke(_api, new object?[] { CategoryId, item.Id, name, factory, permission, null });
            _menus.Add(item.Id);
        }
    }

    public void Unregister()
    {
        foreach (var menuId in _menus)
        {
            try
            {
                _unregisterMenu.Invoke(_api, new object?[] { CategoryId, menuId });
            }
            catch (TargetInvocationException ex)
            {
                _logger.LogWarning(ex.InnerException ?? ex, "Could not unregister SimpleAdmin menu {Menu}", menuId);
            }
        }
        _menus.Clear();
    }

    private Func<CCSPlayerController, object> SubmenuFactory(
        string submenuTitle, IReadOnlyList<(string Id, string Name)> children, Action<CCSPlayerController, string> select, string fallbackTitle) =>
        admin => Safe(fallbackTitle, admin, () =>
        {
            var menu = CreateMenuWithBack(submenuTitle, admin);
            foreach (var (id, name) in children)
            {
                Action<CCSPlayerController> action = player => select(player, id);
                _addMenuOption.Invoke(_api, new object?[] { menu, name, action, false, null });
            }
            return menu;
        });

    // Runs inside SimpleAdmin's menu code: a failure is logged here and the admin gets a plain back menu.
    private object Safe(string title, CCSPlayerController admin, Func<object> build)
    {
        try
        {
            return build();
        }
        catch (Exception ex) when (ex is TargetInvocationException or InvalidOperationException or ArgumentException)
        {
            _logger.LogWarning(ex, "CS2-SimpleAdmin menu {Title} failed", title);
            return CreateMenuWithBack(title, admin);
        }
    }

    private object CreateMenuWithBack(string title, CCSPlayerController admin) =>
        _createMenuWithBack.Invoke(_api, new object?[] { title, CategoryId, admin })
        ?? throw new InvalidOperationException("SimpleAdmin returned no menu");
}
