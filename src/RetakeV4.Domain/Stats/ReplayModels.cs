using System.Text;
using RetakeV4.Domain.Common;

namespace RetakeV4.Domain.Stats;

// Bits of a replay frame's flags (stats v2 spec, section 3.2). 1, 2 and 4 are the MovementFlags of the sample; 8 and 16
// are computed by ReplayEncoder from the round's smokes and bomb carry.
[Flags]
public enum ReplayFrameFlags
{
    None = 0,
    Crouched = 1,
    Walking = 2,
    Airborne = 4,
    InSmoke = 8,
    BombCarrier = 16,
}

// One human participant's trajectory. Frames is the compact JSON text of stats_replay_tracks.frames:
// [[t_ms, x, y, z, yaw, flags, hp], ...] in time order.
public sealed record ReplayTrack(ulong SteamId, TeamSide Side, string Frames, int FrameCount);

// The trajectories of one round (participants with at least one sample). Bytes is the UTF-8 size of every Frames text
// (stats_replays.bytes); the text is ASCII, so it is also its length.
public sealed record ReplayRound(double SampleHz, IReadOnlyList<ReplayTrack> Tracks)
{
    public int Bytes { get; } = Tracks.Sum(t => Encoding.UTF8.GetByteCount(t.Frames));
}
