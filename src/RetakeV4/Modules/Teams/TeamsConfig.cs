using RetakeV4.Configuration;
using RetakeV4.Domain.Teams;

namespace RetakeV4.Modules.Teams;

public sealed record PriorityFlagConfig
{
    public string Flag { get; init; } = string.Empty;

    public int Priority { get; init; }
}

public sealed record TeamsConfig : ModuleConfig
{
    public TeamsConfig() => Version = 1;

    public int MaxPlayers { get; init; } = 9;

    public double TeamBalanceRatio { get; init; } = 0.499;

    public int ScrambleAfterTWins { get; init; } = 5;

    public bool SwitchTeamsOnCtWin { get; init; } = true;

    public bool RestartOnInconsistency { get; init; } = true;

    public IReadOnlyList<PriorityFlagConfig> PriorityFlags { get; init; } = new[]
    {
        new PriorityFlagConfig { Flag = "@css/vip", Priority = 1 },
        new PriorityFlagConfig { Flag = "@css/root", Priority = 2 },
    };

    public TeamRules ToRules() => new(MaxPlayers, TeamBalanceRatio, ScrambleAfterTWins, SwitchTeamsOnCtWin);
}
