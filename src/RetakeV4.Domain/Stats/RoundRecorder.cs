using RetakeV4.Domain.Common;

namespace RetakeV4.Domain.Stats;

// Collects the facts of one round on the game thread: appends only, no I/O. maxShots caps the shots and, separately,
// the player events of a round.
public sealed class RoundRecorder
{
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
    private BombPlantedFact? _plant;
    private BombDefusedFact? _defuse;
    private BombExplodedFact? _explosion;
    private int? _freezeEndMs;

    public RoundRecorder(int maxShots, int minPlayersPerSide)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxShots);
        ArgumentOutOfRangeException.ThrowIfLessThan(minPlayersPerSide, 1);
        _maxShots = maxShots;
        _minPlayersPerSide = minPlayersPerSide;
    }

    public RoundContext? Context { get; private set; }

    public bool IsOpen => Context is not null;

    public int ShotsDropped { get; private set; }

    public int EventsDropped { get; private set; }

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
        var record = new RoundRecord(
            context, outcome, ranked,
            _shots.ToList(), _damages.ToList(), _kills.ToList(), _thrown.ToList(), _detonated.ToList(), _blinds.ToList(),
            _plant, _defuse, _explosion, _mvps.ToList(), _leavers.ToList())
        {
            Events = _events.ToList(),
            EndStates = _endStates.Values.ToList(),
            FreezeEndMs = _freezeEndMs,
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
        _plant = null;
        _defuse = null;
        _explosion = null;
        _freezeEndMs = null;
    }
}
