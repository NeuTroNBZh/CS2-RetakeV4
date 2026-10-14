namespace RetakeV4.Domain.Stats;

// The carrier holds the bomb from a bomb_pickup until their bomb_drop, the plant, their death, another pickup or the
// round end, whichever comes first.
internal static class BombCarry
{
    public static Dictionary<ulong, int> Compute(RoundRecord record, IReadOnlyDictionary<ulong, int> deathTimes)
    {
        var result = new Dictionary<ulong, int>();
        ulong? carrier = null;
        var since = 0;
        foreach (var playerEvent in record.Events
            .Where(e => e.Type is PlayerEventType.BombPickup or PlayerEventType.BombDrop)
            .OrderBy(e => e.TimeMs))
        {
            if (playerEvent.Type == PlayerEventType.BombPickup)
            {
                Stop(playerEvent.TimeMs);
                carrier = playerEvent.Player;
                since = playerEvent.TimeMs;
            }
            else if (carrier == playerEvent.Player)
            {
                Stop(playerEvent.TimeMs);
            }
        }
        Stop(record.Outcome.DurationMs);
        return result;

        void Stop(int at)
        {
            if (carrier is not { } holder)
            {
                return;
            }
            var end = at;
            if (record.Plant is { } plant && plant.Planter == holder && plant.TimeMs >= since)
            {
                end = Math.Min(end, plant.TimeMs);
            }
            if (deathTimes.TryGetValue(holder, out var died) && died >= since)
            {
                end = Math.Min(end, died);
            }
            result[holder] = result.GetValueOrDefault(holder) + Math.Max(0, end - since);
            carrier = null;
        }
    }
}
