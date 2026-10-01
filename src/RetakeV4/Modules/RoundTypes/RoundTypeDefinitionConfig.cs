using RetakeV4.Domain.Loadouts;

namespace RetakeV4.Modules.RoundTypes;

public sealed record WeaponPoolConfig
{
    public IReadOnlyList<string> T { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> CT { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Any { get; init; } = Array.Empty<string>();

    public TeamWeapons ToDomain() => new(T, CT, Any);
}

public sealed record DefaultWeaponsConfig
{
    public string? Primary { get; init; }

    public string Secondary { get; init; } = "weapon_deagle";

    public TeamDefault ToDomain() => new(Primary, Secondary);
}

public sealed record TeamDefaultsConfig
{
    public DefaultWeaponsConfig T { get; init; } = new();

    public DefaultWeaponsConfig CT { get; init; } = new();
}

public sealed record AwpConfig
{
    public bool Enabled { get; init; }

    public int MaxPerTeam { get; init; } = 1;

    public int MinActivePlayers { get; init; } = 5;

    public double Chance { get; init; } = 30;

    public AwpSettings ToDomain() => new(Enabled, MaxPerTeam, MinActivePlayers, Chance);
}

public sealed record DefuseKitConfig
{
    public DefuseKitMode Mode { get; init; } = DefuseKitMode.All;

    public int Quota { get; init; } = 1;

    public double Chance { get; init; } = 100;

    public bool GuaranteeMinimum { get; init; }

    public DefuseKitSettings ToDomain() => new(Mode, Quota, Chance, GuaranteeMinimum);
}

public sealed record ZeusConfig
{
    public bool Enabled { get; init; } = true;

    public double Chance { get; init; } = 100;

    public ZeusSettings ToDomain() => new(Enabled, Chance);
}

public sealed record RoundTypeDefinitionConfig
{
    public string Name { get; init; } = string.Empty;

    public ArmorKind Armor { get; init; } = ArmorKind.KevlarHelmet;

    public WeaponPoolConfig Primaries { get; init; } = new();

    public WeaponPoolConfig Secondaries { get; init; } = new();

    public TeamDefaultsConfig Defaults { get; init; } = new();

    public AwpConfig Awp { get; init; } = new();

    public DefuseKitConfig DefuseKit { get; init; } = new();

    public ZeusConfig Zeus { get; init; } = new();

    public string GrenadePool { get; init; } = "Default";

    public RoundTypeDefinition ToDomain() => new(
        Name, Armor, Primaries.ToDomain(), Secondaries.ToDomain(),
        Defaults.T.ToDomain(), Defaults.CT.ToDomain(),
        Awp.ToDomain(), DefuseKit.ToDomain(), Zeus.ToDomain(), GrenadePool);
}
