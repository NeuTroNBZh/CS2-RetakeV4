using RetakeV4.Domain.Common;
using RetakeV4.Domain.Geometry;

namespace RetakeV4.Domain.Stats;

// Collects the facts of one round on the game thread: appends only, no I/O. maxShots caps the shots and, separately,
// the player events of a round; MaxSamplesPerRound caps the movement samples.
public sealed class RoundRecorder
{
    // Memory bound: 10 players x 8 samples/s x 2 min stay under 10 000; 50 000 samples are about 2 MB.
    public const int MaxSamplesPerRound = 50_000;

    private readonly int _maxShots;
    private readonly int _minPlayersPerSide;
    private readonly List<ShotFired> _shots = new();
    private readonly List<DamageDealt> _damages = new();
    private readonly List<PlayerKilled> _kills = new();
    private readonly List<GrenadeThrown> _thrown = new();
    private readonly List<GrenadeDetonated> _detonated = new();
    private readonly List<PlayerBlinded> _blinds = new();
    private readonly List<MvpAwarded> _mvps = new();
    private readonly List<PlayerLeftRound> _leavers = new();
    private readonly List<PlayerEventFact> _events = new();
    private readonly Dictionary<ulong, PlayerEndState> _endStates = new();
    private readonly List<SmokeCloud> _smokes = new();
    private List<MovementSample> _samples = new();
    private BombPlantedFact? _plant;
    private BombDefusedFact? _defuse;
    private BombExplodedFact? _explosion;
    private int? _freezeEndMs;
    private bool _liveEnded;

    public RoundRecorder(int maxShots, int minPlayersPerSide)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxShots);
        ArgumentOutOfRangeException.ThrowIfLessThan(minPlayersPerSide, 1);
        _maxShots = maxShots;
        _minPlayersPerSide = minPlayersPerSide;
    }

    public RoundContext? Context { get; private set; }

    public bool IsOpen => Context is not null;

    // From the freeze end until EndLive (round_end) or the close.
    public bool IsLive => IsOpen && _freezeEndMs is not null && !_liveEnded;

    public int ShotsDropped { get; private set; }

    public int EventsDropped { get; private set; }

    public int SamplesDropped { get; private set; }

    public void Open(RoundContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        Reset();
        Context = context;
    }

    public void Abandon() => Reset();

    // False when the round is not open or the per-round cap is reached.
    public bool Add(ShotFired shot)
    {
        if (!IsOpen)
        {
            return false;
        }
        if (_shots.Count >= _maxShots)
        {
            ShotsDropped++;
            return false;
        }
        _shots.Add(shot);
        return true;
    }

    // False when the round is not open or the per-round cap is reached.
    public bool Add(PlayerEventFact playerEvent)
    {
        if (!IsOpen)
        {
            return false;
        }
        if (_events.Count >= _maxShots)
        {
            EventsDropped++;
            return false;
        }
        _events.Add(playerEvent);
        return true;
    }

    // False outside the live phase or when the per-round cap is reached.
    public bool Add(MovementSample sample)
    {
        if (!IsLive)
        {
            return false;
        }
        if (_samples.Count >= MaxSamplesPerRound)
        {
            SamplesDropped++;
            return false;
        }
        _samples.Add(sample);
        return true;
    }

    // The last state read for a player wins.
    public void Add(PlayerEndState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (IsOpen)
        {
            _endStates[state.Player] = state;
        }
    }

    // Only the first freeze end of the round counts.
    public void MarkFreezeEnd(int timeMs)
    {
        if (IsOpen && _freezeEndMs is null)
        {
            _freezeEndMs = timeMs;
        }
    }

    // round_end: no sample after it, even before the round closes on the next frame.
    public void EndLive()
    {
        if (IsOpen)
        {
            _liveEnded = true;
        }
    }

    public void AddSmoke(int entityId, int timeMs, Vec3 center)
    {
        if (IsOpen)
        {
            _smokes.Add(new SmokeCloud(entityId, timeMs, center));
        }
    }

    // Ends the latest cloud of that entity still open; an expiry without its detonation is ignored.
    public void EndSmoke(int entityId, int timeMs)
    {
        if (!IsOpen)
        {
            return;
        }
        for (var index = _smokes.Count - 1; index >= 0; index--)
        {
            if (_smokes[index].EntityId == entityId && _smokes[index].EndMs is null)
            {
                _smokes[index] = _smokes[index] with { EndMs = timeMs };
                return;
            }
        }
    }

    public void Add(DamageDealt damage) => AddIfOpen(_damages, damage);

    public void Add(PlayerKilled kill) => AddIfOpen(_kills, kill);

    public void Add(GrenadeThrown grenade) => AddIfOpen(_thrown, grenade);

    public void Add(GrenadeDetonated grenade) => AddIfOpen(_detonated, grenade);

    public void Add(PlayerBlinded blind) => AddIfOpen(_blinds, blind);

    public void Add(MvpAwarded mvp) => AddIfOpen(_mvps, mvp);

    public void Add(PlayerLeftRound leaver) => AddIfOpen(_leavers, leaver);

    public void Add(BombPlantedFact plant)
    {
        if (IsOpen)
        {
            _plant = plant;
        }
    }

    public void Add(BombDefusedFact defuse)
    {
        if (IsOpen)
        {
            _defuse = defuse;
        }
    }

    public void Add(BombExplodedFact explosion)
    {
        if (IsOpen)
        {
            _explosion = explosion;
        }
    }

    public RoundRecord? Close(RoundOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        if (Context is not { } context)
        {
            return null;
        }
        var ranked = context.Participants.Count(p => p.Side == TeamSide.CT) >= _minPlayersPerSide
            && context.Participants.Count(p => p.Side == TeamSide.T) >= _minPlayersPerSide;
        // The record takes the sample list itself (no copy of ~10 000 structs); the recorder starts a new one sized like
        // this round's, so that it does not regrow through large-object-heap arrays every round.
        var samples = _samples;
        _samples = new List<MovementSample>(Math.Min(samples.Count, MaxSamplesPerRound));
        var record = new RoundRecord(
            context, outcome, ranked,
            _shots.ToList(), _damages.ToList(), _kills.ToList(), _thrown.ToList(), _detonated.ToList(), _blinds.ToList(),
            _plant, _defuse, _explosion, _mvps.ToList(), _leavers.ToList())
        {
            Events = _events.ToList(),
            EndStates = _endStates.Values.ToList(),
            FreezeEndMs = _freezeEndMs,
            Samples = samples,
            Smokes = _smokes.ToList(),
        };
        Reset();
        return record;
    }

    private void AddIfOpen<T>(List<T> list, T fact)
    {
        if (IsOpen)
        {
            list.Add(fact);
        }
    }

    private void Reset()
    {
        Context = null;
        ShotsDropped = 0;
        EventsDropped = 0;
        SamplesDropped = 0;
        _shots.Clear();
        _damages.Clear();
        _kills.Clear();
        _thrown.Clear();
        _detonated.Clear();
        _blinds.Clear();
        _mvps.Clear();
        _leavers.Clear();
        _events.Clear();
        _endStates.Clear();
        _smokes.Clear();
        _samples.Clear();
        _plant = null;
        _defuse = null;
        _explosion = null;
        _freezeEndMs = null;
        _liveEnded = false;
    }
}
