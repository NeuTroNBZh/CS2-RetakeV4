using RetakeV4.Domain.Common;
using RetakeV4.Domain.Geometry;
using RetakeV4.Domain.Stats;
using static RetakeV4.Domain.Tests.Stats.StatsTestData;

namespace RetakeV4.Domain.Tests.Stats;

public class ReplayEncoderTests
{
    private const ulong Stranger = 99;

    private static MovementSample S(ulong id, int timeMs, float x, float y, float z, float yaw = 0f,
        MovementFlags flags = MovementFlags.None, int health = 100) =>
        new(id, timeMs, new Vec3(x, y, z), 0f, yaw, 0f, flags, health);

    // Freeze ends at 1000 ms so that the recorder accepts samples.
    private static RoundRecord Live(RoundContext context, Action<RoundRecorder> facts) =>
        Record(context, TeamSide.CT, r =>
        {
            r.MarkFreezeEnd(1000);
            facts(r);
        });

    [Fact]
    public void Encode_WritesOneTrackPerSampledParticipant_InParticipantOrder()
    {
        var record = Live(Context(P(Ct1, TeamSide.CT), P(T1, TeamSide.T), P(Ct2, TeamSide.CT)), r =>
        {
            r.Add(S(T1, 1000, 10.4f, -20.5f, 30.5f, yaw: 90.4f));
            r.Add(S(Ct1, 1000, 1f, 2f, 3f, yaw: -90f, flags: MovementFlags.Crouched | MovementFlags.Walking, health: 87));
            r.Add(S(Ct1, 1125, 1.5f, 2f, 3f, yaw: -90f, flags: MovementFlags.Airborne, health: 87));
            r.Add(S(Stranger, 1000, 0f, 0f, 0f));
        });

        var replay = ReplayEncoder.Encode(record, 72f, 8);

        Assert.NotNull(replay);
        Assert.Equal(8.0, replay.SampleHz);
        Assert.Equal(new[] { Ct1, T1 }, replay.Tracks.Select(t => t.SteamId));
        Assert.Equal(new[] { TeamSide.CT, TeamSide.T }, replay.Tracks.Select(t => t.Side));
        Assert.Equal("[[1000,1,2,3,270,3,87],[1125,2,2,3,270,4,87]]", replay.Tracks[0].Frames);
        Assert.Equal(2, replay.Tracks[0].FrameCount);
        Assert.Equal("[[1000,10,-21,31,90,0,100]]", replay.Tracks[1].Frames);
        Assert.Equal(1, replay.Tracks[1].FrameCount);
        Assert.Equal(replay.Tracks[0].Frames.Length + replay.Tracks[1].Frames.Length, replay.Bytes);
    }

    [Fact]
    public void Encode_OrdersFramesByTime()
    {
        var record = Live(Context(P(Ct1, TeamSide.CT), P(T1, TeamSide.T)), r =>
        {
            r.Add(S(Ct1, 1250, 3f, 0f, 0f));
            r.Add(S(Ct1, 1125, 2f, 0f, 0f));
        });

        var replay = ReplayEncoder.Encode(record, 72f, 8)!;

        Assert.Equal("[[1125,2,0,0,0,0,100],[1250,3,0,0,0,0,100]]", Assert.Single(replay.Tracks).Frames);
    }

    [Fact]
    public void Encode_IsNull_WithoutAParticipantSample()
    {
        var record = Live(Context(P(Ct1, TeamSide.CT), P(T1, TeamSide.T)), r => r.Add(S(Stranger, 1000, 0f, 0f, 0f)));

        Assert.Null(ReplayEncoder.Encode(record, 72f, 8));
    }

    // In a smoke: the eyes (64 u above a standing player's feet) inside an active cloud; the cloud ends at its expiry.
    [Fact]
    public void Encode_FlagsInSmoke_AtTheEyes_WhileTheCloudIsActive()
    {
        var record = Live(Context(P(Ct1, TeamSide.CT), P(T1, TeamSide.T)), r =>
        {
            r.AddSmoke(5, 1000, new Vec3(0f, 0f, 64f));
            r.Add(S(Ct1, 1000, 0f, 0f, 0f));
            r.Add(S(Ct1, 1500, 500f, 0f, 0f));
            r.EndSmoke(5, 2000);
            r.Add(S(Ct1, 2000, 0f, 0f, 0f));
        });

        var frames = ReplayEncoder.Encode(record, 72f, 8)!.Tracks[0].Frames;

        Assert.Equal("[[1000,0,0,0,0,8,100],[1500,500,0,0,0,0,100],[2000,0,0,0,0,0,100]]", frames);
    }

    // The carrier from the bomb_pickup until the plant (end excluded); crouched + carrier = 17.
    [Fact]
    public void Encode_FlagsTheBombCarrier_UntilThePlant()
    {
        var record = Live(Context(P(Ct1, TeamSide.CT), P(T1, TeamSide.T)), r =>
        {
            r.Add(new PlayerEventFact(T1, PlayerEventType.BombPickup, 0, "start", null));
            r.Add(S(T1, 1000, 0f, 0f, 0f, flags: MovementFlags.Crouched));
            r.Add(new BombPlantedFact(T1, 1500));
            r.Add(S(T1, 1500, 0f, 0f, 0f));
        });

        var frames = ReplayEncoder.Encode(record, 72f, 8)!.Tracks[0].Frames;

        Assert.Equal("[[1000,0,0,0,0,17,100],[1500,0,0,0,0,0,100]]", frames);
    }

    [Theory]
    [InlineData(-90f, 270)]
    [InlineData(359.6f, 0)]
    [InlineData(180f, 180)]
    [InlineData(-180f, 180)]
    [InlineData(0.4f, 0)]
    [InlineData(-0.5f, 359)]
    [InlineData(720.2f, 0)]
    public void Yaw_IsWrappedTo0To359(float yaw, int expected) => Assert.Equal(expected, ReplayEncoder.Yaw(yaw));

    [Theory]
    [InlineData(2.5f, 3)]
    [InlineData(-2.5f, -3)]
    [InlineData(2.4f, 2)]
    [InlineData(-1234.6f, -1235)]
    public void Frame_RoundsAwayFromZero(float value, int expected) => Assert.Equal(expected, ReplayEncoder.Coordinate(value));

    [Fact]
    public void Frame_WritesNonFiniteValuesAsZero_AndNegativeHealthAsZero()
    {
        var sample = new MovementSample(Ct1, 40, new Vec3(float.NaN, float.PositiveInfinity, 3f), 0f, float.NaN, 0f, MovementFlags.None, -5);

        Assert.Equal("[40,0,0,3,0,0,0]", ReplayEncoder.Frame(sample, ReplayFrameFlags.None));
    }

    [Theory]
    [InlineData(1.0 / 64, 8, 8.0)]
    [InlineData(1.0 / 64, 3, 21.33)]
    [InlineData(0.0, 8, 0.0)]
    [InlineData(1.0 / 64, 0, 0.0)]
    public void SampleHz_FollowsTheTickInterval(double interval, int everyTicks, double expected) =>
        Assert.Equal(expected, ReplayEncoder.SampleHz(interval, everyTicks));
}
