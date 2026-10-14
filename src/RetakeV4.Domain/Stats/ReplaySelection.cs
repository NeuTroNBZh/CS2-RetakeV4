namespace RetakeV4.Domain.Stats;

public enum ReplayReason
{
    Best,
    Highlight,
    Saved,
}

public static class ReplayReasons
{
    // The stats_replay_owners.reason values.
    public static string Name(ReplayReason reason) => reason switch
    {
        ReplayReason.Best => "best",
        ReplayReason.Highlight => "highlight",
        ReplayReason.Saved => "saved",
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "Unknown replay reason"),
    };
}

// Per-player caps (stats.json ReplayBestPerPlayer, ReplayHighlightsPerPlayer, ReplaySavedPerPlayer); 0 turns a source off.
public sealed record ReplayCaps(int Best, int Highlights, int Saved)
{
    public bool Any => Best > 0 || Highlights > 0 || Saved > 0;

    public int For(ReplayReason reason) => reason switch
    {
        ReplayReason.Best => Best,
        ReplayReason.Highlight => Highlights,
        ReplayReason.Saved => Saved,
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "Unknown replay reason"),
    };
}

// A player who could keep a round as a replay. Score = the player's Retake Rating in that round.
public sealed record ReplayClaim(ulong SteamId, ReplayReason Reason, double Score);

// A replay a player already owns for one reason (a stats_replay_owners row).
public sealed record ReplayOwnership(long RoundId, double Score, DateTimeOffset CreatedAt);

// Keep: insert the claim, after deleting the Evict ownerships of the same player and reason.
public sealed record ReplayDecision(bool Keep, IReadOnlyList<long> Evict)
{
    public static ReplayDecision Skip { get; } = new(false, Array.Empty<long>());

    public bool Equals(ReplayDecision? other) => other is not null && Keep == other.Keep && Evict.SequenceEqual(other.Evict);

    public override int GetHashCode() => HashCode.Combine(Keep, Evict.Count);
}

// Which rounds a player keeps (stats v2 spec, section 3.1). The repository reads the player's current ownerships and
// applies Decide inside the write transaction, so that servers sharing the database agree.
public static class ReplaySelection
{
    // Aces and won clutches count as highlights from this many enemies.
    public const int HighlightMinOpponents = 3;

    // best keeps the strongest scores (the claim ranks with the held ones and never wins a tie, so it must beat the weakest kept one strictly; ties evict the oldest, then the lowest
    // round id); highlight and saved keep the newest. Below the cap nothing is evicted; a lowered cap trims its surplus at
    // the next insertion. A round the player already owns for this reason is skipped.
    public static ReplayDecision Decide(long roundId, ReplayClaim claim, IReadOnlyList<ReplayOwnership> held, int cap)
    {
        ArgumentNullException.ThrowIfNull(claim);
        ArgumentNullException.ThrowIfNull(held);
        if (cap <= 0 || held.Any(h => h.RoundId == roundId))
        {
            return ReplayDecision.Skip;
        }
        if (held.Count < cap)
        {
            return new ReplayDecision(true, Array.Empty<long>());
        }
        var surplus = held.Count - cap + 1;
        if (claim.Reason == ReplayReason.Best)
        {
            var weakest = held.OrderBy(h => h.Score).ThenBy(h => h.CreatedAt).ThenBy(h => h.RoundId).ToList();
            // The claim ranks with the held replays and loses every tie: it must land in the top cap, else nothing is trimmed.
            var stronger = held.Count(h => h.Score >= claim.Score);
            return stronger < cap
                ? new ReplayDecision(true, weakest.Take(surplus).Select(h => h.RoundId).ToList())
                : ReplayDecision.Skip;
        }
        return new ReplayDecision(true, held.OrderBy(h => h.CreatedAt).ThenBy(h => h.RoundId).Take(surplus).Select(h => h.RoundId).ToList());
    }

    // The claims of a scored round, in player order: best for every tracked player of a ranked round, highlight for an
    // ace or a won clutch. A round is a candidate as soon as one claim exists; the database decides. Saved claims come
    // from !save, not from here.
    public static IReadOnlyList<ReplayClaim> Claims(RoundSummary summary, ReplayRound replay, ReplayCaps caps)
    {
        ArgumentNullException.ThrowIfNull(summary);
        ArgumentNullException.ThrowIfNull(replay);
        ArgumentNullException.ThrowIfNull(caps);
        var tracked = replay.Tracks.Select(t => t.SteamId).ToHashSet();
        var claims = new List<ReplayClaim>();
        foreach (var player in summary.Players)
        {
            if (!tracked.Contains(player.SteamId))
            {
                continue;
            }
            if (caps.Best > 0 && summary.Record.Ranked)
            {
                claims.Add(new ReplayClaim(player.SteamId, ReplayReason.Best, player.Rating));
            }
            if (caps.Highlights > 0 && IsHighlight(summary, player))
            {
                claims.Add(new ReplayClaim(player.SteamId, ReplayReason.Highlight, player.Rating));
            }
        }
        return claims;
    }

    // An ace: the player killed every enemy participant, at least HighlightMinOpponents of them. A won clutch: the player
    // was last of their side against at least HighlightMinOpponents enemies (clutch_vs) and the side won.
    public static bool IsHighlight(RoundSummary summary, PlayerRoundStats player)
    {
        ArgumentNullException.ThrowIfNull(summary);
        ArgumentNullException.ThrowIfNull(player);
        var enemies = summary.Record.Context.Participants.Count(p => p.Side != player.Side);
        var ace = enemies >= HighlightMinOpponents && player.Kills >= enemies;
        var clutch = player.ClutchWon && player.ClutchVs >= HighlightMinOpponents;
        return ace || clutch;
    }
}
