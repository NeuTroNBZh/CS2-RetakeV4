using RetakeV4.Domain.Common;

namespace RetakeV4.Domain.Stats;

// Turns the facts of one round into per-player stats. Only participants (humans on a side at round start) get stats;
// facts about anyone else stay in the raw tables only.
public static class RoundAnalyzer
{
    private const int StartHealth = 100;

    // A defuse with less than this left on the bomb is a clutch defuse.
    public const int ClutchDefuseMs = 1000;

    public static RoundSummary Analyze(RoundRecord record, StatsRules rules)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(rules);
        var sides = record.Context.Participants.ToDictionary(p => p.SteamId, p => p.Side);
        var players = record.Context.Participants.ToDictionary(p => p.SteamId, p => new PlayerAccumulator(p));
        var kills = record.Kills.OrderBy(k => k.TimeMs).ToList();
        var deathTimes = CountKills(kills, sides, players);
        CountLeavers(record.Leavers, players, deathTimes);
        CountTrades(kills, sides, players, rules.TradeWindowSeconds);
        CountDamage(record.Damages, sides, players);
        CountShots(record, players);
        CountFlashes(record.Blinds, sides, players);
        CountObjectives(record, players);
        CountEvents(record, players, deathTimes);
        CountBombCarry(record, players, deathTimes);
        ApplyEndStates(record, players, deathTimes);
        ApplyMovement(record, rules, players, deathTimes);
        MarkClutches(sides, players, deathTimes);
        var winner = record.Outcome.Winner;
        return new RoundSummary(
            record,
            record.Context.Participants.Select(p => players[p.SteamId].Build(winner, deathTimes.ContainsKey(p.SteamId))).ToList(),
            GrenadeLinker.Link(record, sides));
    }

    private static bool IsEnemyKill(PlayerKilled kill, IReadOnlyDictionary<ulong, TeamSide> sides) =>
        kill.Attacker is { } attacker
        && attacker != kill.Victim
        && !kill.VictimIsBot
        && sides.TryGetValue(attacker, out var attackerSide)
        && sides.TryGetValue(kill.Victim, out var victimSide)
        && attackerSide != victimSide;

    // A victim dies once: a duplicated death event counts neither a second death nor a second kill.
    private static Dictionary<ulong, int> CountKills(
        List<PlayerKilled> kills, IReadOnlyDictionary<ulong, TeamSide> sides, Dictionary<ulong, PlayerAccumulator> players)
    {
        var deathTimes = new Dictionary<ulong, int>();
        var entryTaken = false;
        for (var index = 0; index < kills.Count; index++)
        {
            var kill = kills[index];
            if (!kill.VictimIsBot && players.TryGetValue(kill.Victim, out var victim))
            {
                if (!deathTimes.TryAdd(kill.Victim, kill.TimeMs))
                {
                    // Duplicate: drop it so that trades do not see it either.
                    kills.RemoveAt(index);
                    index--;
                    continue;
                }
                victim.Deaths++;
            }
            if (kill.Attacker is not { } attackerId || !players.TryGetValue(attackerId, out var attacker))
            {
                continue;
            }
            if (!IsEnemyKill(kill, sides))
            {
                if (attackerId != kill.Victim && !kill.VictimIsBot && sides.TryGetValue(kill.Victim, out var victimSide) && victimSide == sides[attackerId])
                {
                    attacker.TeamKills++;
                }
                continue;
            }
            attacker.Kills++;
            var weapon = attacker.Weapon(kill.Weapon);
            weapon.Kills++;
            if (kill.Headshot)
            {
                attacker.HeadshotKills++;
                weapon.HeadshotKills++;
            }
            if (kill.Assister is { } assisterId && assisterId != attackerId && players.TryGetValue(assisterId, out var assister)
                && sides[assisterId] == sides[attackerId])
            {
                assister.Assists++;
                if (kill.FlashAssist)
                {
                    assister.FlashAssists++;
                }
            }
            if (!entryTaken)
            {
                entryTaken = true;
                attacker.EntryKill = true;
                players[kill.Victim].EntryDeath = true;
            }
        }
        return deathTimes;
    }

    private static void CountLeavers(IReadOnlyList<PlayerLeftRound> leavers, Dictionary<ulong, PlayerAccumulator> players, Dictionary<ulong, int> deathTimes)
    {
        foreach (var leaver in leavers.OrderBy(l => l.TimeMs))
        {
            if (players.TryGetValue(leaver.Player, out var player) && deathTimes.TryAdd(leaver.Player, leaver.TimeMs))
            {
                player.Deaths++;
            }
        }
    }

    // Each revenge kill trades at most one earlier death of the avenger's teammate.
    private static void CountTrades(
        IReadOnlyList<PlayerKilled> kills, IReadOnlyDictionary<ulong, TeamSide> sides, Dictionary<ulong, PlayerAccumulator> players, double windowSeconds)
    {
        var windowMs = (int)Math.Round(windowSeconds * 1000);
        var avenging = new HashSet<int>();
        var counted = kills.Select(k => IsEnemyKill(k, sides)).ToList();
        for (var i = 0; i < kills.Count; i++)
        {
            var first = kills[i];
            if (!counted[i])
            {
                continue;
            }
            for (var j = i + 1; j < kills.Count && kills[j].TimeMs - first.TimeMs <= windowMs; j++)
            {
                var revenge = kills[j];
                if (avenging.Contains(j) || !counted[j] || revenge.Victim != first.Attacker || sides[revenge.Attacker!.Value] != sides[first.Victim])
                {
                    continue;
                }
                avenging.Add(j);
                players[revenge.Attacker.Value].TradeKills++;
                players[first.Victim].TradedDeath = true;
                break;
            }
        }
    }

    // Damage is capped by the victim's remaining health (rounds start at full health); team damage and bots do not count.
    private static void CountDamage(IReadOnlyList<DamageDealt> damages, IReadOnlyDictionary<ulong, TeamSide> sides, Dictionary<ulong, PlayerAccumulator> players)
    {
        var health = new Dictionary<ulong, int>();
        foreach (var hurt in damages.OrderBy(d => d.TimeMs).ThenBy(d => d.Tick))
        {
            if (hurt.VictimIsBot || !sides.TryGetValue(hurt.Victim, out var victimSide))
            {
                continue;
            }
            var before = health.GetValueOrDefault(hurt.Victim, StartHealth);
            var dealt = Math.Clamp(hurt.DmgHealth, 0, before);
            health[hurt.Victim] = Math.Clamp(hurt.HealthAfter, 0, StartHealth);
            players[hurt.Victim].DamageTaken += dealt;
            if (hurt.Attacker is not { } attackerId || !players.TryGetValue(attackerId, out var attacker) || sides[attackerId] == victimSide)
            {
                continue;
            }
            attacker.Damage += dealt;
            attacker.Weapon(hurt.Weapon).Damage += dealt;
            if (StatsWeapons.IsUtility(hurt.Weapon))
            {
                attacker.UtilityDamage += dealt;
            }
        }
    }

    // A hit is one (attacker, tick) pair: the pellets of one shotgun shot land in the same tick and count once. A hit
    // takes the motion of the shot fired by the same player in the same tick.
    private static void CountShots(RoundRecord record, Dictionary<ulong, PlayerAccumulator> players)
    {
        var motions = new Dictionary<(ulong, int), (bool Still, bool Scoped)>();
        foreach (var shot in record.Shots)
        {
            if (!players.TryGetValue(shot.Shooter, out var shooter) || !StatsWeapons.IsBullet(shot.Weapon))
            {
                continue;
            }
            shooter.Shots++;
            shooter.Weapon(shot.Weapon).Shots++;
            if (shot.Motion is not { } motion)
            {
                continue;
            }
            var still = StatsWeapons.IsStill(shot.Weapon, motion);
            motions.TryAdd((shot.Shooter, shot.Tick), (still, motion.Scoped));
            if (still)
            {
                shooter.ShotsStill++;
            }
            else
            {
                shooter.ShotsMoving++;
            }
            if (motion.Scoped)
            {
                shooter.ShotsScoped++;
            }
        }
        var hits = new HashSet<(ulong, int)>();
        var headHits = new HashSet<(ulong, int)>();
        foreach (var hurt in record.Damages)
        {
            if (hurt.VictimIsBot || hurt.Attacker is not { } attackerId || attackerId == hurt.Victim
                || !players.TryGetValue(attackerId, out var attacker) || !StatsWeapons.IsBullet(hurt.Weapon))
            {
                continue;
            }
            if (hits.Add((attackerId, hurt.Tick)))
            {
                attacker.Hits++;
                attacker.Weapon(hurt.Weapon).Hits++;
                if (motions.TryGetValue((attackerId, hurt.Tick), out var shot))
                {
                    if (shot.Still)
                    {
                        attacker.HitsStill++;
                    }
                    else
                    {
                        attacker.HitsMoving++;
                    }
                    if (shot.Scoped)
                    {
                        attacker.HitsScoped++;
                    }
                }
            }
            if (hurt.Hitgroup == StatsWeapons.HeadHitgroup && headHits.Add((attackerId, hurt.Tick)))
            {
                attacker.HeadshotHits++;
                attacker.Weapon(hurt.Weapon).HeadshotHits++;
            }
        }
    }

    private static void CountFlashes(IReadOnlyList<PlayerBlinded> blinds, IReadOnlyDictionary<ulong, TeamSide> sides, Dictionary<ulong, PlayerAccumulator> players)
    {
        foreach (var blind in blinds)
        {
            if (blind.Seconds <= 0 || blind.Attacker is not { } attackerId || attackerId == blind.Victim
                || !players.TryGetValue(attackerId, out var attacker) || !sides.TryGetValue(blind.Victim, out var victimSide))
            {
                continue;
            }
            if (victimSide == sides[attackerId])
            {
                attacker.TeammatesFlashed++;
            }
            else
            {
                attacker.EnemiesFlashed++;
                attacker.BlindSeconds += blind.Seconds;
            }
        }
    }

    private static void CountObjectives(RoundRecord record, Dictionary<ulong, PlayerAccumulator> players)
    {
        if (record.Plant is { } plant && players.TryGetValue(plant.Planter, out var planter))
        {
            planter.Planted = true;
        }
        if (record.Defuse is { } defuse && players.TryGetValue(defuse.Defuser, out var defuser))
        {
            defuser.Defused = true;
            defuser.DefusedWithKit = defuse.WithKit;
            defuser.ClutchDefuse = defuse.RemainingMs is >= 0 and < ClutchDefuseMs;
        }
        foreach (var mvp in record.Mvps)
        {
            if (players.TryGetValue(mvp.Player, out var player))
            {
                player.Mvp = true;
            }
        }
    }

    // Pickups before the freeze ends are the spawn equipment (without a freeze end: those at time 0, the loadout given at
    // the round start); drops from the player's death on are the death drop.
    private static void CountEvents(RoundRecord record, Dictionary<ulong, PlayerAccumulator> players, Dictionary<ulong, int> deathTimes)
    {
        var freezeEnd = record.FreezeEndMs ?? 0;
        foreach (var playerEvent in record.Events)
        {
            if (!players.TryGetValue(playerEvent.Player, out var player))
            {
                continue;
            }
            switch (playerEvent.Type)
            {
                case PlayerEventType.Reload:
                    player.Reloads++;
                    break;
                case PlayerEventType.ReloadFullClip:
                    player.Reloads++;
                    player.ReloadsFull++;
                    break;
                case PlayerEventType.Pickup:
                    if (record.FreezeEndMs is null ? playerEvent.TimeMs > 0 : playerEvent.TimeMs >= freezeEnd)
                    {
                        player.Pickups++;
                    }
                    break;
                case PlayerEventType.Drop:
                    if (playerEvent.TimeMs >= freezeEnd && !DeadBy(deathTimes, playerEvent.Player, playerEvent.TimeMs))
                    {
                        player.Drops++;
                    }
                    break;
                case PlayerEventType.Jump:
                    player.Jumps++;
                    break;
                case PlayerEventType.PlantBegin:
                    player.PlantAttempts++;
                    break;
                case PlayerEventType.PlantAbort:
                    player.PlantAborts++;
                    break;
                case PlayerEventType.DefuseBegin:
                    player.DefuseAttempts++;
                    break;
                case PlayerEventType.DefuseAbort:
                    player.DefuseAborts++;
                    break;
            }
        }
    }

    private static bool DeadBy(Dictionary<ulong, int> deathTimes, ulong player, int timeMs) =>
        deathTimes.TryGetValue(player, out var died) && timeMs >= died;

    private static void CountBombCarry(RoundRecord record, Dictionary<ulong, PlayerAccumulator> players, Dictionary<ulong, int> deathTimes)
    {
        foreach (var (player, carriedMs) in BombCarry.Compute(record, deathTimes))
        {
            if (players.TryGetValue(player, out var carrier))
            {
                carrier.BombCarryMs = carriedMs;
            }
        }
    }

    // A dead (or departed) player ends with 0 and 0; a living one with the last state read at round_end, if any.
    private static void ApplyMovement(RoundRecord record, StatsRules rules, Dictionary<ulong, PlayerAccumulator> players, Dictionary<ulong, int> deathTimes)
    {
        if (rules.Movement is not { } movement)
        {
            return;
        }
        foreach (var (id, stats) in MovementAnalyzer.Analyze(record, movement, deathTimes))
        {
            players[id].Movement = stats;
        }
    }

    private static void ApplyEndStates(RoundRecord record, Dictionary<ulong, PlayerAccumulator> players, Dictionary<ulong, int> deathTimes)
    {
        var states = record.EndStates.GroupBy(s => s.Player).ToDictionary(g => g.Key, g => g.Last());
        foreach (var (id, player) in players)
        {
            if (deathTimes.ContainsKey(id))
            {
                player.HealthEnd = 0;
                player.ArmorEnd = 0;
            }
            else if (states.TryGetValue(id, out var state))
            {
                player.HealthEnd = Math.Max(0, state.Health);
                player.ArmorEnd = Math.Max(0, state.Armor);
            }
        }
    }

    // A player is in a clutch from the moment they are the last of their side alive against at least one opponent;
    // X is the number of opponents alive at that moment.
    private static void MarkClutches(IReadOnlyDictionary<ulong, TeamSide> sides, Dictionary<ulong, PlayerAccumulator> players, Dictionary<ulong, int> deathTimes)
    {
        var alive = sides.Keys.ToHashSet();
        Check();
        foreach (var death in deathTimes.OrderBy(d => d.Value).ThenBy(d => d.Key))
        {
            alive.Remove(death.Key);
            Check();
        }

        void Check()
        {
            foreach (var side in new[] { TeamSide.CT, TeamSide.T })
            {
                var mine = alive.Where(p => sides[p] == side).ToList();
                var opponents = alive.Count(p => sides[p] != side);
                if (mine.Count == 1 && opponents >= 1 && players[mine[0]].ClutchVs == 0)
                {
                    players[mine[0]].ClutchVs = opponents;
                }
            }
        }
    }
}
