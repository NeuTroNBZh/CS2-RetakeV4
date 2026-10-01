using CounterStrikeSharp.API;
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

    // Freeze-time change: only the guns that changed are replaced; C4, grenades, armor and kit stay, and no slot command is sent.
    public static void SwapWeapons(CCSPlayerController player, Loadout before, Loadout after)
    {
        var pawn = player.PlayerPawn.Value;
        if (pawn is null || !pawn.IsValid || pawn.WeaponServices is null)
        {
            return;
        }
        var swap = LoadoutPlanner.SwapBetween(before, after);
        var replaced = pawn.WeaponServices.MyWeapons
            .Select(handle => handle.Value)
            .Where(weapon => weapon is { IsValid: true }
                && ((swap.Primary && WeaponCatalog.IsPrimary(weapon.DesignerName)) || (swap.Secondary && WeaponCatalog.IsSecondary(weapon.DesignerName))))
            .ToList();
        foreach (var weapon in replaced)
        {
            weapon!.Remove();
        }
        if (swap.Secondary)
        {
            player.GiveNamedItem(after.Secondary);
        }
        if (swap.Primary && after.Primary is { } primary)
        {
            player.GiveNamedItem(primary);
        }
    }

    // After a captured native buy: every gun goes, the round's guns come back (the bought one never stays).
    public static void ReplaceGuns(CCSPlayerController player, Loadout loadout)
    {
        var pawn = player.PlayerPawn.Value;
        if (pawn is null || !pawn.IsValid || pawn.WeaponServices is null)
        {
            return;
        }
        var guns = pawn.WeaponServices.MyWeapons
            .Select(handle => handle.Value)
            .Where(weapon => weapon is { IsValid: true } && (WeaponCatalog.IsPrimary(weapon.DesignerName) || WeaponCatalog.IsSecondary(weapon.DesignerName)))
            .ToList();
        foreach (var weapon in guns)
        {
            weapon!.Remove();
        }
        player.GiveNamedItem(loadout.Secondary);
        if (loadout.Primary is { } primary)
        {
            player.GiveNamedItem(primary);
        }
    }

    public static void ResetCash(CCSPlayerController player)
    {
        if (player.InGameMoneyServices is not { } money)
        {
            return;
        }
        money.Account = NativeBuy.Cash;
        Utilities.SetStateChanged(player, "CCSPlayerController", "m_pInGameMoneyServices");
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
