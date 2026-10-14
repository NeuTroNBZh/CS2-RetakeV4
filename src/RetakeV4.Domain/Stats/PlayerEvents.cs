using System.Globalization;
using RetakeV4.Domain.Geometry;

namespace RetakeV4.Domain.Stats;

public enum PlayerEventType
{
    Reload,
    ReloadFullClip,
    Pickup,
    Drop,
    Jump,
    PlantBegin,
    PlantAbort,
    DefuseBegin,
    DefuseAbort,
    BombPickup,
    BombDrop,
}

// One stats_player_events row. Value is short text: the weapon for pickups and drops, "weapon:clip/max" for reloads,
// "kit" for a defuse started with a kit, "start" for the bomb carrier at the round start.
public sealed record PlayerEventFact(ulong Player, PlayerEventType Type, int TimeMs, string? Value, Vec3? Position);

// Health and armor read on the pawn at round_end (0 and 0 for a dead pawn).
public sealed record PlayerEndState(ulong Player, int Health, int Armor);

public static class PlayerEvents
{
    public static string Name(PlayerEventType type) => type switch
    {
        PlayerEventType.Reload => "reload",
        PlayerEventType.ReloadFullClip => "reload_full_clip",
        PlayerEventType.Pickup => "pickup",
        PlayerEventType.Drop => "drop",
        PlayerEventType.Jump => "jump",
        PlayerEventType.PlantBegin => "plant_begin",
        PlayerEventType.PlantAbort => "plant_abort",
        PlayerEventType.DefuseBegin => "defuse_begin",
        PlayerEventType.DefuseAbort => "defuse_abort",
        PlayerEventType.BombPickup => "bomb_pickup",
        PlayerEventType.BombDrop => "bomb_drop",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown player event type"),
    };

    // CS2 never reloads a full magazine: a reload started with at least half of it left is the "full clip" (early) reload.
    public static PlayerEventFact Reload(ulong player, string? weapon, int clip, int maxClip, int timeMs, Vec3? position)
    {
        var name = StatsWeapons.Normalize(weapon);
        var early = maxClip > 0 && clip * 2 >= maxClip;
        var value = maxClip > 0 ? string.Create(CultureInfo.InvariantCulture, $"{name}:{clip}/{maxClip}") : name;
        return new PlayerEventFact(player, early ? PlayerEventType.ReloadFullClip : PlayerEventType.Reload, timeMs, value, position);
    }

    // Grenades leave the inventory when thrown and the bomb has its own events: neither is a pickup or a drop.
    public static PlayerEventFact? Item(ulong player, PlayerEventType type, string? item, int timeMs, Vec3? position)
    {
        if (type is not (PlayerEventType.Pickup or PlayerEventType.Drop))
        {
            throw new ArgumentOutOfRangeException(nameof(type), type, "Only pickups and drops carry an item");
        }
        var name = StatsWeapons.Normalize(item);
        if (name.Length == 0 || name == "c4" || StatsWeapons.GrenadeOf(name) is not null)
        {
            return null;
        }
        return new PlayerEventFact(player, type, timeMs, name, position);
    }

    public static PlayerEventFact Simple(ulong player, PlayerEventType type, int timeMs, Vec3? position, string? value = null) =>
        new(player, type, timeMs, value, position);
}

public static class BombClock
{
    // Milliseconds left before the planted bomb explodes, never negative. Times are engine seconds. Null when a time is
    // not finite (an unread or garbage C4Blow): unknown, never 0 ms (which would make a clutch defuse).
    public static int? RemainingMs(float blowTime, float now)
    {
        if (!float.IsFinite(blowTime) || !float.IsFinite(now))
        {
            return null;
        }
        return (int)Math.Clamp(Math.Round(((double)blowTime - now) * 1000.0), 0, int.MaxValue);
    }
}
