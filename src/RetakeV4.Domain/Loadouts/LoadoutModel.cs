using RetakeV4.Domain.Common;

namespace RetakeV4.Domain.Loadouts;

public enum ArmorKind
{
    None,
    Kevlar,
    KevlarHelmet,
}

public enum DefuseKitMode
{
    All,
    Quota,
    Chance,
}

public sealed record TeamWeapons(IReadOnlyList<string> T, IReadOnlyList<string> CT, IReadOnlyList<string> Any)
{
    public static TeamWeapons Empty { get; } = new(Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());

    public IReadOnlyList<string> For(TeamSide side) =>
        (side == TeamSide.T ? T : CT).Concat(Any).Distinct(StringComparer.Ordinal).ToList();
}

public sealed record TeamDefault(string? Primary, string Secondary);

public sealed record AwpSettings(bool Enabled, int MaxPerTeam, int MinActivePlayers, double Chance);

public sealed record DefuseKitSettings(DefuseKitMode Mode, int Quota, double Chance, bool GuaranteeMinimum);

public sealed record ZeusSettings(bool Enabled, double Chance);

public sealed record RoundTypeDefinition(
    string Name,
    ArmorKind Armor,
    TeamWeapons Primaries,
    TeamWeapons Secondaries,
    TeamDefault DefaultT,
    TeamDefault DefaultCT,
    AwpSettings Awp,
    DefuseKitSettings DefuseKit,
    ZeusSettings Zeus,
    string GrenadePool)
{
    public TeamDefault DefaultFor(TeamSide side) => side == TeamSide.T ? DefaultT : DefaultCT;
}

public sealed record GrenadeKit(TeamSide? Team, IReadOnlyList<string> Grenades);

public sealed record LoadoutPreference(string? Primary, string? Secondary, bool AwpOptIn);

public sealed record LoadoutRequest(PlayerId Player, TeamSide Team, LoadoutPreference? Preference);

public sealed record Loadout(string? Primary, string Secondary, ArmorKind Armor, bool DefuseKit, bool Zeus, IReadOnlyList<string> Grenades);
