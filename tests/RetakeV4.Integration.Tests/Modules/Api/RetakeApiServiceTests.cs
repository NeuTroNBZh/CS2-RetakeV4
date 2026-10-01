using Microsoft.Extensions.Logging;
using RetakeV4.Contracts;
using RetakeV4.Domain.Common;
using RetakeV4.Domain.Events;
using RetakeV4.Domain.Loadouts;
using RetakeV4.Domain.Rounds;
using RetakeV4.Domain.Spawns;
using RetakeV4.Domain.Teams;
using RetakeV4.Modules.Api;
using ContractSite = RetakeV4.Contracts.BombSite;
using ContractForceMode = RetakeV4.Contracts.ForceSiteMode;
using DomainSite = RetakeV4.Domain.Common.BombSite;

namespace RetakeV4.Integration.Tests.Modules.Api;

public class RetakeApiServiceTests
{
    private readonly EventBus _bus = new(_ => { });
    private readonly ListLogger _logger = new();
    private readonly RetakeApiService _api;

    public RetakeApiServiceTests() =>
        _api = new RetakeApiService(_bus, _logger, steamId => steamId == 76561198000000005UL ? 5 : null, slot => 76561198000000000UL + (ulong)slot);

    [Fact]
    public void RoundPrepared_UpdatesTheState_AndIsRaised()
    {
        var raised = new List<RoundPreparedEvent>();
        _api.RoundPrepared += raised.Add;
        _api.OnPhase(new RoundPhaseChanged(RoundPhase.Preparing, RoundPhase.FreezeTime, 3));
        _api.OnRoundPrepared(new RoundPrepared(new PreparationContext(3) { RoundType = "FullBuy", Site = DomainSite.B, Planter = new PlayerId(5) }));
        Assert.Equal(RetakeState.FreezeTime, _api.State);
        Assert.Equal(ContractSite.B, _api.CurrentSite);
        Assert.Equal("FullBuy", _api.CurrentRoundType);
        Assert.Equal(new RoundPreparedEvent(3, "FullBuy", ContractSite.B, new RetakePlayer(5, 76561198000000005UL)), Assert.Single(raised));
        Assert.Equal(1, _api.ApiVersion);
    }

    [Fact]
    public void FaultySubscriber_IsLoggedAndOthersStillRun()
    {
        var reached = false;
        _api.BombPlanted += _ => throw new InvalidOperationException("broken plugin");
        _api.BombPlanted += _ => reached = true;
        _api.OnBombPlanted(new BombPlanted(DomainSite.A, null));
        Assert.True(reached);
        Assert.Contains(_logger.Entries, e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public void ForceSiteAndScramble_AreRequestedLikeTheConsole()
    {
        var forces = new List<ForceSiteRequested>();
        var scrambles = new List<ScrambleRequested>();
        _bus.Subscribe<ForceSiteRequested>("test", forces.Add);
        _bus.Subscribe<ScrambleRequested>("test", scrambles.Add);
        _api.ForceSite(ContractSite.B, ContractForceMode.Sticky);
        _api.RequestScramble();
        Assert.Equal(new ForceSiteRequested(null, new ForceSiteRequest(new SiteForce(DomainSite.B, Domain.Spawns.ForceSiteMode.Sticky))), Assert.Single(forces));
        Assert.Null(Assert.Single(scrambles).Requester);
    }

    [Fact]
    public void QueuePositions_AndNewEntries()
    {
        var queued = new List<PlayerQueuedEvent>();
        _api.PlayerQueued += queued.Add;
        var state = TeamState.Empty with { Queue = new[] { new QueuedPlayer(new PlayerId(5), 0, 1) } };
        _api.OnTeams(new TeamStateChanged(state));
        _api.OnTeams(new TeamStateChanged(state));
        Assert.Equal(new PlayerQueuedEvent(new RetakePlayer(5, 76561198000000005UL), 1), Assert.Single(queued));
        Assert.Equal(1, _api.GetQueuePosition(76561198000000005UL));
        Assert.Null(_api.GetQueuePosition(1UL));
    }

    [Fact]
    public void Loadouts_LastAliveAndRoundEnd_AreTranslated()
    {
        var loadouts = new List<LoadoutAssignedEvent>();
        var lastAlive = new List<LastPlayerAliveEvent>();
        var ended = new List<RoundEndedEvent>();
        _api.LoadoutAssigned += loadouts.Add;
        _api.LastPlayerAlive += lastAlive.Add;
        _api.RoundEnded += ended.Add;
        var loadout = new Loadout("weapon_ak47", "weapon_glock", ArmorKind.KevlarHelmet, false, true, new[] { "weapon_flashbang" });
        _api.OnLoadouts(new LoadoutsAssigned(4, new Dictionary<PlayerId, Loadout> { [new PlayerId(2)] = loadout }));
        _api.OnLastAlive(TeamSide.CT, 2);
        _api.OnRoundEnded(4, TeamSide.T);
        var player = new RetakePlayer(2, 76561198000000002UL);
        var assigned = Assert.Single(Assert.Single(loadouts).Loadouts);
        Assert.Equal(player, assigned.Player);
        Assert.Equal("weapon_ak47", assigned.Primary);
        Assert.True(assigned.Zeus);
        Assert.Equal(new[] { "weapon_flashbang" }, assigned.Grenades);
        Assert.Equal(new LastPlayerAliveEvent(RetakeTeam.CT, player), Assert.Single(lastAlive));
        Assert.Equal(new RoundEndedEvent(4, RetakeTeam.T), Assert.Single(ended));
    }
}
