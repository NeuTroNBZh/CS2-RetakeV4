using System.Collections.Immutable;

namespace RetakeV4.Domain.Preferences;

// Detects preferences changed outside the game (web panel) without trusting any clock: a player's stamp is compared for
// equality only. A player with in-game writes not yet saved, or who edited since a reload was requested, keeps his choice.
public sealed record PreferenceSync(
    ImmutableDictionary<ulong, string> Stamps,
    ImmutableDictionary<ulong, long> Edits,
    ImmutableDictionary<ulong, int> Pending)
{
    public const string NoRows = "";

    public static PreferenceSync Empty { get; } = new(
        ImmutableDictionary<ulong, string>.Empty, ImmutableDictionary<ulong, long>.Empty, ImmutableDictionary<ulong, int>.Empty);

    public long EditOf(ulong steamId) => Edits.GetValueOrDefault(steamId);

    public PreferenceSync Edited(ulong steamId) => this with
    {
        Edits = Edits.SetItem(steamId, EditOf(steamId) + 1),
        Pending = Pending.SetItem(steamId, Pending.GetValueOrDefault(steamId) + 1),
    };

    public PreferenceSync Flushed(ulong steamId)
    {
        var left = Pending.GetValueOrDefault(steamId) - 1;
        return this with { Pending = left > 0 ? Pending.SetItem(steamId, left) : Pending.Remove(steamId) };
    }

    public PreferenceSync Stamped(ulong steamId, string stamp) => this with { Stamps = Stamps.SetItem(steamId, stamp) };

    public PreferenceSync Forget(ulong steamId) => this with
    {
        Stamps = Stamps.Remove(steamId),
        Edits = Edits.Remove(steamId),
        Pending = Pending.Remove(steamId),
    };

    public IReadOnlyList<ulong> Changed(IReadOnlyCollection<ulong> connected, IReadOnlyDictionary<ulong, string> current) =>
        connected
            .Where(id => !Pending.ContainsKey(id))
            .Where(id => !Stamps.TryGetValue(id, out var known) || known != current.GetValueOrDefault(id, NoRows))
            .ToList();

    public bool CanApply(ulong steamId, long editAtRequest) => !Pending.ContainsKey(steamId) && EditOf(steamId) == editAtRequest;
}
