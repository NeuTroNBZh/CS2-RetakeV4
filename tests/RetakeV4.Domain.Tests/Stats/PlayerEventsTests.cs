using RetakeV4.Domain.Geometry;
using RetakeV4.Domain.Stats;

namespace RetakeV4.Domain.Tests.Stats;

public class PlayerEventsTests
{
    [Theory]
    [InlineData(PlayerEventType.Reload, "reload")]
    [InlineData(PlayerEventType.ReloadFullClip, "reload_full_clip")]
    [InlineData(PlayerEventType.Pickup, "pickup")]
    [InlineData(PlayerEventType.Drop, "drop")]
    [InlineData(PlayerEventType.Jump, "jump")]
    [InlineData(PlayerEventType.PlantBegin, "plant_begin")]
    [InlineData(PlayerEventType.PlantAbort, "plant_abort")]
    [InlineData(PlayerEventType.DefuseBegin, "defuse_begin")]
    [InlineData(PlayerEventType.DefuseAbort, "defuse_abort")]
    [InlineData(PlayerEventType.BombPickup, "bomb_pickup")]
    [InlineData(PlayerEventType.BombDrop, "bomb_drop")]
    public void Name_IsTheStoredType(PlayerEventType type, string expected) => Assert.Equal(expected, PlayerEvents.Name(type));

    [Fact]
    public void Name_OfAnUnknownType_Throws() => Assert.Throws<ArgumentOutOfRangeException>(() => PlayerEvents.Name((PlayerEventType)99));

    [Theory]
    [InlineData(20, 30, PlayerEventType.ReloadFullClip, "m4a1:20/30")]
    [InlineData(15, 30, PlayerEventType.ReloadFullClip, "m4a1:15/30")]
    [InlineData(14, 30, PlayerEventType.Reload, "m4a1:14/30")]
    [InlineData(0, 30, PlayerEventType.Reload, "m4a1:0/30")]
    [InlineData(5, 0, PlayerEventType.Reload, "m4a1")]
    public void Reload_WithHalfTheMagazineLeft_IsAFullClipReload(int clip, int maxClip, PlayerEventType expected, string value)
    {
        var fact = PlayerEvents.Reload(7, "weapon_m4a1", clip, maxClip, 1200, new Vec3(1, 2, 3));
        Assert.Equal(new PlayerEventFact(7, expected, 1200, value, new Vec3(1, 2, 3)), fact);
    }

    // item_remove fires when a grenade is thrown; the bomb has its own events.
    [Theory]
    [InlineData("weapon_hegrenade")]
    [InlineData("smokegrenade")]
    [InlineData("flashbang")]
    [InlineData("molotov")]
    [InlineData("incgrenade")]
    [InlineData("decoy")]
    [InlineData("weapon_c4")]
    [InlineData("")]
    [InlineData(null)]
    public void Item_IgnoresGrenadesAndTheBomb(string? item)
    {
        Assert.Null(PlayerEvents.Item(7, PlayerEventType.Pickup, item, 100, null));
        Assert.Null(PlayerEvents.Item(7, PlayerEventType.Drop, item, 100, null));
    }

    [Fact]
    public void Item_KeepsTheNormalizedWeapon() =>
        Assert.Equal(new PlayerEventFact(7, PlayerEventType.Drop, 100, "ak47", null), PlayerEvents.Item(7, PlayerEventType.Drop, "weapon_AK47", 100, null));

    [Fact]
    public void Item_OnlyForPickupsAndDrops() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => PlayerEvents.Item(7, PlayerEventType.Jump, "ak47", 100, null));

    [Fact]
    public void Simple_KeepsItsValue() =>
        Assert.Equal(new PlayerEventFact(7, PlayerEventType.BombPickup, 0, "start", null), PlayerEvents.Simple(7, PlayerEventType.BombPickup, 0, null, "start"));

    [Theory]
    [InlineData(40.5f, 40f, 500)]
    [InlineData(40f, 41f, 0)]
    [InlineData(45f, 5f, 40000)]
    public void BombClock_GivesTheTimeLeft(float blow, float now, int expected) => Assert.Equal(expected, BombClock.RemainingMs(blow, now));

    // A NaN would otherwise become 0 ms left, i.e. a clutch defuse.
    [Theory]
    [InlineData(float.NaN, 40f)]
    [InlineData(40f, float.NaN)]
    [InlineData(float.PositiveInfinity, 40f)]
    [InlineData(40f, float.NegativeInfinity)]
    public void BombClock_WithoutFiniteTimes_IsUnknown(float blow, float now) => Assert.Null(BombClock.RemainingMs(blow, now));

    [Fact]
    public void BombClock_IsCappedAtIntMax() => Assert.Equal(int.MaxValue, BombClock.RemainingMs(float.MaxValue, 0f));
}
