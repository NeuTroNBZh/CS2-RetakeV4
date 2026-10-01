using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using RetakeV4.Adapters;
using RetakeV4.Domain.Common;
using RetakeV4.Domain.Events;
using RetakeV4.Domain.Hud;
using RetakeV4.Domain.Loadouts;
using RetakeV4.Domain.Rounds;

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
    private readonly Func<RoundPhase> _phase;
    private readonly Dictionary<int, (DateTimeOffset Until, IReadOnlyDictionary<uint, string> Inventory)> _pending = new();

    public NativeBuySelector(
        ModuleContext context, Func<RoundTypeDefinition?> current, Action<CCSPlayerController, BuyDecision> decided, Action<CCSPlayerController> restoreGuns,
        Func<RoundPhase> phase)
    {
        _phase = phase;
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
                if (!NativeBuy.CanCapture(_phase(), player.PawnIsAlive))
                {
                    Alert(player, "allocation.buy.freeze_only");
                    return HookResult.Handled;
                }
                _pending[player.Slot] = (DateTimeOffset.UtcNow + CaptureWindow, Inventory(player));
                return HookResult.Continue;
        }
    }

    public void OnItemPickup(EventItemPickup e)
    {
        if (e.Userid is not { IsValid: true } player || !_pending.Remove(player.Slot, out var pending) || DateTimeOffset.UtcNow > pending.Until)
        {
            return;
        }
        if (PlayerQueries.SideOf(player) is not { } side)
        {
            return;
        }
        // Only what the purchase changed is undone: the bought entity, and the gun it made the player drop.
        var diff = InventorySnapshot.Diff(pending.Inventory, Inventory(player));
        RemoveEntities(diff.Added, null);
        RemoveEntities(diff.Dropped, pending.Inventory);
        LoadoutApplier.ResetCash(player);
        if (NativeBuyResolver.FromPickup(e.Defindex, e.Item) is { } weapon)
        {
            _decided(player, NativeBuy.Decide(weapon, side, _current()));
            _restoreGuns(player);
        }
        else if (NativeBuyResolver.IsAutoManaged(e.Item))
        {
            Alert(player, "allocation.buy.auto_managed");
        }
    }

    public void ClearPending() => _pending.Clear();

    private static IReadOnlyDictionary<uint, string> Inventory(CCSPlayerController player)
    {
        if (player.PlayerPawn.Value is not { IsValid: true, WeaponServices: { } services })
        {
            return new Dictionary<uint, string>();
        }
        return services.MyWeapons
            .Select(handle => handle.Value)
            .OfType<CBasePlayerWeapon>()
            .Where(weapon => weapon.IsValid)
            .GroupBy(weapon => weapon.Index)
            .ToDictionary(group => group.Key, group => group.First().DesignerName);
    }

    // Dropped entities are checked against the snapshot's designer name: the index may have been reused meanwhile.
    private static void RemoveEntities(IEnumerable<uint> indexes, IReadOnlyDictionary<uint, string>? expected)
    {
        foreach (var index in indexes)
        {
            var entity = Utilities.GetEntityFromIndex<CBasePlayerWeapon>((int)index);
            if (entity is { IsValid: true } && (expected is null || expected.GetValueOrDefault(index) == entity.DesignerName))
            {
                entity.Remove();
            }
        }
    }

    private void Alert(CCSPlayerController player, string key) =>
        _context.Bus.Publish(new HudAlert(new PlayerId(player.Slot), HudText.Of(key)));
}
