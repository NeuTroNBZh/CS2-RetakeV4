using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;
using RetakeV4.Adapters;
using RetakeV4.Configuration;
using RetakeV4.Contracts;
using RetakeV4.Domain.Common;
using RetakeV4.Domain.Events;
using RetakeV4.Domain.Rounds;
using RetakeV4.Domain.Teams;

namespace RetakeV4.Modules.Api;

// Exposes IRetakeApi to other plugins through the "retakev4:api" capability.
public sealed class ApiModule : IRetakeModule
{
    private ApiConfig _config = new();
    private ModuleContext? _context;
    private RetakeApiService? _service;
    private LastAliveTracker _lastAlive = LastAliveTracker.Empty;

    public string Name => "Api";

    public IReadOnlyList<string> DependsOn { get; } = new[] { "Core" };

    public ModuleConfig LoadConfig(JsonConfigStore store, ILogger logger)
    {
        var result = store.Load("api.json", new ApiConfig());
        ConfigLogging.Report(logger, result.Issues);
        _config = result.Config;
        return _config;
    }

    public void Load(ModuleContext context)
    {
        _context = context;
        var service = new RetakeApiService(context.Bus, context.Logger, SlotOf, SteamIdOf);
        _service = service;
        RetakeApiHost.Publish(service);
        var hooks = context.Hooks;
        hooks.OnBus<RoundPhaseChanged>(service.OnPhase);
        hooks.OnBus<RoundPrepared>(service.OnRoundPrepared);
        hooks.OnBus<BombPlanted>(service.OnBombPlanted);
        hooks.OnBus<LoadoutsAssigned>(service.OnLoadouts);
        hooks.OnBus<TeamStateChanged>(service.OnTeams);
        hooks.OnEvent<EventRoundStart>("api_round_start", _ => _lastAlive = LastAliveTracker.Empty);
        hooks.OnEvent<EventPlayerDeath>("api_player_death", _ =>
            Server.NextFrame(() => _context?.Guard.Run(Name, "api_last_alive", CheckLastAlive)));
        hooks.OnEvent<EventRoundEnd>("api_round_end", e => service.OnRoundEnded(context.Rounds.State.RoundNumber, WinnerOf(e.Winner)));
    }

    public void Unload()
    {
        RetakeApiHost.Publish(null);
        _service = null;
        _context = null;
    }

    // The death is processed on the next frame, once the victim is no longer counted as alive.
    private void CheckLastAlive()
    {
        if (_service is not { } service || _context?.Rounds.State.Phase != RoundPhase.Live)
        {
            return;
        }
        var players = PlayerQueries.Humans()
            .Select(p => (Player: p, Side: PlayerQueries.SideOf(p)))
            .Where(p => p.Side is not null)
            .ToList();
        TeamCount Count(TeamSide side) =>
            new(players.Count(p => p.Side == side && p.Player.PawnIsAlive), players.Count(p => p.Side == side));
        var (tracker, newly) = _lastAlive.Update(Count(TeamSide.T), Count(TeamSide.CT));
        _lastAlive = tracker;
        foreach (var side in newly)
        {
            service.OnLastAlive(side, players.First(p => p.Side == side && p.Player.PawnIsAlive).Player.Slot);
        }
    }

    private static TeamSide? WinnerOf(int winner) => (CsTeam)winner switch
    {
        CsTeam.Terrorist => TeamSide.T,
        CsTeam.CounterTerrorist => TeamSide.CT,
        _ => null,
    };

    private static int? SlotOf(ulong steamId) => PlayerQueries.Humans().FirstOrDefault(p => p.SteamID == steamId)?.Slot;

    private static ulong SteamIdOf(int slot) => Utilities.GetPlayerFromSlot(slot) is { IsValid: true } player ? player.SteamID : 0UL;
}
