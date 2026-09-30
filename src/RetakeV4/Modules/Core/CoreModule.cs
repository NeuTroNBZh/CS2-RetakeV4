using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Timers;
using Microsoft.Extensions.Logging;
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
    private Timer? _watchdog;
    private IDisposable? _debugSubscription;
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
        var plugin = context.Plugin;
        plugin.RegisterListener<Listeners.OnMapStart>(mapName => Context.Guard.Run(Name, "map_start", () => StartForMap(mapName, false)));
        plugin.RegisterEventHandler<EventRoundStart>((_, _) => Guarded("round_start", OnRoundStart));
        plugin.RegisterEventHandler<EventRoundFreezeEnd>((_, _) => Guarded("freeze_end", () => Context.Rounds.Handle(RoundSignal.FreezeEnded)));
        plugin.RegisterEventHandler<EventRoundEnd>((_, _) => Guarded("round_end", () => Context.Rounds.Handle(RoundSignal.RoundEnded)));
        plugin.AddCommand("css_retake_info", "Shows the RetakeV4 version", OnInfoCommand);
        if (_config.Debug)
        {
            _debugSubscription = context.Bus.Subscribe<RoundPhaseChanged>(Name, e =>
                context.Logger.LogInformation("Round {Round}: {From} -> {To}", e.RoundNumber, e.From, e.To));
        }
        if (!string.IsNullOrWhiteSpace(Server.MapName))
        {
            StartForMap(Server.MapName, context.HotReload);
        }
    }

    public void Unload()
    {
        _watchdog?.Kill();
        _watchdog = null;
        _debugSubscription?.Dispose();
        _debugSubscription = null;
        _context = null;
    }

    private HookResult Guarded(string stage, Action action)
    {
        Context.Guard.Run(Name, stage, action);
        return HookResult.Continue;
    }

    private void OnRoundStart()
    {
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
        var (next, forceEnd) = _warmup.Evaluate(new WarmupSnapshot(rules.WarmupPeriod, rules.WarmupPeriodEnd, Server.CurrentTime));
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

    private void OnInfoCommand(CCSPlayerController? player, CommandInfo command)
    {
        var plugin = Context.Plugin;
        var args = new object[] { plugin.ModuleName, plugin.ModuleVersion, plugin.ModuleAuthor };
        command.ReplyToCommand(player is null
            ? Context.Text.Server("core.info.version", args)
            : Context.Text.For(player, "core.info.version", args));
    }
}
