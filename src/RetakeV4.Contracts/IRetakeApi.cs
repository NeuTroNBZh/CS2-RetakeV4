using CounterStrikeSharp.API.Core.Capabilities;

namespace RetakeV4.Contracts;

public enum RetakeState
{
    Warmup,
    Preparing,
    FreezeTime,
    Live,
    PostRound,
}

public enum BombSite
{
    A,
    B,
}

public enum ForceSiteMode
{
    Once,
    Sticky,
}

public enum RetakeTeam
{
    T,
    CT,
}

public sealed record RetakePlayer(int Slot, ulong SteamId);

public sealed record RoundPreparedEvent(int RoundNumber, string? RoundType, BombSite? Site, RetakePlayer? Planter);

public sealed record BombPlantedEvent(BombSite? Site, RetakePlayer? Planter);

public sealed record PlayerLoadout(RetakePlayer Player, string? Primary, string Secondary, bool DefuseKit, bool Zeus, IReadOnlyList<string> Grenades);

public sealed record LoadoutAssignedEvent(int RoundNumber, IReadOnlyList<PlayerLoadout> Loadouts);

public sealed record LastPlayerAliveEvent(RetakeTeam Team, RetakePlayer Player);

public sealed record RoundEndedEvent(int RoundNumber, RetakeTeam? Winner);

public sealed record PlayerQueuedEvent(RetakePlayer Player, int Position);

// Events are raised on the game thread. An exception thrown by a subscriber is logged by RetakeV4 and never stops the retake.
public interface IRetakeApi
{
    int ApiVersion { get; }

    RetakeState State { get; }

    BombSite? CurrentSite { get; }

    string? CurrentRoundType { get; }

    int? GetQueuePosition(ulong steamId);

    void ForceSite(BombSite site, ForceSiteMode mode);

    void RequestScramble();

    event Action<RoundPreparedEvent>? RoundPrepared;

    event Action<BombPlantedEvent>? BombPlanted;

    event Action<LoadoutAssignedEvent>? LoadoutAssigned;

    event Action<LastPlayerAliveEvent>? LastPlayerAlive;

    event Action<RoundEndedEvent>? RoundEnded;

    event Action<PlayerQueuedEvent>? PlayerQueued;
}

public static class RetakeApi
{
    public const int Version = 1;

    // Get() returns null while RetakeV4 (or its Api module) is not loaded.
    public static PluginCapability<IRetakeApi?> Capability { get; } = new("retakev4:api");
}
