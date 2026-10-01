using RetakeV4.Domain.Common;
using RetakeV4.Domain.Hud;
using RetakeV4.Domain.Rounds;

namespace RetakeV4.Domain.Loadouts;

public enum WeaponSlot
{
    Primary,
    Secondary,
}

public sealed record WeaponMenuSelection(TeamSide Team, string RoundType, WeaponSlot Slot, string Weapon)
{
    private const string Prefix = "pick";

    // The round type goes last: it is free text from roundtypes.json and may contain ':'.
    public string ToItemId() => $"{Prefix}:{Team}:{Slot}:{Weapon}:{RoundType}";

    public static WeaponMenuSelection? Parse(string itemId)
    {
        var parts = itemId.Split(':', 5);
        if (parts.Length != 5 || parts[0] != Prefix || parts[3].Length == 0 || parts[4].Length == 0)
        {
            return null;
        }
        if (!Enum.TryParse<TeamSide>(parts[1], out var team) || !Enum.IsDefined(team)
            || !Enum.TryParse<WeaponSlot>(parts[2], out var slot) || !Enum.IsDefined(slot))
        {
            return null;
        }
        return new WeaponMenuSelection(team, parts[4], slot, parts[3]);
    }
}

public sealed record WeaponMenuState(
    IReadOnlyList<RoundTypeDefinition> Definitions,
    RoundTypeDefinition? Current,
    TeamSide? Team,
    Func<TeamSide, string, LoadoutPreference?> PreferenceFor,
    bool AwpOptIn);

public static class WeaponMenu
{
    public const string MenuId = "allocation.weapons";
    public const string AwpItemId = "awp";
    public const string CurrentItemId = "current";
    public const string OthersItemId = "others";

    private static readonly TeamSide[] Sides = { TeamSide.T, TeamSide.CT };

    // The AWP is never a regular choice: it is only handed out to volunteers.
    public static IReadOnlyList<string> Options(RoundTypeDefinition definition, TeamSide team, WeaponSlot slot)
    {
        var primary = slot == WeaponSlot.Primary;
        var pool = primary ? definition.Primaries.For(team) : definition.Secondaries.For(team);
        var fallback = primary ? definition.DefaultFor(team).Primary : definition.DefaultFor(team).Secondary;
        return new[] { fallback }
            .Concat(pool)
            .OfType<string>()
            .Where(w => (primary ? WeaponCatalog.IsPrimary(w) : WeaponCatalog.IsSecondary(w)) && w != WeaponCatalog.Awp)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    public static bool HasChoice(RoundTypeDefinition definition, TeamSide team) =>
        Options(definition, team, WeaponSlot.Primary).Count > 1 || Options(definition, team, WeaponSlot.Secondary).Count > 1;

    public static bool IsAllowed(WeaponMenuSelection selection, IReadOnlyList<RoundTypeDefinition> definitions) =>
        definitions.FirstOrDefault(d => d.Name == selection.RoundType) is { } definition
        && Options(definition, selection.Team, selection.Slot).Contains(selection.Weapon, StringComparer.Ordinal);

    // The current loadout is only replaced during freeze time, for a living player choosing for his own team and the current round type.
    public static bool AppliesNow(WeaponMenuSelection selection, RoundPhase phase, bool alive, TeamSide? side, string? currentRoundType) =>
        phase == RoundPhase.FreezeTime && alive && side == selection.Team && selection.RoundType == currentRoundType;

    public static Menu Build(WeaponMenuState state)
    {
        var items = new List<MenuItem>();
        if (state.Team is { } team && state.Current is { } current && HasChoice(current, team))
        {
            items.Add(new MenuItem(
                CurrentItemId,
                HudText.Of("allocation.menu.current", current.Name, team.ToString()),
                MenuItemKind.Submenu,
                Submenu: ConfigMenu(state, current, team)));
        }
        var others = state.Definitions
            .SelectMany(d => Sides.Select(side => (Definition: d, Side: side)))
            .Where(c => HasChoice(c.Definition, c.Side) && !(c.Side == state.Team && c.Definition.Name == state.Current?.Name))
            .Select(c => new MenuItem(
                ConfigId(c.Side, c.Definition.Name),
                HudText.Of("allocation.menu.config", c.Definition.Name, c.Side.ToString()),
                MenuItemKind.Submenu,
                Submenu: ConfigMenu(state, c.Definition, c.Side)))
            .ToList();
        if (others.Count > 0)
        {
            items.Add(new MenuItem(
                OthersItemId,
                HudText.Of("allocation.menu.others"),
                MenuItemKind.Submenu,
                Submenu: new Menu(OthersItemId, HudText.Of("allocation.menu.others"), others)));
        }
        items.Add(new MenuItem(AwpItemId, HudText.Of("allocation.menu.awp"), MenuItemKind.Toggle, IsOn: state.AwpOptIn));
        return new Menu(MenuId, HudText.Of("allocation.menu.title"), items);
    }

    private static Menu ConfigMenu(WeaponMenuState state, RoundTypeDefinition definition, TeamSide team)
    {
        var request = new LoadoutRequest(new PlayerId(0), team, state.PreferenceFor(team, definition.Name));
        var (primary, secondary) = LoadoutPlanner.ResolveWeapons(definition, request);
        var items = new[]
            {
                SlotItem(definition, team, WeaponSlot.Primary, primary, "allocation.menu.primary"),
                SlotItem(definition, team, WeaponSlot.Secondary, secondary, "allocation.menu.secondary"),
            }
            .OfType<MenuItem>()
            .ToList();
        return new Menu(ConfigId(team, definition.Name), HudText.Of("allocation.menu.config", definition.Name, team.ToString()), items);
    }

    private static MenuItem? SlotItem(RoundTypeDefinition definition, TeamSide team, WeaponSlot slot, string? effective, string labelKey)
    {
        var options = Options(definition, team, slot);
        if (options.Count < 2)
        {
            return null;
        }
        var label = HudText.Of(labelKey, WeaponNames.Display(effective));
        var choices = options
            .Select(w => new MenuItem(
                new WeaponMenuSelection(team, definition.Name, slot, w).ToItemId(),
                HudText.Raw(WeaponNames.Display(w)),
                MenuItemKind.Choice,
                IsOn: w == effective))
            .ToList();
        var id = slot.ToString().ToLowerInvariant();
        return new MenuItem(id, label, MenuItemKind.Submenu, Submenu: new Menu($"{ConfigId(team, definition.Name)}:{id}", label, choices));
    }

    private static string ConfigId(TeamSide team, string roundType) => $"cfg:{team}:{roundType}";
}
