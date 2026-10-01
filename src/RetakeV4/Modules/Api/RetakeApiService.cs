using Microsoft.Extensions.Logging;
using RetakeV4.Contracts;
using RetakeV4.Domain.Common;
using RetakeV4.Domain.Events;
using RetakeV4.Domain.Rounds;
using RetakeV4.Domain.Spawns;
using RetakeV4.Domain.Teams;
using ContractForceMode = RetakeV4.Contracts.ForceSiteMode;
using ContractSite = RetakeV4.Contracts.BombSite;
using DomainBombPlanted = RetakeV4.Domain.Events.BombPlanted;
using DomainRoundPrepared = RetakeV4.Domain.Events.RoundPrepared;
using DomainSite = RetakeV4.Domain.Common.BombSite;
using DomainForceMode = RetakeV4.Domain.Spawns.ForceSiteMode;

namespace RetakeV4.Modules.Api;

// Translates bus events into the public contract. Game thread only.
internal sealed class RetakeApiService : IRetakeApi
{
    private readonly IEventBus _bus;
    private readonly ILogger _logger;
    private readonly Func<ulong, int?> _slotOf;
    private readonly Func<int, ulong> _steamIdOf;
    private TeamState _teams = TeamState.Empty;

    public RetakeApiService(IEventBus bus, ILogger logger, Func<ulong, int?> slotOf, Func<int, ulong> steamIdOf)
    {
        _bus = bus;
        _logger = logger;
        _slotOf = slotOf;
        _steamIdOf = steamIdOf;
    }

    public event Action<RoundPreparedEvent>? RoundPrepared;

    public event Action<BombPlantedEvent>? BombPlanted;

    public event Action<LoadoutAssignedEvent>? LoadoutAssigned;

    public event Action<LastPlayerAliveEvent>? LastPlayerAlive;

    public event Action<RoundEndedEvent>? RoundEnded;

    public event Action<PlayerQueuedEvent>? PlayerQueued;

    public int ApiVersion => RetakeApi.Version;

    public RetakeState State { get; private set; } = RetakeState.Warmup;

    public ContractSite? CurrentSite { get; private set; }

    public string? CurrentRoundType { get; private set; }

    public int? GetQueuePosition(ulong steamId) => _slotOf(steamId) is { } slot ? _teams.QueuePosition(new PlayerId(slot)) : null;

    public void ForceSite(ContractSite site, ContractForceMode mode)
    {
        var force = new SiteForce(Map(site), mode == ContractForceMode.Sticky ? DomainForceMode.Sticky : DomainForceMode.Once);
        _bus.Publish(new ForceSiteRequested(null, new ForceSiteRequest(force)));
    }

    public void RequestScramble() => _bus.Publish(new ScrambleRequested(null));

    public void OnPhase(RoundPhaseChanged e) => State = e.To switch
    {
        RoundPhase.Warmup => RetakeState.Warmup,
        RoundPhase.Preparing => RetakeState.Preparing,
        RoundPhase.FreezeTime => RetakeState.FreezeTime,
        RoundPhase.Live => RetakeState.Live,
        _ => RetakeState.PostRound,
    };

    public void OnRoundPrepared(DomainRoundPrepared e)
    {
        CurrentSite = Map(e.Context.Site);
        CurrentRoundType = e.Context.RoundType;
        Raise(RoundPrepared, new RoundPreparedEvent(e.Context.RoundNumber, e.Context.RoundType, CurrentSite, PlayerOf(e.Context.Planter)));
    }

    public void OnBombPlanted(DomainBombPlanted e) => Raise(BombPlanted, new BombPlantedEvent(Map(e.Site), PlayerOf(e.Planter)));

    public void OnLoadouts(LoadoutsAssigned e)
    {
        var loadouts = e.Loadouts
            .OrderBy(entry => entry.Key.Slot)
            .Select(entry => new PlayerLoadout(
                Player(entry.Key.Slot), entry.Value.Primary, entry.Value.Secondary, entry.Value.DefuseKit, entry.Value.Zeus, entry.Value.Grenades))
            .ToList();
        Raise(LoadoutAssigned, new LoadoutAssignedEvent(e.RoundNumber, loadouts));
    }

    public void OnTeams(TeamStateChanged e)
    {
        var added = QueueChanges.NewlyQueued(_teams, e.State);
        _teams = e.State;
        foreach (var (player, position) in added)
        {
            Raise(PlayerQueued, new PlayerQueuedEvent(Player(player.Slot), position));
        }
    }

    public void OnLastAlive(TeamSide team, int slot) => Raise(LastPlayerAlive, new LastPlayerAliveEvent(Map(team), Player(slot)));

    public void OnRoundEnded(int roundNumber, TeamSide? winner) =>
        Raise(RoundEnded, new RoundEndedEvent(roundNumber, winner is { } side ? Map(side) : null));

    private void Raise<T>(Action<T>? handlers, T e)
    {
        if (handlers is null)
        {
            return;
        }
        foreach (var handler in handlers.GetInvocationList().Cast<Action<T>>())
        {
            try
            {
                handler(e);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "A RetakeV4 API subscriber failed on {Event}", typeof(T).Name);
            }
        }
    }

    private RetakePlayer Player(int slot) => new(slot, _steamIdOf(slot));

    private RetakePlayer? PlayerOf(PlayerId? id) => id is { } player ? Player(player.Slot) : null;

    private static RetakeTeam Map(TeamSide side) => side == TeamSide.T ? RetakeTeam.T : RetakeTeam.CT;

    private static ContractSite? Map(DomainSite? site) => site switch
    {
        DomainSite.A => ContractSite.A,
        DomainSite.B => ContractSite.B,
        _ => null,
    };

    private static DomainSite Map(ContractSite site) => site == ContractSite.A ? DomainSite.A : DomainSite.B;
}
