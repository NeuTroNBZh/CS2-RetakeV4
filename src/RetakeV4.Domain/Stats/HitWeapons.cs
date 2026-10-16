namespace RetakeV4.Domain.Stats;

// player_hurt names some weapons by their base item (M4A1-S → m4a1, USP-S → hkp2000), while weapon_fire and player_death
// give the real one. A bullet hit takes the weapon of the shot fired by the same player in the same tick, so the hits,
// damage and raw damage rows of a weapon match its shots and kills. A hit with no such shot keeps the name of the event.
public static class HitWeapons
{
    public static RoundRecord Resolve(RoundRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        var fired = new Dictionary<(ulong, int), string>();
        foreach (var shot in record.Shots)
        {
            if (StatsWeapons.IsBullet(shot.Weapon))
            {
                fired.TryAdd((shot.Shooter, shot.Tick), shot.Weapon);
            }
        }
        if (fired.Count == 0)
        {
            return record;
        }
        var renamed = false;
        var damages = new List<DamageDealt>(record.Damages.Count);
        foreach (var hurt in record.Damages)
        {
            if (hurt.Attacker is { } attacker && StatsWeapons.IsBullet(hurt.Weapon)
                && fired.TryGetValue((attacker, hurt.Tick), out var weapon) && weapon != hurt.Weapon)
            {
                damages.Add(hurt with { Weapon = weapon });
                renamed = true;
            }
            else
            {
                damages.Add(hurt);
            }
        }
        return renamed ? record with { Damages = damages } : record;
    }
}
