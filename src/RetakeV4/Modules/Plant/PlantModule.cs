using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;
using RetakeV4.Adapters;
using RetakeV4.Configuration;
using RetakeV4.Domain.Common;
using RetakeV4.Domain.Events;
using RetakeV4.Domain.Plant;
using RetakeV4.Domain.Rounds;
using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;

namespace RetakeV4.Modules.Plant;

public sealed class PlantModule : IRetakeModule
{
    private PlantConfig _config = new();
    private ModuleContext? _context;
    private PreparationContext? _prepared;
    private Timer? _plantCheck;
    private AutoPlantDecision? _lastSkip;

    public string Name => "Plant";

    public IReadOnlyList<string> DependsOn { get; } = new[] { "Core", "Spawns" };

    private ModuleContext Context => _context ?? throw new InvalidOperationException("Plant module is not loaded");

    public ModuleConfig LoadConfig(JsonConfigStore store, ILogger logger)
    {
        var result = store.Load("plant.json", new PlantConfig(), new PlantConfigValidator());
        ConfigLogging.Report(logger, result.Issues);
        _config = result.Config;
        return _config;
    }

    public void Load(ModuleContext context)
    {
        _context = context;
        var hooks = context.Hooks;
        hooks.OnBus<RoundPrepared>(e => _prepared = e.Context);
        hooks.PreparationStep(new DelegatePreparationStep("plant", PreparationOrder.Plant, GiveBombForFastPlant));
        hooks.OnEvent<EventRoundFreezeEnd>("freeze_end", _ => OnFreezeEnd());
        hooks.OnEvent<EventBombBeginplant>("bomb_beginplant", _ => SpeedUpPlant());
        hooks.OnEvent<EventBombPlanted>("bomb_planted", _ => OnBombPlanted());
        hooks.OnBus<RoundPhaseChanged>(e =>
        {
            if (e.To == RoundPhase.PostRound)
            {
                StopPlantCheck();
            }
        });
        hooks.OnBus<MapStarted>(_ =>
        {
            _plantCheck = null;
            _prepared = null;
            _lastSkip = null;
        });
    }

    public void Unload()
    {
        StopPlantCheck();
        _context = null;
    }

    private PreparationContext GiveBombForFastPlant(PreparationContext context)
    {
        if (_config.Mode != PlantMode.FastPlant || context.Planter is not { } planter)
        {
            return context;
        }
        var player = Utilities.GetPlayerFromSlot(planter.Slot);
        if (player is not { IsValid: true })
        {
            return context;
        }
        player.GiveNamedItem("weapon_c4");
        player.ExecuteClientCommand("slot5");
        if (_config.PlantCheckSeconds > 0f)
        {
            player.PrintToCenter(Context.Text.For(player, "plant.fast.instructions", _config.PlantCheckSeconds));
        }
        return context;
    }

    private void OnFreezeEnd()
    {
        if (GameRulesAccessor.IsWarmup())
        {
            return;
        }
        if (_config.Mode == PlantMode.AutoPlant)
        {
            AutoPlant(_prepared);
            return;
        }
        StartPlantCheck(_prepared);
    }

    private void AutoPlant(PreparationContext? prepared)
    {
        var playersOnTeams = PlayerQueries.Humans().Count(p => PlayerQueries.SideOf(p) is not null);
        var decision = PlantRules.Evaluate(false, playersOnTeams, prepared?.Planter, prepared?.Site);
        if (decision != AutoPlantDecision.Plant)
        {
            ReportAutoPlantSkipped(decision, playersOnTeams, prepared);
            return;
        }
        _lastSkip = null;
        var planter = Utilities.GetPlayerFromSlot(prepared!.Planter!.Value.Slot);
        var pawn = planter?.PlayerPawn.Value;
        if (planter is not { IsValid: true } || pawn is not { IsValid: true } || pawn.AbsOrigin is null || pawn.TeamNum != (byte)CsTeam.Terrorist)
        {
            Context.Logger.LogWarning("Auto plant skipped: planter is no longer a valid living terrorist");
            return;
        }
        if (CreatePlantedBomb(pawn, prepared.Site!.Value))
        {
            FireBombPlantedEvent(planter, prepared.Site.Value);
        }
    }

    private void ReportAutoPlantSkipped(AutoPlantDecision decision, int playersOnTeams, PreparationContext? prepared)
    {
        var detail = decision switch
        {
            AutoPlantDecision.NotEnoughPlayers => $"only {playersOnTeams} human player(s) on a team (minimum {PlantRules.MinimumPlayers}, bots do not count)",
            AutoPlantDecision.NoPlanter => "no human Terrorist to carry the bomb",
            AutoPlantDecision.NoSite => "no bomb site was chosen (does the map have spawns? see !retake edit)",
            _ => decision.ToString(),
        };
        if (_lastSkip == decision)
        {
            Context.Logger.LogDebug("Auto plant skipped again: {Detail}", detail);
            return;
        }
        _lastSkip = decision;
        Context.Logger.LogWarning("Auto plant skipped, no bomb this round: {Detail} (planter {Planter}, site {Site})", detail, prepared?.Planter, prepared?.Site);
    }

    private bool CreatePlantedBomb(CCSPlayerPawn pawn, BombSite site)
    {
        var bomb = Utilities.CreateEntityByName<CPlantedC4>("planted_c4");
        if (bomb?.AbsOrigin is null)
        {
            Context.Logger.LogError("Auto plant failed: planted_c4 could not be created");
            return false;
        }
        bomb.AbsOrigin.X = pawn.AbsOrigin!.X;
        bomb.AbsOrigin.Y = pawn.AbsOrigin.Y;
        bomb.AbsOrigin.Z = pawn.AbsOrigin.Z;
        bomb.HasExploded = false;
        bomb.BombSite = (int)site;
        bomb.BombTicking = true;
        bomb.CannotBeDefused = false;
        bomb.DispatchSpawn();
        var rules = GameRulesAccessor.Get();
        if (rules is not null)
        {
            rules.BombPlanted = true;
            rules.BombDefused = false;
        }
        return true;
    }

    private static void FireBombPlantedEvent(CCSPlayerController planter, BombSite site)
    {
        var gameEvent = NativeAPI.CreateEvent("bomb_planted", true);
        NativeAPI.SetEventPlayerController(gameEvent, "userid", planter.Handle);
        NativeAPI.SetEventInt(gameEvent, "site", (int)site);
        NativeAPI.FireEvent(gameEvent, false);
    }

    private void SpeedUpPlant()
    {
        if (_config.Mode != PlantMode.FastPlant)
        {
            return;
        }
        var bomb = Utilities.FindAllEntitiesByDesignerName<CC4>("weapon_c4").FirstOrDefault();
        if (bomb is null)
        {
            return;
        }
        bomb.BombPlacedAnimation = false;
        bomb.ArmedTime = 0f;
    }

    private void StartPlantCheck(PreparationContext? prepared)
    {
        if (_config.PlantCheckSeconds <= 0f || prepared?.Planter is null)
        {
            return;
        }
        StopPlantCheck();
        _plantCheck = Context.Plugin.AddTimer(_config.PlantCheckSeconds, () =>
        {
            _plantCheck = null;
            _context?.Guard.Run(Name, "plant_check", () => CheckPlanted(prepared.Planter.Value));
        }, TimerFlags.STOP_ON_MAPCHANGE);
    }

    private void CheckPlanted(PlayerId planter)
    {
        if (Utilities.FindAllEntitiesByDesignerName<CPlantedC4>("planted_c4").Any())
        {
            return;
        }
        var name = Utilities.GetPlayerFromSlot(planter.Slot)?.PlayerName ?? "?";
        Context.Text.ChatAll("plant.failed", name);
        GameRulesAccessor.Get()?.TerminateRound(1f, RoundEndReason.CTsWin);
    }

    private void StopPlantCheck()
    {
        _plantCheck?.Kill();
        _plantCheck = null;
    }

    private void OnBombPlanted()
    {
        StopPlantCheck();
        Context.Bus.Publish(new BombPlanted(_prepared?.Site, _prepared?.Planter));
    }
}
