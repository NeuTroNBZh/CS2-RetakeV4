using RetakeV4.Domain.Common;
using RetakeV4.Domain.Geometry;

namespace RetakeV4.Domain.Stats;

// Movement and behaviour of each participant during the live phase (freeze end to round end), from the samples taken
// every SampleEveryTicks. Without a freeze end there is no live phase: nothing is computed. The server knows no map
// geometry: "behind a smoke" means a smoke is on the line to the nearest enemy, wall or not.
public static class MovementAnalyzer
{
    // Below this horizontal speed (u/s) a sample is still.
    public const float StillSpeed = 10f;

    // An AFK period ends on a move of more than 2 m or a turn of more than 5 degrees from its start, or on a shot.
    public const float AfkMoveUnits = (float)(2 * MovementRoundStats.UnitsPerMeter);

    public const float AfkTurnDegrees = 5f;

    // A smoke cloud lasts at most this long, expired or not.
    public const int SmokeLifetimeMs = 18_000;

    internal static Dictionary<ulong, MovementRoundStats> Analyze(RoundRecord record, MovementRules rules, IReadOnlyDictionary<ulong, int> deathTimes)
    {
        var result = new Dictionary<ulong, MovementRoundStats>();
        if (record.FreezeEndMs is not { } freezeEnd)
        {
            return result;
        }
        var roundEnd = Math.Max(freezeEnd, record.Outcome.DurationMs);
        var sides = record.Context.Participants.ToDictionary(p => p.SteamId, p => p.Side);
        var samples = LiveSamples(record, sides, deathTimes, freezeEnd, roundEnd);
        var afkMs = (int)Math.Round(rules.AfkSeconds * 1000);
        foreach (var id in sides.Keys)
        {
            var own = samples.TryGetValue(id, out var list) ? list : new List<MovementSample>();
            var aliveEnd = deathTimes.TryGetValue(id, out var died) ? Math.Clamp(died, freezeEnd, roundEnd) : roundEnd;
            result[id] = Basic(own, aliveEnd - freezeEnd, ShotTimes(record, id), afkMs);
        }
        ApplySmokes(record, rules, sides, samples, result);
        return result;
    }

    // Samples of participants inside the live phase while alive, per player, in time order.
    private static Dictionary<ulong, List<MovementSample>> LiveSamples(
        RoundRecord record, IReadOnlyDictionary<ulong, TeamSide> sides, IReadOnlyDictionary<ulong, int> deathTimes, int freezeEnd, int roundEnd)
    {
        var result = new Dictionary<ulong, List<MovementSample>>();
        foreach (var sample in record.Samples)
        {
            if (!sides.ContainsKey(sample.Player) || sample.TimeMs < freezeEnd || sample.TimeMs > roundEnd
                || (deathTimes.TryGetValue(sample.Player, out var died) && sample.TimeMs >= died))
            {
                continue;
            }
            if (!result.TryGetValue(sample.Player, out var list))
            {
                list = new List<MovementSample>();
                result[sample.Player] = list;
            }
            list.Add(sample);
        }
        foreach (var id in result.Keys.ToList())
        {
            result[id] = result[id].OrderBy(s => s.TimeMs).ToList();
        }
        return result;
    }

    // Every weapon_fire counts as activity for the AFK rule, grenades included.
    private static List<int> ShotTimes(RoundRecord record, ulong player) =>
        record.Shots.Where(s => s.Shooter == player).Select(s => s.TimeMs).Order().ToList();

    // Exclusive states, so that the five shares add up to 100 %: in the air, else crouched, else still, else walking;
    // anything else is running.
    private static MovementRoundStats Basic(List<MovementSample> samples, int aliveMs, List<int> shotTimes, int afkMs)
    {
        int walk = 0, run = 0, crouch = 0, still = 0, air = 0;
        var distance = 0.0;
        for (var index = 0; index < samples.Count; index++)
        {
            var sample = samples[index];
            if (sample.Airborne)
            {
                air++;
            }
            else if (sample.Crouched)
            {
                crouch++;
            }
            else if (sample.Speed < StillSpeed)
            {
                still++;
            }
            else if (sample.Walking)
            {
                walk++;
            }
            else
            {
                run++;
            }
            if (index > 0)
            {
                distance += Horizontal(sample.Position - samples[index - 1].Position);
            }
        }
        return new MovementRoundStats
        {
            AliveMs = aliveMs,
            AliveSamples = samples.Count,
            WalkSamples = walk,
            RunSamples = run,
            CrouchSamples = crouch,
            StillSamples = still,
            AirSamples = air,
            DistanceUnits = distance,
            AfkMs = Afk(samples, shotTimes, afkMs),
        };
    }

    // A quiet period starts at a sample and lasts while later samples stay within AfkMoveUnits (horizontally) and
    // AfkTurnDegrees (yaw, pitch) of it with no shot; it counts, first to last quiet sample, when it lasts afkMs or more.
    private static int Afk(List<MovementSample> samples, List<int> shotTimes, int afkMs)
    {
        if (samples.Count == 0)
        {
            return 0;
        }
        var total = 0;
        var start = samples[0];
        var last = start;
        var nextShot = 0;
        for (var index = 1; index < samples.Count; index++)
        {
            var sample = samples[index];
            var fired = false;
            while (nextShot < shotTimes.Count && shotTimes[nextShot] <= sample.TimeMs)
            {
                fired |= shotTimes[nextShot] > last.TimeMs;
                nextShot++;
            }
            if (fired
                || Horizontal(sample.Position - start.Position) > AfkMoveUnits
                || MathF.Abs(AngleMath.NormalizeDegrees(sample.Yaw - start.Yaw)) > AfkTurnDegrees
                || MathF.Abs(sample.Pitch - start.Pitch) > AfkTurnDegrees)
            {
                total += Quiet(start, last, afkMs);
                start = sample;
            }
            last = sample;
        }
        return total + Quiet(start, last, afkMs);
    }

    private static int Quiet(MovementSample start, MovementSample last, int afkMs)
    {
        var span = last.TimeMs - start.TimeMs;
        return span >= afkMs ? span : 0;
    }

    // In a smoke: the eyes are inside an active cloud. Behind a smoke: not in one, and the line from the eyes to the eyes
    // of the nearest living enemy crosses an active cloud. Every human is sampled in the same tick, so samples with the
    // same time are a snapshot of the living players; bots are never sampled.
    private static void ApplySmokes(RoundRecord record, MovementRules rules, IReadOnlyDictionary<ulong, TeamSide> sides,
        Dictionary<ulong, List<MovementSample>> samples, Dictionary<ulong, MovementRoundStats> result)
    {
        if (record.Smokes.Count == 0)
        {
            return;
        }
        var snapshots = samples.Values.SelectMany(s => s).GroupBy(s => s.TimeMs).ToDictionary(g => g.Key, g => g.ToList());
        var active = new List<SmokeCloud>();
        foreach (var (id, own) in samples)
        {
            var inSmoke = 0;
            var behind = 0;
            foreach (var sample in own)
            {
                ActiveAt(record.Smokes, sample.TimeMs, active);
                if (active.Count == 0)
                {
                    continue;
                }
                var eyes = sample.Eyes;
                if (Inside(active, eyes, rules.SmokeRadius))
                {
                    inSmoke++;
                }
                else if (NearestEnemy(sample, sides, snapshots[sample.TimeMs]) is { } enemy && Crosses(active, eyes, enemy.Eyes, rules.SmokeRadius))
                {
                    behind++;
                }
            }
            result[id] = result[id] with { InSmokeSamples = inSmoke, BehindSmokeSamples = behind };
        }
    }

    // A cloud is active from its detonation until its expiry or SmokeLifetimeMs, whichever comes first.
    private static void ActiveAt(IReadOnlyList<SmokeCloud> smokes, int timeMs, List<SmokeCloud> active)
    {
        active.Clear();
        foreach (var smoke in smokes)
        {
            var end = Math.Min(smoke.EndMs ?? int.MaxValue, smoke.StartMs + SmokeLifetimeMs);
            if (timeMs >= smoke.StartMs && timeMs < end)
            {
                active.Add(smoke);
            }
        }
    }

    private static bool Inside(List<SmokeCloud> active, Vec3 point, float radius)
    {
        foreach (var smoke in active)
        {
            if ((point - smoke.Center).Length <= radius)
            {
                return true;
            }
        }
        return false;
    }

    private static bool Crosses(List<SmokeCloud> active, Vec3 from, Vec3 to, float radius)
    {
        foreach (var smoke in active)
        {
            if (Segments.DistanceToSegment(smoke.Center, from, to) <= radius)
            {
                return true;
            }
        }
        return false;
    }

    private static MovementSample? NearestEnemy(MovementSample sample, IReadOnlyDictionary<ulong, TeamSide> sides, List<MovementSample> snapshot)
    {
        MovementSample? nearest = null;
        var best = float.MaxValue;
        foreach (var other in snapshot)
        {
            if (sides[other.Player] == sides[sample.Player])
            {
                continue;
            }
            var distance = (other.Position - sample.Position).Length;
            if (distance < best)
            {
                best = distance;
                nearest = other;
            }
        }
        return nearest;
    }

    private static float Horizontal(Vec3 v) => MathF.Sqrt((v.X * v.X) + (v.Y * v.Y));
}
