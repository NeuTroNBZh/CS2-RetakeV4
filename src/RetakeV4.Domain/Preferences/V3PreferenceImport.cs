using RetakeV4.Domain.Common;
using RetakeV4.Domain.Loadouts;

namespace RetakeV4.Domain.Preferences;

public sealed record V3PreferenceRow(string Table, ulong UserId, int Team, string? WeaponString, int? AwpChance);

public static class V3PreferenceImport
{
    private enum Field
    {
        Primary,
        Secondary,
        Awp,
    }

    private static readonly IReadOnlyDictionary<string, (string RoundType, Field Field)> Targets =
        new Dictionary<string, (string, Field)>(StringComparer.Ordinal)
        {
            ["FullBuyPrimary"] = ("FullBuy", Field.Primary),
            ["FullBuySecondary"] = ("FullBuy", Field.Secondary),
            ["MidPrimary"] = ("Mid", Field.Primary),
            ["MidSecondary"] = ("Mid", Field.Secondary),
            ["Pistol"] = ("Pistol", Field.Secondary),
            ["FullBuyAWPChance"] = (PreferenceKey.AnyRoundType, Field.Awp),
        };

    public static IReadOnlyList<StoredPreference> Convert(IEnumerable<V3PreferenceRow> rows)
    {
        var merged = new Dictionary<PreferenceKey, LoadoutPreference>();
        foreach (var row in rows)
        {
            TeamSide? team = row.Team switch { 2 => TeamSide.T, 3 => TeamSide.CT, _ => null };
            if (team is null || !Targets.TryGetValue(row.Table, out var target))
            {
                continue;
            }
            var key = new PreferenceKey(row.UserId, team.Value, target.RoundType);
            var current = merged.GetValueOrDefault(key) ?? new LoadoutPreference(null, null, false);
            merged[key] = Apply(current, target.Field, row);
        }
        return merged.Select(entry => new StoredPreference(entry.Key, entry.Value)).ToList();
    }

    private static LoadoutPreference Apply(LoadoutPreference current, Field field, V3PreferenceRow row) => field switch
    {
        Field.Primary => current with { Primary = Weapon(row.WeaponString) },
        Field.Secondary => current with { Secondary = Weapon(row.WeaponString) },
        _ => current with { AwpOptIn = row.AwpChance > 0 },
    };

    private static string? Weapon(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
