using System.Globalization;
using System.Text;

namespace RetakeV4.Domain.Stats;

// Turns the movement samples of a closed round into replay tracks (stats v2 spec, section 3.2): one track per human
// participant with samples, frames [t_ms, x, y, z, yaw, flags, hp] in time order. t_ms counts from the round start, like
// every other fact (kills, grenades, plant_time_ms); coordinates are game units rounded to the integer; yaw is rounded to
// the degree and wrapped into 0-359 (0 = +X, counter-clockwise seen from above). Pure: the module calls it off the game
// thread, before RoundSummary.WithoutSamples drops the samples. Bots are never sampled, so never in a replay.
public static class ReplayEncoder
{
    // About 35 characters per frame.
    private const int FrameCapacity = 36;

    private static readonly IReadOnlyDictionary<ulong, int> NoDeaths = new Dictionary<ulong, int>();

    public static double SampleHz(double tickIntervalSeconds, int sampleEveryTicks) =>
        tickIntervalSeconds <= 0 || sampleEveryTicks <= 0
            ? 0
            : Math.Round(1.0 / (tickIntervalSeconds * sampleEveryTicks), 2, MidpointRounding.AwayFromZero);

    // Null when no participant has a sample (sampling off, no freeze end seen).
    public static ReplayRound? Encode(RoundRecord record, float smokeRadius, double sampleHz)
    {
        ArgumentNullException.ThrowIfNull(record);
        var participants = record.Context.Participants.Select(p => p.SteamId).ToHashSet();
        var samples = record.Samples
            .Where(s => participants.Contains(s.Player))
            .GroupBy(s => s.Player)
            .ToDictionary(g => g.Key, g => g.OrderBy(s => s.TimeMs).ToList());
        if (samples.Count == 0)
        {
            return null;
        }
        var carries = BombCarry.Intervals(record, NoDeaths);
        var active = new List<SmokeCloud>();
        var tracks = new List<ReplayTrack>();
        foreach (var participant in record.Context.Participants)
        {
            if (!samples.TryGetValue(participant.SteamId, out var own))
            {
                continue;
            }
            var builder = new StringBuilder((own.Count * FrameCapacity) + 2);
            builder.Append('[');
            for (var index = 0; index < own.Count; index++)
            {
                if (index > 0)
                {
                    builder.Append(',');
                }
                AppendFrame(builder, own[index], FlagsOf(own[index], record, carries, active, smokeRadius));
            }
            builder.Append(']');
            tracks.Add(new ReplayTrack(participant.SteamId, participant.Side, builder.ToString(), own.Count));
        }
        return new ReplayRound(sampleHz, tracks);
    }

    public static string Frame(MovementSample sample, ReplayFrameFlags flags)
    {
        var builder = new StringBuilder(FrameCapacity);
        AppendFrame(builder, sample, flags);
        return builder.ToString();
    }

    // Away from zero, like the other rounded stats; a non-finite value (never expected from the engine) is written 0.
    public static int Coordinate(float value) =>
        float.IsFinite(value) ? (int)Math.Round((double)value, MidpointRounding.AwayFromZero) : 0;

    public static int Yaw(float yaw)
    {
        var degrees = Coordinate(yaw) % 360;
        return degrees < 0 ? degrees + 360 : degrees;
    }

    private static ReplayFrameFlags FlagsOf(MovementSample sample, RoundRecord record, List<CarryInterval> carries, List<SmokeCloud> active, float smokeRadius)
    {
        var flags = (ReplayFrameFlags)(int)sample.Flags;
        MovementAnalyzer.ActiveAt(record.Smokes, sample.TimeMs, active);
        if (active.Count > 0 && MovementAnalyzer.Inside(active, sample.Eyes, smokeRadius))
        {
            flags |= ReplayFrameFlags.InSmoke;
        }
        foreach (var carry in carries)
        {
            if (carry.Player == sample.Player && sample.TimeMs >= carry.StartMs && sample.TimeMs < carry.EndMs)
            {
                flags |= ReplayFrameFlags.BombCarrier;
                break;
            }
        }
        return flags;
    }

    private static void AppendFrame(StringBuilder builder, MovementSample sample, ReplayFrameFlags flags)
    {
        builder.Append('[')
            .Append(sample.TimeMs.ToString(CultureInfo.InvariantCulture)).Append(',')
            .Append(Coordinate(sample.Position.X).ToString(CultureInfo.InvariantCulture)).Append(',')
            .Append(Coordinate(sample.Position.Y).ToString(CultureInfo.InvariantCulture)).Append(',')
            .Append(Coordinate(sample.Position.Z).ToString(CultureInfo.InvariantCulture)).Append(',')
            .Append(Yaw(sample.Yaw).ToString(CultureInfo.InvariantCulture)).Append(',')
            .Append(((int)flags).ToString(CultureInfo.InvariantCulture)).Append(',')
            .Append(Math.Max(0, sample.Health).ToString(CultureInfo.InvariantCulture))
            .Append(']');
    }
}
