using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using Microsoft.Extensions.Logging;
using RetakeV4.Adapters;
using RetakeV4.Configuration;
using RetakeV4.Domain.Common;
using RetakeV4.Domain.Events;
using RetakeV4.Domain.MapCleanup;
using RetakeV4.Domain.Rounds;

namespace RetakeV4.Modules.MapCleanup;

// Opens doors and breaks windows and vents once per round (plus one check at freeze end), never anything else.
public sealed class MapCleanupModule : IRetakeModule
{

    private const int DebugIgnoredLimit = 40;

    private MapCleanupConfig _config = new();
    private ModuleContext? _context;
    private CleanupOverrideStore? _store;
    private CleanupEditor? _editor;
    private string? _map;
    private IReadOnlyList<CleanupOverride> _current = Array.Empty<CleanupOverride>();
    private IReadOnlyDictionary<string, CleanupKind> _overrides = new Dictionary<string, CleanupKind>();
    private IReadOnlyList<(CleanupTarget Target, string ClassName)> _lastTargets = Array.Empty<(CleanupTarget, string)>();
    private bool _spawnEditing;

    public string Name => "MapCleanup";

    public IReadOnlyList<string> DependsOn { get; } = new[] { "Core", "Hud" };

    public ModuleConfig LoadConfig(JsonConfigStore store, ILogger logger)
    {
        var result = store.Load("mapcleanup.json", new MapCleanupConfig(), new MapCleanupConfigValidator());
        ConfigLogging.Report(logger, result.Issues);
        _config = result.Config;
        return _config;
    }

    public void Load(ModuleContext context)
    {
        _context = context;
        _store = new CleanupOverrideStore(Path.GetFullPath(
            Path.Combine(context.Plugin.ModuleDirectory, "..", "..", "configs", "plugins", "RetakeV4", "mapcleanup")));
        _editor = new CleanupEditor(context, new CleanupEditorHost(_config.Debug, () => _current, SaveOverrides, RunPass));
        var hooks = context.Hooks;
        hooks.OnBus<MapStarted>(e => StartMap(e.MapName));
        hooks.OnBus<SpawnEditorStateChanged>(e => _spawnEditing = e.Active);
        hooks.PreparationStep(new DelegatePreparationStep("cleanup", PreparationOrder.Cleanup, ctx =>
        {
            Prepare();
            return ctx;
        }));
        if (_config.FreezeEndCheck)
        {
            hooks.OnEvent<EventRoundFreezeEnd>("freeze_end", _ => Recheck());
        }
        hooks.OnBus<MapCleanupEditorRequested>(e => OpenEditor(e.Player));
        hooks.OnBus<MapCleanupReplayRequested>(e => e.Reply($"Map cleanup replayed: {RunPass(_overrides)} entity(ies)"));
        hooks.OnBus<HudMenuSelected>(e => _editor?.OnSelected(e));
        hooks.OnBus<HudMenuOpen>(e => _editor?.OnMenuOpened(e));
        hooks.OnEvent<EventPlayerDisconnect>("editor_disconnect", e =>
        {
            if (e.Userid is { } player)
            {
                _editor?.OnDisconnect(player.Slot);
            }
        });
    }

    public void Unload()
    {
        _editor?.Reset();
        _editor = null;
        _store = null;
        _context = null;
    }

    private bool ShouldRun() =>
        _context is not null && !_spawnEditing && _editor is { Active: false } && !GameRulesAccessor.IsWarmup();

    // Targets from an earlier round point at entities recreated since: the freeze-end check only replays this round's pass.
    private void Prepare()
    {
        _editor?.ExpireIfIdle();
        _lastTargets = Array.Empty<(CleanupTarget, string)>();
        if (ShouldRun())
        {
            RunPass(_overrides);
        }
        else if (_config.Debug)
        {
            _context?.Logger.LogInformation("Map cleanup skipped: spawn editor {SpawnEditing}, cleanup editor {Editing}, warmup {Warmup}",
                _spawnEditing, _editor?.Active ?? false, GameRulesAccessor.IsWarmup());
        }
    }

    private void StartMap(string map)
    {
        _editor?.Reset();
        _map = map;
        _lastTargets = Array.Empty<(CleanupTarget, string)>();
        SetCurrent(LoadOverrides(map));
    }

    private IReadOnlyList<CleanupOverride> LoadOverrides(string map)
    {
        if (_store is null || _context is not { } context)
        {
            return Array.Empty<CleanupOverride>();
        }
        try
        {
            var (overrides, issues) = _store.Load(map);
            foreach (var issue in issues)
            {
                context.Logger.LogWarning("Map cleanup corrections for {Map}: {Issue}", map, issue);
            }
            return overrides;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            context.Logger.LogWarning(ex, "Map cleanup corrections for {Map} could not be read; automatic rules only", map);
            return Array.Empty<CleanupOverride>();
        }
    }

    private bool SaveOverrides(IReadOnlyList<CleanupOverride> overrides)
    {
        if (_store is null || _map is not { } map || _context is not { } context)
        {
            return false;
        }
        try
        {
            _store.Save(map, overrides);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            context.Logger.LogError(ex, "Map cleanup corrections for {Map} could not be saved", map);
            return false;
        }
        SetCurrent(overrides);
        context.Logger.LogInformation("Map cleanup: {Count} correction(s) saved for {Map}", overrides.Count, map);
        return true;
    }

    private void SetCurrent(IReadOnlyList<CleanupOverride> overrides)
    {
        _current = overrides;
        _overrides = CleanupOverridesFormat.ToMap(overrides);
    }

    private int RunPass(IReadOnlyDictionary<string, CleanupKind> overrides)
    {
        var candidates = CleanupEntities.Candidates();
        var classes = candidates.ToDictionary(c => c.Handle, c => c.Facts.ClassName);
        var plan = CleanupPlan.Build(candidates, overrides, _config.ToSettings(), SystemRandom.Shared);
        _lastTargets = plan.Select(t => (t, classes[t.Handle])).ToList();
        if (_config.Debug)
        {
            LogPass(candidates, plan, overrides);
        }
        return ApplyAll(_lastTargets);
    }

    // Debug only: what the map offers and what was left alone, to write corrections or adjust the automatic rules.
    private void LogPass(IReadOnlyList<CleanupCandidate> candidates, IReadOnlyList<CleanupTarget> plan, IReadOnlyDictionary<string, CleanupKind> overrides)
    {
        if (_context is not { } context)
        {
            return;
        }
        context.Logger.LogInformation("Map cleanup on {Map}: {Doors} door(s), {Windows} window(s), {Vents} vent(s) out of {Candidates} candidate(s) ({Classes})",
            _map, plan.Count(t => t.Kind == CleanupKind.Door), plan.Count(t => t.Kind == CleanupKind.Window), plan.Count(t => t.Kind == CleanupKind.Vent),
            candidates.Count, string.Join(", ", candidates.GroupBy(c => c.Facts.ClassName).Select(g => $"{g.Key}={g.Count()}")));
        context.Logger.LogInformation("Map cleanup on {Map}: related entity classes {Classes}", _map, CleanupEntities.RelatedClasses());
        var ignored = candidates
            .Where(c => CleanupClassifier.Classify(c.Facts, c.Key, overrides) == CleanupKind.Ignore)
            .Select(c => $"{c.Key} [{c.Facts.ModelName ?? "no model"}]")
            .Distinct()
            .Take(DebugIgnoredLimit)
            .ToList();
        if (ignored.Count > 0)
        {
            context.Logger.LogInformation("Map cleanup on {Map}: left alone {Entities}", _map, string.Join(" | ", ignored));
        }
    }

    private void Recheck()
    {
        if (ShouldRun())
        {
            ApplyAll(_lastTargets);
        }
    }

    // One failing entity must not stop the others; failures are summed into a single log line.
    private int ApplyAll(IReadOnlyList<(CleanupTarget Target, string ClassName)> targets)
    {
        var applied = 0;
        var failed = 0;
        Exception? lastError = null;
        foreach (var (target, className) in targets)
        {
            try
            {
                applied += CleanupEntities.Apply(target, className) ? 1 : 0;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // Spec: a failing entity is skipped and counted, whatever CounterStrikeSharp threw for it.
                failed++;
                lastError = ex;
            }
        }
        if (failed > 0)
        {
            _context?.Logger.LogWarning(lastError, "Map cleanup: {Failed} of {Count} entities failed on {Map}", failed, targets.Count, _map);
        }
        return applied;
    }

    private void OpenEditor(PlayerId id)
    {
        if (_editor is null || Utilities.GetPlayerFromSlot(id.Slot) is not { IsValid: true } player)
        {
            return;
        }
        if (!RetakePermissions.IsAdmin(player))
        {
            _context?.Text.ChatAlert(player, "admin.no_permission");
            return;
        }
        _editor.Enter(player);
    }
}
