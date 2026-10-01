using System.Collections.Immutable;
using RetakeV4.Domain.Common;
using RetakeV4.Domain.Loadouts;

namespace RetakeV4.Domain.Preferences;

public sealed record PreferenceKey(ulong SteamId, TeamSide Team, string RoundType)
{
    public const string AnyRoundType = "*";
}

public sealed record StoredPreference(PreferenceKey Key, LoadoutPreference Preference);

public sealed record PreferenceBook(
    ImmutableDictionary<PreferenceKey, LoadoutPreference> Entries,
    ImmutableDictionary<ulong, long> Sessions,
    long NextToken)
{
    private static readonly TeamSide[] BothSides = { TeamSide.T, TeamSide.CT };
    private static readonly LoadoutPreference Nothing = new(null, null, false);

    public static PreferenceBook Empty { get; } =
        new(ImmutableDictionary<PreferenceKey, LoadoutPreference>.Empty, ImmutableDictionary<ulong, long>.Empty, 1);

    public (PreferenceBook Book, long Token) StartSession(ulong steamId) =>
        (this with { Sessions = Sessions.SetItem(steamId, NextToken), NextToken = NextToken + 1 }, NextToken);

    // Keys already present were changed while loading: they are newer than the database and win.
    public PreferenceBook WithPlayer(ulong steamId, long sessionToken, IEnumerable<StoredPreference> stored)
    {
        if (Sessions.GetValueOrDefault(steamId) != sessionToken)
        {
            return this;
        }
        var fresh = stored.Where(s => s.Key.SteamId == steamId && !Entries.ContainsKey(s.Key));
        return this with { Entries = Entries.SetItems(fresh.Select(s => KeyValuePair.Create(s.Key, s.Preference))) };
    }

    public PreferenceBook WithoutPlayer(ulong steamId) => this with
    {
        Entries = Entries.RemoveRange(Entries.Keys.Where(k => k.SteamId == steamId)),
        Sessions = Sessions.Remove(steamId),
    };

    // Like WithPlayer without a session token: entries already in memory (chosen in game) win.
    public PreferenceBook Merge(ulong steamId, IEnumerable<StoredPreference> stored)
    {
        if (!Sessions.ContainsKey(steamId))
        {
            return this;
        }
        var fresh = stored.Where(s => s.Key.SteamId == steamId && !Entries.ContainsKey(s.Key));
        return this with { Entries = Entries.SetItems(fresh.Select(s => KeyValuePair.Create(s.Key, s.Preference))) };
    }

    // The database is the reference (changed outside the game): every entry of the player is replaced.
    public PreferenceBook Replace(ulong steamId, IEnumerable<StoredPreference> stored)
    {
        if (!Sessions.ContainsKey(steamId))
        {
            return this;
        }
        var kept = Entries.RemoveRange(Entries.Keys.Where(k => k.SteamId == steamId));
        var fresh = stored.Where(s => s.Key.SteamId == steamId).Select(s => KeyValuePair.Create(s.Key, s.Preference));
        return this with { Entries = kept.SetItems(fresh) };
    }

    public PreferenceBook With(StoredPreference preference) =>
        this with { Entries = Entries.SetItem(preference.Key, preference.Preference) };

    public (PreferenceBook Book, StoredPreference Change) SetWeapon(ulong steamId, TeamSide team, string roundType, WeaponSlot slot, string weapon)
    {
        var key = new PreferenceKey(steamId, team, roundType);
        var current = Entries.GetValueOrDefault(key) ?? Nothing;
        var change = new StoredPreference(key, slot == WeaponSlot.Primary ? current with { Primary = weapon } : current with { Secondary = weapon });
        return (With(change), change);
    }

    public LoadoutPreference? RequestFor(ulong steamId, TeamSide team, string roundType)
    {
        var weapons = Entries.GetValueOrDefault(new PreferenceKey(steamId, team, roundType));
        var awp = Entries.GetValueOrDefault(new PreferenceKey(steamId, team, PreferenceKey.AnyRoundType));
        if (weapons is null && awp is null)
        {
            return null;
        }
        return (weapons ?? Nothing) with { AwpOptIn = awp?.AwpOptIn ?? false };
    }

    public bool IsAwpVolunteer(ulong steamId) =>
        BothSides.Any(side => Entries.GetValueOrDefault(new PreferenceKey(steamId, side, PreferenceKey.AnyRoundType))?.AwpOptIn == true);

    public bool IsAwpVolunteer(ulong steamId, TeamSide side) =>
        Entries.GetValueOrDefault(new PreferenceKey(steamId, side, PreferenceKey.AnyRoundType))?.AwpOptIn == true;

    public (PreferenceBook Book, IReadOnlyList<StoredPreference> Changes, bool OptIn) ToggleAwp(ulong steamId, TeamSide side)
    {
        var key = new PreferenceKey(steamId, side, PreferenceKey.AnyRoundType);
        var optIn = !IsAwpVolunteer(steamId, side);
        var change = new StoredPreference(key, (Entries.GetValueOrDefault(key) ?? Nothing) with { AwpOptIn = optIn });
        return (With(change), new[] { change }, optIn);
    }

    // Both sides at once: the !awp command used by a player without a team.
    public (PreferenceBook Book, IReadOnlyList<StoredPreference> Changes, bool OptIn) ToggleAwp(ulong steamId)
    {
        var optIn = !IsAwpVolunteer(steamId);
        var changes = BothSides
            .Select(side => new PreferenceKey(steamId, side, PreferenceKey.AnyRoundType))
            .Select(key => new StoredPreference(key, (Entries.GetValueOrDefault(key) ?? Nothing) with { AwpOptIn = optIn }))
            .ToList();
        return (changes.Aggregate(this, (book, change) => book.With(change)), changes, optIn);
    }
}
