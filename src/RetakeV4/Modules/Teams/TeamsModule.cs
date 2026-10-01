using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;
using RetakeV4.Adapters;
using RetakeV4.Configuration;
using RetakeV4.Domain.Common;
using RetakeV4.Domain.Events;
using RetakeV4.Domain.Hud;
using RetakeV4.Domain.Rounds;
using RetakeV4.Domain.Teams;

namespace RetakeV4.Modules.Teams;

public sealed class TeamsModule : IRetakeModule
{
    private const string AdminFlag = "@retakev4/admin";
    private const int SpectatorArg = 1;
    private const int TerroristArg = 2;
    private const int CounterTerroristArg = 3;

    private readonly IRandom _random = SystemRandom.Shared;
    private TeamsConfig _config = new();
    private ModuleContext? _context;
    private TeamState _state = TeamState.Empty;
    private bool _scrambleRequested;
    private bool _restartedForInconsistency;

    public string Name => "Teams";

    public IReadOnlyList<string> DependsOn { get; } = new[] { "Core" };

    private ModuleContext Context => _context ?? throw new InvalidOperationException("Teams module is not loaded");

    public ModuleConfig LoadConfig(JsonConfigStore store, ILogger logger)
    {
        var result = store.Load("teams.json", new TeamsConfig(), new TeamsConfigValidator());
        ConfigLogging.Report(logger, result.Issues);
        _config = result.Config;
        return _config;
    }

    public void Load(ModuleContext context)
    {
        _context = context;
        var hooks = context.Hooks;
        hooks.CommandListener("jointeam", OnJoinTeam, HookMode.Pre);
        hooks.OnEventHook<EventPlayerTeam>("player_team", e =>
        {
            e.Silent = true;
            return HookResult.Continue;
        }, HookMode.Pre);
        hooks.OnEvent<EventPlayerDisconnect>("player_disconnect", e =>
        {
            if (e.Userid is { } player)
            {
                SetState(TeamPlanner.Leave(_state, new PlayerId(player.Slot)));
            }
        });
        hooks.OnEvent<EventRoundEnd>("round_end", OnRoundEnd);
        hooks.OnEvent<EventRoundFreezeEnd>("freeze_end", _ => Reconcile());
        hooks.OnBus<MapStarted>(_ => ResetForMap());
        hooks.OnBus<RoundPhaseChanged>(OnPhaseChanged);
        hooks.OnBus<ModulesReady>(_ => Context.Bus.Publish(new TeamStateChanged(_state)));
        hooks.Command("css_retake_scramble", "Scrambles the teams at the end of the round", OnScrambleCommand);
        hooks.OnBus<ScrambleRequested>(_ => RequestScramble());
    }

    public void Unload() => _context = null;

    private void SetState(TeamState state)
    {
        _state = state;
        _context?.Bus.Publish(new TeamStateChanged(state));
    }

    private HookResult OnJoinTeam(CCSPlayerController? player, CommandInfo info)
    {
        if (player is null || !player.IsValid || player.IsBot || player.IsHLTV)
        {
            return HookResult.Continue;
        }
        if (!int.TryParse(info.GetArg(1), out var requestedArg) || requestedArg is < SpectatorArg or > CounterTerroristArg)
        {
            return HookResult.Handled;
        }
        // The join can end the round (round_end and every module's reaction): run it on the next frame, outside the
        // processing of the client's messages, which the engine aborts with a kick past ~500 ms.
        var slot = player.Slot;
        Server.NextFrame(() => _context?.Guard.Run(Name, "jointeam", () => Join(slot, requestedArg)));
        return requestedArg == SpectatorArg ? HookResult.Continue : HookResult.Handled;
    }

    private void Join(int slot, int requestedArg)
    {
        var player = Utilities.GetPlayerFromSlot(slot);
        if (player is not { IsValid: true })
        {
            return;
        }
        var id = new PlayerId(slot);
        if (requestedArg == SpectatorArg)
        {
            SetState(TeamPlanner.Leave(_state, id));
            return;
        }
        var requested = requestedArg == TerroristArg ? TeamSide.T : TeamSide.CT;
        var result = TeamPlanner.RequestJoin(_state, id, PriorityOf(player), GameRulesAccessor.IsWarmup(), requested, _config.ToRules());
        SetState(result.State);
        ApplyJoin(player, result, requested);
    }

    private void ApplyJoin(CCSPlayerController player, JoinResult result, TeamSide requested)
    {
        switch (result.Outcome)
        {
            case JoinOutcome.JoinedNow:
                player.ChangeTeam(PlayerQueries.ToCsTeam(result.Side!.Value));
                if (result.RestartRound)
                {
                    GameRulesAccessor.Get()?.TerminateRound(1f, RoundEndReason.RoundDraw);
                }
                break;
            case JoinOutcome.AlreadyPlaying when result.Side != requested:
                Context.Text.ChatAlert(player, "teams.switch.refused");
                break;
            case JoinOutcome.Queued:
            case JoinOutcome.AlreadyQueued:
                if ((CsTeam)player.TeamNum != CsTeam.Spectator)
                {
                    player.ChangeTeam(CsTeam.Spectator);
                }
                Context.Text.Chat(player, "teams.queue.joined", result.QueuePosition ?? 0);
                break;
        }
    }

    private int PriorityOf(CCSPlayerController player) =>
        _config.PriorityFlags
            .Where(f => f.Flag.StartsWith('#')
                ? AdminManager.PlayerInGroup(player, f.Flag)
                : AdminManager.PlayerHasPermissions(player, f.Flag))
            .Select(f => f.Priority)
            .DefaultIfEmpty(0)
            .Max();

    private void OnRoundEnd(EventRoundEnd e)
    {
        if (GameRulesAccessor.IsWarmup())
        {
            return;
        }
        var winner = e.Winner switch
        {
            (int)CsTeam.Terrorist => RoundWinner.T,
            (int)CsTeam.CounterTerrorist => RoundWinner.CT,
            _ => RoundWinner.None,
        };
        var plan = TeamPlanner.PlanRoundEnd(_state, winner, _scrambleRequested, _config.ToRules(), _random);
        _scrambleRequested = false;
        if (winner == RoundWinner.T && !plan.Scrambled)
        {
            Context.Text.ChatAll("teams.round.t_streak", plan.State.TWinStreak);
        }
        ApplyPlan(plan);
    }

    private void OnPhaseChanged(RoundPhaseChanged e)
    {
        if (e.From != RoundPhase.Warmup || e.To != RoundPhase.PostRound)
        {
            return;
        }
        var timer = new StepTimer();
        AdoptPlayersOnTeams();
        timer.Mark("adopt");
        var plan = TeamPlanner.PlanRoundEnd(_state, RoundWinner.None, true, _config.ToRules(), _random);
        timer.Mark("plan");
        ApplyPlan(plan);
        timer.Mark($"apply ({plan.Moves.Count} moves)");
        if (plan.Moves.Count > 0)
        {
            // Players already respawned in their warmup team for round 1: restart so they spawn
            // in their new team (also resets TotalRoundsPlayed for the round type sequence).
            Server.ExecuteCommand("mp_restartgame 1");
            timer.Mark("restart");
        }
        timer.ReportIfSlow(Context.Logger, "Teams/warmup end");
    }

    private void ResetForMap()
    {
        SetState(TeamState.Empty);
        _scrambleRequested = false;
        AdoptPlayersOnTeams();
    }

    private void AdoptPlayersOnTeams()
    {
        var onTeams = PlayerQueries.Humans()
            .Select(p => (Player: new PlayerId(p.Slot), Side: PlayerQueries.SideOf(p)))
            .Where(p => p.Side is not null)
            .Select(p => (p.Player, p.Side!.Value))
            .ToList();
        SetState(TeamPlanner.Adopt(_state, onTeams, _config.ToRules()));
    }

    private void ApplyPlan(TeamPlan plan)
    {
        SetState(plan.State);
        foreach (var move in plan.Moves)
        {
            var player = Utilities.GetPlayerFromSlot(move.Player.Slot);
            if (player is null || !player.IsValid)
            {
                continue;
            }
            // A queued player is a spectator without a pawn: ChangeTeam puts them on the team for
            // the next spawn, whereas SwitchTeam is meant for live players and may not apply.
            if (move.Reason == MoveReason.EnteredFromQueue)
            {
                player.ChangeTeam(PlayerQueries.ToCsTeam(move.To));
            }
            else
            {
                player.SwitchTeam(PlayerQueries.ToCsTeam(move.To));
            }
            Context.Text.Chat(player, ReasonKey(move.Reason), move.To.ToString());
            Context.Bus.Publish(new HudAlert(new PlayerId(player.Slot), HudText.Of(ReasonKey(move.Reason), move.To.ToString())));
        }
        NotifyQueue();
    }

    private void NotifyQueue()
    {
        foreach (var queued in _state.OrderedQueue())
        {
            var player = Utilities.GetPlayerFromSlot(queued.Player.Slot);
            if (player is { IsValid: true })
            {
                Context.Text.Chat(player, "teams.queue.joined", _state.QueuePosition(queued.Player) ?? 0);
            }
        }
    }

    private static string ReasonKey(MoveReason reason) => reason switch
    {
        MoveReason.SwitchedAfterCtWin => "teams.move.switched_after_ct_win",
        MoveReason.EnteredFromQueue => "teams.move.entered_from_queue",
        MoveReason.Scrambled => "teams.move.scrambled",
        _ => "teams.move.balanced",
    };

    private void Reconcile()
    {
        if (GameRulesAccessor.IsWarmup())
        {
            return;
        }
        var humans = PlayerQueries.Humans();
        var actual = humans.ToDictionary(p => new PlayerId(p.Slot), PlayerQueries.SideOf);
        var result = TeamPlanner.Reconcile(_state, actual, id => PriorityOf(humans.First(h => h.Slot == id.Slot)));
        SetState(result.State);
        foreach (var intruder in result.ToSpectator.Select(i => Utilities.GetPlayerFromSlot(i.Slot)).OfType<CCSPlayerController>())
        {
            intruder.ChangeTeam(CsTeam.Spectator);
            Context.Text.Chat(intruder, "teams.queue.joined", _state.QueuePosition(new PlayerId(intruder.Slot)) ?? 0);
        }
        foreach (var fix in result.Fixes)
        {
            Utilities.GetPlayerFromSlot(fix.Player.Slot)?.SwitchTeam(PlayerQueries.ToCsTeam(fix.To));
        }
        var restart = _config.RestartOnInconsistency && result.Fixes.Count > 0 && !_restartedForInconsistency;
        _restartedForInconsistency = restart;
        if (restart)
        {
            Context.Text.ChatAll("teams.inconsistent");
            GameRulesAccessor.Get()?.TerminateRound(1f, RoundEndReason.RoundDraw);
        }
    }

    private void OnScrambleCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player is not null && !AdminManager.PlayerHasPermissions(player, AdminFlag))
        {
            Context.Text.ChatAlert(player, "teams.no_permission");
            return;
        }
        RequestScramble();
    }

    private void RequestScramble()
    {
        _scrambleRequested = true;
        Context.Text.ChatAll("teams.scramble.requested");
        Context.Bus.Publish(new HudAlert(null, HudText.Of("teams.scramble.requested")));
    }
}
