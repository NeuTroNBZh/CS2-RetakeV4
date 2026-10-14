using RetakeV4.Domain.Geometry;

namespace RetakeV4.Domain.Stats;

// Bit values of the replay frame flags (stats v2 spec, section 3.2): 1 crouched, 2 walking, 4 airborne. A replay encoder
// adds 8 (in a smoke) and 16 (bomb carrier) from the round's smokes and bomb carry: they are not pawn state.
[Flags]
public enum MovementFlags : byte
{
    None = 0,
    Crouched = 1,
    Walking = 2,
    Airborne = 4,
}

// One sample of a living human during the live phase (freeze end to round end), every SampleEveryTicks. A struct: the
// sampling allocates nothing but the list slot. Position is the pawn origin (feet), Speed the horizontal speed (u/s),
// Yaw and Pitch the eye angles in degrees, TimeMs the time since the round started (as every other fact).
public readonly record struct MovementSample(
    ulong Player, int TimeMs, Vec3 Position, float Speed, float Yaw, float Pitch, MovementFlags Flags, int Health)
{
    // CS2 eye heights above the origin.
    public const float StandingEyeHeight = 64f;

    public const float CrouchedEyeHeight = 46f;

    public bool Crouched => (Flags & MovementFlags.Crouched) != 0;

    public bool Walking => (Flags & MovementFlags.Walking) != 0;

    public bool Airborne => (Flags & MovementFlags.Airborne) != 0;

    public Vec3 Eyes => new(Position.X, Position.Y, Position.Z + (Crouched ? CrouchedEyeHeight : StandingEyeHeight));

    public static MovementFlags FlagsOf(bool crouched, bool walking, bool airborne) =>
        (crouched ? MovementFlags.Crouched : MovementFlags.None)
        | (walking ? MovementFlags.Walking : MovementFlags.None)
        | (airborne ? MovementFlags.Airborne : MovementFlags.None);
}

// A smoke cloud from smokegrenade_detonate (any thrower, bots included: a cloud is geometry, not an action) until
// smokegrenade_expired; EndMs is null when the expiry was not seen.
public sealed record SmokeCloud(int EntityId, int StartMs, Vec3 Center)
{
    public int? EndMs { get; init; }
}

// Built from StatsConfig (the Domain holds no default values). Distances in game units.
public sealed record MovementRules(float SmokeRadius, float BaitDistance, double BaitWindowSeconds, double AfkSeconds);

// Movement and behaviour of one participant in one round (MovementAnalyzer). Percentages are of the alive samples (null
// without a sample); FirstContactMs counts from the freeze end; BaitFlag is null when the round gave no bait chance.
public sealed record MovementRoundStats
{
    public const double UnitsPerMeter = 39.37;

    public int AliveMs { get; init; }

    public int AliveSamples { get; init; }

    public int WalkSamples { get; init; }

    public int RunSamples { get; init; }

    public int CrouchSamples { get; init; }

    public int StillSamples { get; init; }

    public int AirSamples { get; init; }

    // Sum of the horizontal moves between consecutive samples, in game units.
    public double DistanceUnits { get; init; }

    public int AfkMs { get; init; }

    public int InSmokeSamples { get; init; }

    public int BehindSmokeSamples { get; init; }

    public int? FirstContactMs { get; init; }

    public int? EngageOrder { get; init; }

    public bool BaitChance { get; init; }

    public bool Bait { get; init; }

    public double? WalkPct => Pct(WalkSamples);

    public double? RunPct => Pct(RunSamples);

    public double? CrouchPct => Pct(CrouchSamples);

    public double? StillPct => Pct(StillSamples);

    public double? AirPct => Pct(AirSamples);

    public double? InSmokePct => Pct(InSmokeSamples);

    public double? BehindSmokePct => Pct(BehindSmokeSamples);

    public double DistanceMeters => Math.Round(DistanceUnits / UnitsPerMeter, 2, MidpointRounding.AwayFromZero);

    public long DistanceCm => (long)Math.Round(DistanceUnits * 100 / UnitsPerMeter, MidpointRounding.AwayFromZero);

    public bool? BaitFlag => BaitChance ? Bait : null;

    private double? Pct(int samples) =>
        AliveSamples == 0 ? null : Math.Round(100.0 * samples / AliveSamples, 2, MidpointRounding.AwayFromZero);
}
