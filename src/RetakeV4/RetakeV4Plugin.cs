using System.Diagnostics;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using Microsoft.Extensions.Logging;
using RetakeV4.Adapters;
using RetakeV4.Configuration;
using RetakeV4.Contracts;
using RetakeV4.Domain.Common;
using RetakeV4.Domain.Events;
using RetakeV4.Domain.Modules;
using RetakeV4.Domain.Rounds;
using RetakeV4.Localization;
using RetakeV4.Modules;

namespace RetakeV4;

[MinimumApiVersion(370)]
public sealed class RetakeV4Plugin : BasePlugin
{
    private const int MaxErrorsPerRound = 5;
    private static readonly TimeSpan SlowHandlerThreshold = TimeSpan.FromMilliseconds(50);

    private ModuleHost? _host;
    private EventBus? _bus;
    private IDisposable? _roundResetSubscription;

    public override string ModuleName => "RetakeV4";
    public override string ModuleVersion => "4.1.0";
    public override string ModuleAuthor => "NeuTroNBZh";
    public override string ModuleDescription => "Modular CS2 retake plugin";

    public override void Load(bool hotReload)
    {
        // Registers the API capability even if the Api module is disabled: consumers then get null instead of an exception.
        RetakeApiHost.Publish(null);
        WarmUpInBackground();
        var bus = new EventBus(OnBusError);
        _bus = bus;
        var guard = new ModuleGuard(MaxErrorsPerRound, OnGuardFailure,
            new SlowRunReporter(SlowHandlerThreshold, () => Stopwatch.GetElapsedTime(0), OnSlowRun));
        var pipeline = new PreparationPipeline(guard);
        var rounds = new RoundTracker(bus, pipeline, RoundState.Initial, GameRulesAccessor.TotalRoundsPlayed);
        var text = new TextService(Localizer);
        _roundResetSubscription = bus.Subscribe<RoundPhaseChanged>("bootstrap", e =>
        {
            if (e.To == RoundPhase.PostRound)
            {
                guard.ResetRound();
            }
        });

        _host = new ModuleHost(ModuleCatalog.CreateAll(), Logger, guard.Disable);
        _host.Start(new JsonConfigStore(ConfigDirectory()), (module, registrations) =>
            new ModuleContext(this, bus, guard, text, Logger, rounds,
                new ModuleHooks(this, bus, pipeline, guard, module.Name, registrations)));
        Logger.LogInformation("RetakeV4 {Version} loaded with modules: {Modules}", ModuleVersion, string.Join(", ", _host.LoadedModules));
        bus.Publish(new ModulesReady(hotReload));
    }

    public override void Unload(bool hotReload)
    {
        _host?.Stop();
        _host = null;
        _roundResetSubscription?.Dispose();
        _roundResetSubscription = null;
        _bus = null;
    }

    public override void OnAllPluginsLoaded(bool hotReload) => _bus?.Publish(new AllPluginsLoaded(hotReload));

    private void WarmUpInBackground() =>
        Task.Run(DomainWarmup.Run).ContinueWith(
            t => Logger.LogWarning(t.Exception, "Domain warm-up failed: the first round may stall the server"),
            TaskContinuationOptions.OnlyOnFaulted);

    private void OnSlowRun(SlowRun run) =>
        Logger.LogWarning("Slow handler {Module}/{Stage}: {Elapsed} ms", run.Module, run.Stage, (int)run.Elapsed.TotalMilliseconds);

    private string ConfigDirectory() =>
        Path.GetFullPath(Path.Combine(ModuleDirectory, "..", "..", "configs", "plugins", "RetakeV4"));

    private void OnBusError(BusError error) =>
        Logger.LogError(error.Exception, "Event {Event} handler of {Subscriber} failed", error.EventType.Name, error.Subscriber);

    private void OnGuardFailure(GuardFailure failure)
    {
        Logger.LogError(failure.Exception, "Module {Module} failed during {Stage}", failure.Module, failure.Stage);
        if (failure.ModuleDisabled)
        {
            Logger.LogError("Module {Module} disabled until next reload (too many errors this round)", failure.Module);
        }
    }
}
