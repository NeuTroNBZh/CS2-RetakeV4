using RetakeV4.Configuration;

namespace RetakeV4.Modules.MapVote;

public sealed record MapVoteConfig : ModuleConfig
{
    public MapVoteConfig() => Version = 1;

    public int TriggerRoundsBeforeEnd { get; init; } = 3;

    public int VoteSeconds { get; init; } = 30;

    public int ChangeDelaySeconds { get; init; } = 8;

    public bool RtvEnabled { get; init; } = true;

    public int RtvPercentage { get; init; } = 60;

    public int RtvMinPlayers { get; init; } = 2;

    public int RtvMinRounds { get; init; } = 3;

    public IReadOnlyList<string> ExcludedMaps { get; init; } = Array.Empty<string>();
}
