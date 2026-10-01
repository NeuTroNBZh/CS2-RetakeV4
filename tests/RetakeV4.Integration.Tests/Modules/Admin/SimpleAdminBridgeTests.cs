using CounterStrikeSharp.API.Core;
using RetakeV4.Domain.Admin;
using RetakeV4.Domain.Hud;
using RetakeV4.Modules.Admin;

namespace RetakeV4.Integration.Tests.Modules.Admin;

public sealed class FakeCapability
{
    public object? Api { get; set; }

    public object? Get() => Api;
}

// Same shape as CS2-SimpleAdmin's ICS2_SimpleAdminApi for the methods the bridge uses.
public interface IFakeSimpleAdminApi
{
    public static FakeCapability PluginCapability = new();

    void RegisterMenuCategory(string categoryId, string categoryName, string permission);

    void RegisterMenu(string categoryId, string menuId, string menuName, Func<CCSPlayerController, object> menuFactory, string? permission, string? commandName);

    void UnregisterMenu(string categoryId, string menuId);

    object CreateMenuWithBack(string title, string categoryId, CCSPlayerController player);

    void AddMenuOption(object menu, string name, Action<CCSPlayerController> action, bool disabled, string? permission);
}

// An older API without AddMenuOption.
public interface IOldSimpleAdminApi
{
    public static FakeCapability PluginCapability = new();

    void RegisterMenuCategory(string categoryId, string categoryName, string permission);

    void RegisterMenu(string categoryId, string menuId, string menuName, Func<CCSPlayerController, object> menuFactory, string? permission, string? commandName);

    void UnregisterMenu(string categoryId, string menuId);

    object CreateMenuWithBack(string title, string categoryId, CCSPlayerController player);
}

public sealed class FakeSimpleAdmin : IFakeSimpleAdminApi
{
    public List<string> Calls { get; } = new();

    public Dictionary<string, Func<CCSPlayerController, object>> Factories { get; } = new();

    public List<(string Name, Action<CCSPlayerController> Action)> Options { get; } = new();

    public void RegisterMenuCategory(string categoryId, string categoryName, string permission) =>
        Calls.Add($"category:{categoryId}:{categoryName}:{permission}");

    public void RegisterMenu(string categoryId, string menuId, string menuName, Func<CCSPlayerController, object> menuFactory, string? permission, string? commandName)
    {
        Calls.Add($"menu:{menuId}:{menuName}");
        Factories[menuId] = menuFactory;
    }

    public void UnregisterMenu(string categoryId, string menuId) => Calls.Add($"unregister:{menuId}");

    public object CreateMenuWithBack(string title, string categoryId, CCSPlayerController player)
    {
        Calls.Add($"back:{title}");
        return new object();
    }

    public void AddMenuOption(object menu, string name, Action<CCSPlayerController> action, bool disabled, string? permission) =>
        Options.Add((name, action));
}

public sealed class SimpleAdminBridgeTests : IDisposable
{
    private static readonly CCSPlayerController Admin = new(IntPtr.Zero);

    private readonly FakeSimpleAdmin _api = new();
    private readonly ListLogger _logger = new();
    private readonly List<string> _selected = new();
    private bool _labelsAvailable = true;

    public SimpleAdminBridgeTests() => IFakeSimpleAdminApi.PluginCapability.Api = _api;

    public void Dispose()
    {
        IFakeSimpleAdminApi.PluginCapability.Api = null;
        IOldSimpleAdminApi.PluginCapability.Api = null;
    }

    private string Label(HudText text) =>
        _labelsAvailable ? text.Key ?? text.Literal ?? string.Empty : throw new InvalidOperationException("module unloaded");

    private SimpleAdminBridge Registered()
    {
        var bridge = SimpleAdminBridge.TryCreate(typeof(IFakeSimpleAdminApi), _logger)!;
        bridge.Register(AdminMenu.Build(), "@retakev4/admin", Label, (_, itemId) => _selected.Add(itemId));
        return bridge;
    }

    [Fact]
    public void Register_AddsTheCategory_AndOneEntryPerRootItem()
    {
        Registered();
        Assert.Equal(
            new[]
            {
                "category:retakev4:admin.menu.title:@retakev4/admin",
                "menu:editor:admin.menu.editor",
                "menu:forcesite:admin.menu.forcesite",
                "menu:scramble:admin.menu.scramble",
                "menu:cleanup:admin.menu.cleanup",
            },
            _api.Calls);
    }

    [Fact]
    public void ActionEntry_RunsTheSelection_AndReturnsABackMenu()
    {
        Registered();
        Assert.NotNull(_api.Factories["scramble"](Admin));
        Assert.Equal(new[] { AdminMenu.ScrambleId }, _selected);
    }

    [Fact]
    public void SubmenuEntry_OffersOneOptionPerChoice()
    {
        Registered();
        _api.Factories["forcesite"](Admin);
        Assert.Equal(5, _api.Options.Count);
        _api.Options[0].Action(Admin);
        Assert.Equal(new[] { "force:A:Once" }, _selected);
    }

    [Fact]
    public void Entries_KeepWorking_WhenLabelsAreNoLongerAvailable()
    {
        Registered();
        _labelsAvailable = false;
        _api.Factories["forcesite"](Admin);
        Assert.Equal(5, _api.Options.Count);
    }

    [Fact]
    public void Unregister_RemovesEveryEntry()
    {
        Registered().Unregister();
        Assert.Equal(new[] { "unregister:editor", "unregister:forcesite", "unregister:scramble", "unregister:cleanup" }, _api.Calls.Where(c => c.StartsWith("unregister:")));
    }

    [Fact]
    public void IncompatibleApi_IsRejectedUpFront()
    {
        IOldSimpleAdminApi.PluginCapability.Api = new object();
        Assert.Null(SimpleAdminBridge.TryCreate(typeof(IOldSimpleAdminApi), _logger));
        Assert.Contains(_logger.Entries, e => e.Message.Contains("AddMenuOption", StringComparison.Ordinal));
    }

    [Fact]
    public void UnavailableApi_GivesNoBridge()
    {
        IFakeSimpleAdminApi.PluginCapability.Api = null;
        Assert.Null(SimpleAdminBridge.TryCreate(typeof(IFakeSimpleAdminApi), _logger));
    }
}
