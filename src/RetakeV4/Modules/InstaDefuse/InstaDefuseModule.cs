using System.Globalization;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;
using RetakeV4.Adapters;
using RetakeV4.Configuration;
using RetakeV4.Domain.Events;
using RetakeV4.Domain.Geometry;
using RetakeV4.Domain.Hud;
using RetakeV4.Domain.InstaDefuse;
using RetakeV4.Domain.Rounds;

namespace RetakeV4.Modules.InstaDefuse;

public sealed class InstaDefuseModule : IRetakeModule
{
    private InstaDefuseConfig _config = new();
    private ModuleContext? _context;
    private ThreatState _threats = ThreatState.Empty;
    private float _bombPlantedAt = float.NaN;
    private bool _bombTicking;

    public string Name => "InstaDefuse";

    public IReadOnlyList<string> DependsOn { get; } = new[] { "Core" };

    private ModuleContext Context => _context ?? throw new InvalidOperationException("InstaDefuse module is not loaded");

    public ModuleConfig LoadConfig(JsonConfigStore store, ILogger logger)
    {
        var result = store.Load("instadefuse.json", new InstaDefuseConfig(), new InstaDefuseConfigValidator());
        ConfigLogging.Report(logger, result.Issues);
        _config = result.Config;
        return _config;
    }

    public void Load(ModuleContext context)
    {
        _context = context;
        var hooks = context.Hooks;
        hooks.OnEvent<EventGrenadeThrown>("grenade_thrown", e => _threats = _threats.GrenadeThrown(e.Weapon));
        hooks.OnEvent<EventHegrenadeDetonate>("hegrenade_detonate", _ => _threats = _threats.HeDetonated());
        hooks.OnEvent<EventMolotovDetonate>("molotov_detonate", _ => _threats = _threats.MolotovDetonated());
        hooks.OnEvent<EventInfernoStartburn>("inferno_startburn", e =>
            _threats = _threats.InfernoStarted(e.Entityid, new Vec3(e.X, e.Y, e.Z), BombPosition(), _config.InfernoDistance));
        hooks.OnEvent<EventInfernoExtinguish>("inferno_extinguish", e => _threats = _threats.InfernoEnded(e.Entityid));
        hooks.OnEvent<EventInfernoExpire>("inferno_expire", e => _threats = _threats.InfernoEnded(e.Entityid));
        hooks.OnEvent<EventBombPlanted>("bomb_planted", _ =>
        {
            _bombPlantedAt = Server.CurrentTime;
            _bombTicking = true;
        });
        hooks.OnEvent<EventBombDefused>("bomb_defused", _ => _bombTicking = false);
        hooks.OnEvent<EventBombExploded>("bomb_exploded", _ => _bombTicking = false);
        hooks.OnEvent<EventBombBegindefuse>("bomb_begindefuse", OnBeginDefuse);
        hooks.OnBus<RoundPhaseChanged>(e =>
        {
            if (e.To == RoundPhase.Preparing)
            {
                Reset();
            }
        });
    }

    public void Unload() => _context = null;

    private void Reset()
    {
        _threats = ThreatState.Empty;
        _bombPlantedAt = float.NaN;
        _bombTicking = false;
    }

    private void OnBeginDefuse(EventBombBegindefuse e)
    {
        if (GameRulesAccessor.IsWarmup() || !_bombTicking || e.Userid is not { IsValid: true, PawnIsAlive: true } defuser)
        {
            return;
        }
        var bomb = FindPlantedBomb();
        if (bomb is null || bomb.CannotBeDefused)
        {
            return;
        }
        var situation = new DefuseSituation(
            TerroristsAlive(),
            bomb.TimerLength - (Server.CurrentTime - _bombPlantedAt),
            bomb.DefuseLength,
            defuser.PawnHasDefuser);
        Apply(InstaDefusePolicy.Evaluate(_config.ToRules(), _threats, situation), defuser.PlayerName);
    }

    private void Apply(InstaDefuseDecision decision, string defuserName)
    {
        switch (decision)
        {
            case InstaDefuseDecision.Blocked blocked:
                Notify($"instadefuse.blocked.{blocked.Threat.ToString().ToLowerInvariant()}");
                break;
            case InstaDefuseDecision.NotEnoughTime notEnough:
                Notify("instadefuse.not_enough_time", defuserName, Seconds(notEnough.MissingSeconds));
                if (notEnough.ForceExplode)
                {
                    OnNextFrame("force_explode", bomb => bomb.C4Blow = 1f);
                }
                break;
            case InstaDefuseDecision.Allowed allowed:
                OnNextFrame("instant_defuse", bomb =>
                {
                    bomb.DefuseCountDown = 0f;
                    Notify("instadefuse.success", defuserName, Seconds(allowed.SecondsLeft));
                });
                break;
        }
    }

    private void OnNextFrame(string stage, Action<CPlantedC4> action) =>
        Server.NextFrame(() => _context?.Guard.Run(Name, stage, () =>
        {
            if (FindPlantedBomb() is { } bomb)
            {
                action(bomb);
            }
        }));

    private void Notify(string key, params object[] args)
    {
        if (_config.ChatNotification)
        {
            Context.Text.ChatAll(key, args);
            Context.Bus.Publish(new HudAlert(null, HudText.Of(key, args)));
        }
    }

    private static string Seconds(float seconds) => seconds.ToString("0.000", CultureInfo.InvariantCulture);

    private static CPlantedC4? FindPlantedBomb() =>
        Utilities.FindAllEntitiesByDesignerName<CPlantedC4>("planted_c4").FirstOrDefault(b => b.IsValid);

    private static Vec3? BombPosition() =>
        FindPlantedBomb()?.AbsOrigin is { } origin ? new Vec3(origin.X, origin.Y, origin.Z) : null;

    private static bool TerroristsAlive() =>
        Utilities.GetPlayers().Any(p => p is { IsValid: true, PawnIsAlive: true } && (CsTeam)p.TeamNum == CsTeam.Terrorist);
}
