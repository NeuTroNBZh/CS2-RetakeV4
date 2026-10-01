using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using Microsoft.Extensions.Logging;
using RetakeV4.Adapters;
using RetakeV4.Domain.Common;
using RetakeV4.Domain.Events;
using RetakeV4.Domain.Geometry;
using RetakeV4.Domain.MapCleanup;

namespace RetakeV4.Modules.MapCleanup;

// The module side the editor works against: current corrections, saving them, replaying the pass with a working set.
internal sealed record CleanupEditorHost(
    Func<IReadOnlyList<CleanupOverride>> Current,
    Func<IReadOnlyList<CleanupOverride>, bool> Save,
    Func<IReadOnlyDictionary<string, CleanupKind>, int> Replay);

// Game thread only. One admin edits at a time; the automatic pass is paused while a session is open.
internal sealed class CleanupEditor
{
    private readonly ModuleContext _context;
    private readonly CleanupEditorHost _host;
    private Dictionary<string, CleanupOverride> _working = new(StringComparer.Ordinal);
    private CleanupCandidate? _aimed;
    private int? _slot;
    private bool _dirty;

    public CleanupEditor(ModuleContext context, CleanupEditorHost host)
    {
        _context = context;
        _host = host;
    }

    public bool Active => _slot is not null;

    public void Enter(CCSPlayerController player)
    {
        if (_slot is { } slot && slot != player.Slot)
        {
            _context.Text.ChatAlert(player, "mapcleanup.editor.busy");
            return;
        }
        if (_slot is null)
        {
            _slot = player.Slot;
            _working = _host.Current().ToDictionary(o => o.Key, StringComparer.Ordinal);
            _dirty = false;
            _context.Text.Chat(player, "mapcleanup.editor.entered", _working.Count);
        }
        Pick(player);
        Show(player, refreshOnly: false);
    }

    public void OnSelected(HudMenuSelected e)
    {
        if (e.MenuId != CleanupEditorMenu.MenuId || e.Player.Slot != _slot || CleanupEditorMenu.Parse(e.ItemId) is not { } command)
        {
            return;
        }
        if (Utilities.GetPlayerFromSlot(e.Player.Slot) is not { IsValid: true } player)
        {
            End(null);
            return;
        }
        Run(player, command);
    }

    public void OnDisconnect(int slot)
    {
        if (slot == _slot)
        {
            End(null);
        }
    }

    // Map change or unload: the session ends without saving (the corrections belong to the previous map).
    public void Reset() => End(_slot is { } slot ? Utilities.GetPlayerFromSlot(slot) : null);

    private void Run(CCSPlayerController player, CleanupEditorCommand command)
    {
        switch (command.Kind)
        {
            case CleanupEditorCommandKind.Pick:
                Pick(player);
                break;
            case CleanupEditorCommandKind.Set when command.Value is { } kind:
                Change(_aimed, a => _working[a.Key] = new CleanupOverride(a.Key, kind, $"{a.Facts.ClassName} {a.Facts.ModelName ?? "-"}"));
                break;
            case CleanupEditorCommandKind.Auto:
                Change(_aimed, a => _working.Remove(a.Key));
                break;
            case CleanupEditorCommandKind.Test:
                _context.Text.Chat(player, "mapcleanup.editor.tested", _host.Replay(CleanupOverridesFormat.ToMap(_working.Values)));
                break;
            case CleanupEditorCommandKind.Save:
                Save(player);
                break;
            case CleanupEditorCommandKind.ExitSave:
                if (Save(player))
                {
                    End(player);
                }
                return;
            case CleanupEditorCommandKind.ExitDiscard:
                End(player);
                return;
        }
        Show(player, refreshOnly: true);
    }

    private void Change(CleanupCandidate? aimed, Action<CleanupCandidate> change)
    {
        if (aimed is null)
        {
            return;
        }
        change(aimed);
        _dirty = true;
    }

    private bool Save(CCSPlayerController player)
    {
        if (!_host.Save(_working.Values.ToList()))
        {
            _context.Text.ChatAlert(player, "mapcleanup.editor.save_failed");
            return false;
        }
        _dirty = false;
        _context.Text.Chat(player, "mapcleanup.editor.saved", _working.Count);
        return true;
    }

    private void Pick(CCSPlayerController player)
    {
        if (PlayerView.Of(player) is not { } view)
        {
            _aimed = null;
            return;
        }
        var candidates = CleanupEntities.Candidates();
        var handle = AimPicker.Pick(view.Eye, ViewGeometry.Forward(view.Angles), candidates.Select(c => (c.Handle, c.Facts.Origin)).ToList());
        _aimed = candidates.FirstOrDefault(c => c.Handle == handle);
    }

    private void Show(CCSPlayerController player, bool refreshOnly)
    {
        var view = _aimed is { } a
            ? new CleanupEditorView(a.Facts.ClassName, a.Facts.ModelName, a.Facts.TargetName, CleanupClassifier.Detect(a.Facts),
                CleanupClassifier.Classify(a.Facts, a.Key, CleanupOverridesFormat.ToMap(_working.Values)), _working.ContainsKey(a.Key), _dirty)
            : new CleanupEditorView(null, null, null, null, null, false, _dirty);
        _context.Bus.Publish(new HudMenuOpen(new PlayerId(player.Slot), CleanupEditorMenu.Build(view), refreshOnly));
    }

    private void End(CCSPlayerController? player)
    {
        if (_slot is not { } slot)
        {
            return;
        }
        _slot = null;
        _aimed = null;
        _working = new Dictionary<string, CleanupOverride>(StringComparer.Ordinal);
        _dirty = false;
        _context.Bus.Publish(new HudMenuClose(new PlayerId(slot), CleanupEditorMenu.MenuId));
        if (player is { IsValid: true })
        {
            _context.Text.Chat(player, "mapcleanup.editor.left");
        }
        _context.Logger.LogDebug("Map cleanup editor closed");
    }
}
