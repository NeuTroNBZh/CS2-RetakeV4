using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Timers;
using Microsoft.Extensions.Logging;
using RetakeV4.Adapters;
using RetakeV4.Configuration;
using RetakeV4.Domain.Events;
using RetakeV4.Domain.Rounds;
using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;

namespace RetakeV4.Modules.Core;

public sealed class CoreModule : IRetakeModule
{
    private CoreConfig _config = new();
    private ModuleContext? _context;
    private WarmupTracker _warmup = WarmupTracker.Start(16f);
    private const int WatchdogTraceEvery = 20;
    private int _watchdogTicks;
    private MapConfigGate _configGate = MapConfigGate.Idle;
    private Timer? _watchdog;
    private string _mapName = string.Empty;

    public string Name => "Core";

    public IReadOnlyList<string> DependsOn { get; } = Array.Empty<string>();

    private ModuleContext Context => _context ?? throw new InvalidOperationException("Core module is not loaded");

    public ModuleConfig LoadConfig(JsonConfigStore store, ILogger logger)
    {
        var result = store.Load("core.json", new CoreConfig(), new CoreConfigValidator());
        ConfigLogging.Report(logger, result.Issues);
        _config = result.Config;
        return _config;
    }

    public void Load(ModuleContext context)
    {
        _context = context;
        var hooks = context.Hooks;
        hooks.OnMapStart("map_start", mapName => StartForMap(mapName, false));
        hooks.OnEvent<EventRoundStart>("round_start", _ => OnRoundStart());
        hooks.OnEvent<EventRoundFreezeEnd>("freeze_end", _ => Context.Rounds.Handle(RoundSignal.FreezeEnded));
        hooks.OnEvent<EventRoundEnd>("round_end", _ => Context.Rounds.Handle(RoundSignal.RoundEnded));
        hooks.Command("css_retake_info", "Shows the RetakeV4 version", OnInfoCommand);
        hooks.OnBus<SpawnEditorStateChanged>(e =>
        {
            if (e.Active)
            {
                _warmup = _warmup.Settle();
            }
        });
        hooks.OnBus<ModulesReady>(e =>
        {
            if (!string.IsNullOrWhiteSpace(Server.MapName))
            {
                StartForMap(Server.MapName, e.HotReload);
            }
        });
        if (_config.Debug)
        {
            hooks.OnBus<RoundPhaseChanged>(e =>
                context.Logger.LogInformation("Round {Round}: {From} -> {To}", e.RoundNumber, e.From, e.To));
        }
    }

    public void Unload()
    {
        _watchdog?.Kill();
        _watchdog = null;
        _context = null;
    }

    private void OnRoundStart()
    {
        var (gate, execute) = _configGate.RoundStarted();
        _configGate = gate;
        if (execute)
        {
            ReapplyConfig();
        }
        if (GameRulesAccessor.Get()?.WarmupPeriod == true)
        {
            Context.Rounds.Handle(RoundSignal.WarmupStarted);
            return;
        }
        Context.Rounds.Handle(RoundSignal.WarmupEnded);
        Context.Rounds.Handle(RoundSignal.RoundStarted);
    }

    private void StartForMap(string mapName, bool isHotReload)
    {
        _mapName = mapName;
        Server.ExecuteCommand($"exec {_config.ExecConfig}");
        if (!isHotReload)
        {
            _configGate = _configGate.MapStarted();
        }
        _warmup = WarmupTracker.Start(_config.WarmupFallbackSeconds);
        var isWarmup = GameRulesAccessor.Get()?.WarmupPeriod ?? true;
        Context.Rounds.Reset(RoundTracker.InitialStateFor(isWarmup));
        Context.Bus.Publish(new MapStarted(mapName));
        RestartWatchdog();
        if (isHotReload)
        {
            Server.ExecuteCommand("mp_restartgame 1");
        }
    }

    // The gamemode config ran after our map start (bots, competitive warmup and timings): apply ours again, and restart a
    // warmup that was started with the competitive duration so the retake one is used.
    private void ReapplyConfig()
    {
        Server.ExecuteCommand($"exec {_config.ExecConfig}");
        if (GameRulesAccessor.Get()?.WarmupPeriod == true)
        {
            Server.ExecuteCommand("mp_warmup_start");
        }
        Context.Logger.LogInformation("Retake config {Config} applied again after the server configs", _config.ExecConfig);
    }

    private void RestartWatchdog()
    {
        _watchdog?.Kill();
        _watchdog = Context.Plugin.AddTimer(
            _config.WatchdogIntervalSeconds,
            () => Context.Guard.Run(Name, "warmup_watchdog", CheckWarmup),
            TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
    }

    private void CheckWarmup()
    {
        var rules = GameRulesAccessor.Get();
        if (rules is null)
        {
            return;
        }
        var snapshot = new WarmupSnapshot(rules.WarmupPeriod, rules.WarmupPeriodEnd, Server.CurrentTime);
        var (next, forceEnd) = _warmup.Evaluate(snapshot);
        TraceWarmup(snapshot, next, forceEnd);
        _warmup = next;
        if (!forceEnd)
        {
            return;
        }
        Context.Logger.LogInformation("Forcing warmup end on {Map}", _mapName);
        Server.ExecuteCommand("mp_warmup_end");
        Context.Bus.Publish(new WarmupForcedEnd(_mapName));
        Context.Text.ChatAll("core.warmup.forced_end");
    }

    // Diagnostic (core.json Debug): the warmup state the watchdog sees, every few seconds and whenever it decides.
    private void TraceWarmup(WarmupSnapshot snapshot, WarmupTracker next, bool forceEnd)
    {
        if (!_config.Debug || (++_watchdogTicks % WatchdogTraceEvery != 0 && !forceEnd))
        {
            return;
        }
        Context.Logger.LogInformation(
            "Warmup watchdog: warmup={Warmup} end={End} now={Now} seenAt={SeenAt} settled={Settled} forceEnd={ForceEnd} phase={Phase}",
            snapshot.IsWarmup, snapshot.WarmupPeriodEnd, snapshot.Now, next.WarmupSeenAt, next.Settled, forceEnd, Context.Rounds.State.Phase);
    }

    private void OnInfoCommand(CCSPlayerController? player, CommandInfo command)
    {
        var plugin = Context.Plugin;
        var args = new object[] { plugin.ModuleName, plugin.ModuleVersion, plugin.ModuleAuthor };
        command.ReplyToCommand(player is null
            ? Context.Text.Server("core.info.version", args)
            : Context.Text.For(player, "core.info.version", args));
    }
}
