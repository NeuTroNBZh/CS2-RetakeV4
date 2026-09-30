using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using RetakeV4.Domain.Loadouts;

namespace RetakeV4.Modules.Allocation;

internal static class LoadoutApplier
{
    private static readonly string[] SlotOrder = { "slot3", "slot2", "slot1" };

    public static void Apply(CCSPlayerController player, Loadout loadout)
    {
        var pawn = player.PlayerPawn.Value;
        if (pawn is null || !pawn.IsValid || pawn.ItemServices is null || pawn.WeaponServices is null)
        {
            return;
        }
        Strip(pawn);
        var items = new CCSPlayer_ItemServices(pawn.ItemServices.Handle);
        foreach (var grenade in loadout.Grenades)
        {
            player.GiveNamedItem(grenade);
        }
        player.GiveNamedItem(loadout.Secondary);
        if (loadout.Primary is { } primary)
        {
            player.GiveNamedItem(primary);
        }
        if (loadout.Zeus)
        {
            player.GiveNamedItem(CsItem.Taser);
        }
        if (loadout.DefuseKit)
        {
            items.HasDefuser = true;
        }
        GiveArmor(player, items, loadout.Armor);
        foreach (var slot in SlotOrder)
        {
            player.ExecuteClientCommand(slot);
        }
    }

    private static void Strip(CCSPlayerPawn pawn)
    {
        var weapons = pawn.WeaponServices!.MyWeapons
            .Select(handle => handle.Value)
            .Where(weapon => weapon is { IsValid: true } && !IsKnife(weapon.DesignerName))
            .ToList();
        foreach (var weapon in weapons)
        {
            weapon!.Remove();
        }
        pawn.ArmorValue = 0;
        var items = new CCSPlayer_ItemServices(pawn.ItemServices!.Handle);
        items.HasHelmet = false;
        items.HasDefuser = false;
    }

    private static bool IsKnife(string designerName) =>
        designerName.Contains("knife", StringComparison.Ordinal) || designerName.Contains("bayonet", StringComparison.Ordinal);

    private static void GiveArmor(CCSPlayerController player, CCSPlayer_ItemServices items, ArmorKind armor)
    {
        switch (armor)
        {
            case ArmorKind.Kevlar:
                player.GiveNamedItem(CsItem.Kevlar);
                break;
            case ArmorKind.KevlarHelmet:
                player.GiveNamedItem(CsItem.AssaultSuit);
                items.HasHelmet = true;
                break;
        }
    }
}
