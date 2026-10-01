using RetakeV4.Domain.Common;
using RetakeV4.Domain.Hud;

namespace RetakeV4.Domain.Events;

// RefreshOnly: replace the menu only if the player still has this menu open (never reopens a closed menu).
public sealed record HudMenuOpen(PlayerId Player, Menu Menu, bool RefreshOnly = false);

public sealed record HudMenuClose(PlayerId Player, string MenuId);

public sealed record HudMenuSelected(PlayerId Player, string MenuId, string ItemId);

// Published after a full loadout: the slot commands it sends come back as client commands for about a round trip.
public sealed record LoadoutApplied(PlayerId Player);

// Player null: shown to everyone.
public sealed record HudAlert(PlayerId? Player, HudText Text);
