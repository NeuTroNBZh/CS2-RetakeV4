using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using RetakeV4.Adapters;
using RetakeV4.Domain.Common;
using RetakeV4.Domain.Events;
using RetakeV4.Domain.Hud;
using RetakeV4.Domain.Loadouts;

namespace RetakeV4.Modules.Allocation;

// The CS2 buy menu as a weapon picker (agora's method): a resolved buy is blocked and treated as a choice; an ambiguous one
// (numeric buy-menu id) goes through, and the bought item is identified at item_pickup, removed, and the round's guns restored.
internal sealed class NativeBuySelector
{
    private static readonly TimeSpan CaptureWindow = TimeSpan.FromSeconds(2);

    private readonly ModuleContext _context;
    private readonly Func<RoundTypeDefinition?> _current;
    private readonly Action<CCSPlayerController, BuyDecision> _decided;
    private readonly Action<CCSPlayerController> _restoreGuns;
    private readonly Dictionary<int, DateTimeOffset> _pending = new();

    public NativeBuySelector(
        ModuleContext context, Func<RoundTypeDefinition?> current, Action<CCSPlayerController, BuyDecision> decided, Action<CCSPlayerController> restoreGuns)
    {
        _context = context;
        _current = current;
        _decided = decided;
        _restoreGuns = restoreGuns;
    }

    public HookResult OnBuy(CCSPlayerController? player, CommandInfo command)
    {
        if (player is not { IsValid: true } || PlayerQueries.SideOf(player) is not { } side || GameRulesAccessor.IsWarmup())
        {
            return HookResult.Continue;
        }
        var arguments = Enumerable.Range(1, Math.Max(0, command.ArgCount - 1)).Select(command.GetArg).Append(command.ArgString);
        var request = NativeBuyResolver.Resolve(arguments);
        switch (request.Kind)
        {
            case BuyRequestKind.AutoManaged:
                Alert(player, "allocation.buy.auto_managed");
                return HookResult.Handled;
            case BuyRequestKind.Weapon when request.Weapon is { } weapon:
                _decided(player, NativeBuy.Decide(weapon, side, _current()));
                return HookResult.Handled;
            default:
                _pending[player.Slot] = DateTimeOffset.UtcNow + CaptureWindow;
                return HookResult.Continue;
        }
    }

    public void OnItemPickup(EventItemPickup e)
    {
        if (e.Userid is not { IsValid: true } player || !_pending.Remove(player.Slot, out var until) || DateTimeOffset.UtcNow > until)
        {
            return;
        }
        if (PlayerQueries.SideOf(player) is not { } side)
        {
            return;
        }
        RemoveItem(player, e.Item);
        LoadoutApplier.ResetCash(player);
        if (NativeBuyResolver.FromPickup(e.Defindex, e.Item) is { } weapon)
        {
            _decided(player, NativeBuy.Decide(weapon, side, _current()));
        }
        else if (NativeBuyResolver.IsAutoManaged(e.Item))
        {
            Alert(player, "allocation.buy.auto_managed");
        }
        _restoreGuns(player);
    }

    public void ClearPending() => _pending.Clear();

    private static void RemoveItem(CCSPlayerController player, string? item)
    {
        if (item is null || player.PlayerPawn.Value is not { IsValid: true, WeaponServices: { } services })
        {
            return;
        }
        var bought = NativeBuyResolver.Normalize(item);
        var matching = services.MyWeapons
            .Select(handle => handle.Value)
            .Where(weapon => weapon is { IsValid: true } && NativeBuyResolver.Normalize(weapon.DesignerName) == bought)
            .ToList();
        foreach (var weapon in matching)
        {
            weapon!.Remove();
        }
    }

    private void Alert(CCSPlayerController player, string key) =>
        _context.Bus.Publish(new HudAlert(new PlayerId(player.Slot), HudText.Of(key)));
}
