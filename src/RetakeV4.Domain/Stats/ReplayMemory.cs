using System.Globalization;

namespace RetakeV4.Domain.Stats;

// A recent round kept for !save: its replay and each owner's Retake Rating (the score of a saved replay). Only players
// with a track in the replay can own (save) the round; build it with Create so Ratings holds exactly those players.
public sealed record RememberedRound(long RoundKey, int RoundNumber, string MapName, ReplayRound Replay, IReadOnlyDictionary<ulong, double> Ratings)
{
    public static RememberedRound Create(long roundKey, int roundNumber, string mapName, ReplayRound replay, IReadOnlyDictionary<ulong, double> ratings)
    {
        ArgumentNullException.ThrowIfNull(replay);
        ArgumentNullException.ThrowIfNull(ratings);
        var owners = replay.Tracks.Select(t => t.SteamId).ToHashSet();
        var owned = ratings.Where(r => owners.Contains(r.Key)).ToDictionary(r => r.Key, r => r.Value);
        return new RememberedRound(roundKey, roundNumber, mapName, replay, owned);
    }
}

public enum SaveLookupStatus
{
    NotFound,
    Found,
    AlreadySaved,
}

public sealed record SaveLookup(SaveLookupStatus Status, RememberedRound? Round)
{
    public static SaveLookup NotFound { get; } = new(SaveLookupStatus.NotFound, null);
}

// The replays of the last Capacity recorded rounds, newest first, for !save [n] (stats v2 spec, section 3.1). Game thread
// only. Memory bound: Capacity rounds of frame text, about 250 KB each for 10 players over 45 s at 8 samples/s (worst
// case RoundRecorder.MaxSamplesPerRound samples, about 3.5 MB). A player saves a round once. TryFind only looks: the
// caller calls MarkSaved once the write queue has accepted the command, so a refused enqueue leaves the round unsaved.
public sealed class ReplayMemory
{
    private readonly List<(RememberedRound Round, HashSet<ulong> Saved)> _rounds = new();

    public ReplayMemory(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        Capacity = capacity;
    }

    public int Capacity { get; }

    public int Count => _rounds.Count;

    public void Add(RememberedRound round)
    {
        ArgumentNullException.ThrowIfNull(round);
        _rounds.Insert(0, (round, new HashSet<ulong>()));
        if (_rounds.Count > Capacity)
        {
            _rounds.RemoveAt(_rounds.Count - 1);
        }
    }

    public void Clear() => _rounds.Clear();

    // roundsAgo 1 = the newest round. NotFound: no such round in memory, or the player owns no track in it. Found or
    // AlreadySaved carry the round; nothing is changed.
    public SaveLookup TryFind(ulong steamId, int roundsAgo)
    {
        if (roundsAgo < 1 || roundsAgo > _rounds.Count)
        {
            return SaveLookup.NotFound;
        }
        var (round, saved) = _rounds[roundsAgo - 1];
        if (!round.Ratings.ContainsKey(steamId))
        {
            return SaveLookup.NotFound;
        }
        return new SaveLookup(saved.Contains(steamId) ? SaveLookupStatus.AlreadySaved : SaveLookupStatus.Found, round);
    }

    // Records that the player's save of that round was accepted. False if the round left memory or the player cannot own it.
    public bool MarkSaved(ulong steamId, long roundKey)
    {
        foreach (var (round, saved) in _rounds)
        {
            if (round.RoundKey == roundKey)
            {
                return round.Ratings.ContainsKey(steamId) && saved.Add(steamId);
            }
        }
        return false;
    }

    // The n of "!save n": none (or blanks) = 1; otherwise digits only, from 1 to capacity. Null for anything else.
    public static int? ParseRoundsAgo(string? argument, int capacity)
    {
        if (string.IsNullOrWhiteSpace(argument))
        {
            return 1;
        }
        return int.TryParse(argument.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var roundsAgo) && roundsAgo >= 1 && roundsAgo <= capacity
            ? roundsAgo
            : null;
    }
}
