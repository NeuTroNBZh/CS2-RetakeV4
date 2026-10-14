using RetakeV4.Domain.Common;
using RetakeV4.Domain.Stats;

namespace RetakeV4.Domain.Tests.Stats;

public class EloCacheTests
{
    private const ulong A = 7;

    private static EloCache Cache() => new(start: 1000, floor: 100);

    private static PlayerRoundStats Played(ulong id, int before, int delta) =>
        new(id, "p", TeamSide.CT) { EloBefore = before, EloDelta = delta, EloAfter = before + delta };

    private static IReadOnlyDictionary<ulong, int> Stored(ulong id, int elo) => new Dictionary<ulong, int> { [id] = elo };

    private static bool Connected(ulong _) => true;

    [Fact]
    public void UnknownPlayer_HasTheStartElo() => Assert.Equal(1000, Cache().Current(A));

    [Fact]
    public void Load_SetsTheStoredValue_AndMarksLoaded()
    {
        var cache = Cache();
        var token = cache.BeginLoad(A)!.Value;
        Assert.True(cache.CompleteLoad(A, token, Stored(A, 1234)));
        Assert.Equal(1234, cache.Current(A));
        Assert.True(cache.IsLoaded(A));
        Assert.Null(cache.BeginLoad(A));
    }

    [Fact]
    public void Load_IsNotStartedTwice()
    {
        var cache = Cache();
        Assert.NotNull(cache.BeginLoad(A));
        Assert.Null(cache.BeginLoad(A));
    }

    [Fact]
    public void RoundsClosedDuringALoad_AreAddedToTheStoredValue()
    {
        var cache = Cache();
        var token = cache.BeginLoad(A)!.Value;
        cache.ApplyRound(new[] { Played(A, 1000, 12) }, Connected);
        Assert.Equal(1012, cache.Current(A));
        cache.CompleteLoad(A, token, Stored(A, 1500));
        Assert.Equal(1512, cache.Current(A));
    }

    [Fact]
    public void LoadedValue_RespectsTheFloor()
    {
        var cache = Cache();
        var token = cache.BeginLoad(A)!.Value;
        cache.ApplyRound(new[] { Played(A, 1000, -50) }, Connected);
        cache.CompleteLoad(A, token, Stored(A, 120));
        Assert.Equal(100, cache.Current(A));
    }

    [Fact]
    public void NewPlayer_WithoutAStoredValue_KeepsTheRoundsPlayed()
    {
        var cache = Cache();
        var token = cache.BeginLoad(A)!.Value;
        cache.ApplyRound(new[] { Played(A, 1000, 8) }, Connected);
        cache.CompleteLoad(A, token, new Dictionary<ulong, int>());
        Assert.Equal(1008, cache.Current(A));
        Assert.True(cache.IsLoaded(A));
    }

    [Fact]
    public void RoundAfterTheLoad_IsNotAddedTwice()
    {
        var cache = Cache();
        cache.CompleteLoad(A, cache.BeginLoad(A)!.Value, Stored(A, 1500));
        cache.ApplyRound(new[] { Played(A, 1500, 10) }, Connected);
        Assert.Equal(1510, cache.Current(A));
    }

    // A player leaves mid-round: the round closes after the disconnect, and the stored value already includes its delta
    // when the player comes back.
    [Fact]
    public void Leaver_IsNotUpdatedByTheRoundClosedAfterTheirDisconnect()
    {
        var cache = Cache();
        cache.CompleteLoad(A, cache.BeginLoad(A)!.Value, Stored(A, 1500));
        cache.Forget(A);
        cache.ApplyRound(new[] { Played(A, 1500, -15) }, _ => false);
        Assert.Equal(1000, cache.Current(A));
        var token = cache.BeginLoad(A)!.Value;
        cache.CompleteLoad(A, token, Stored(A, 1485));
        Assert.Equal(1485, cache.Current(A));
    }

    [Fact]
    public void LoadStartedBeforeAReconnect_IsIgnored()
    {
        var cache = Cache();
        var stale = cache.BeginLoad(A)!.Value;
        cache.Forget(A);
        var current = cache.BeginLoad(A)!.Value;
        Assert.False(cache.CompleteLoad(A, stale, Stored(A, 1400)));
        Assert.False(cache.IsLoaded(A));
        Assert.Equal(1000, cache.Current(A));
        Assert.True(cache.CompleteLoad(A, current, Stored(A, 1450)));
        Assert.Equal(1450, cache.Current(A));
    }

    [Fact]
    public void StaleFailure_DoesNotCancelTheCurrentLoad()
    {
        var cache = Cache();
        var stale = cache.BeginLoad(A)!.Value;
        cache.Forget(A);
        var current = cache.BeginLoad(A)!.Value;
        cache.CancelLoad(A, stale);
        Assert.Null(cache.BeginLoad(A));
        cache.CancelLoad(A, current);
        Assert.NotNull(cache.BeginLoad(A));
    }

    [Fact]
    public void Clear_ForgetsEverything()
    {
        var cache = Cache();
        var token = cache.BeginLoad(A)!.Value;
        cache.ApplyRound(new[] { Played(A, 1000, 5) }, Connected);
        cache.Clear();
        Assert.False(cache.CompleteLoad(A, token, Stored(A, 1300)));
        Assert.Equal(1000, cache.Current(A));
        Assert.NotNull(cache.BeginLoad(A));
    }
}
