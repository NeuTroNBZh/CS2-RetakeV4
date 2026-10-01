using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;
using RetakeV4.Adapters;
using RetakeV4.Domain.Common;
using RetakeV4.Domain.Events;
using RetakeV4.Domain.Geometry;
using RetakeV4.Domain.Spawns;
using SpawnPoint = RetakeV4.Domain.Spawns.SpawnPoint;

namespace RetakeV4.Modules.Spawns;

// Game thread only. All editors share the catalog's set; markers are rebuilt after every round cleanup.
internal sealed class SpawnEditor
{
    private const float NearestDistance = 150f;
    private const int NearestRefreshTicks = 8;

    private readonly ModuleContext _context;
    private readonly SpawnCatalog _catalog;
    private readonly SpawnMarkers _markers = new();
    private readonly Dictionary<int, Guid?> _nearest = new();
    private EditorState _state = EditorState.Idle;
    private bool _entitiesAllowed = true;
    private int _tick;

    public SpawnEditor(ModuleContext context, SpawnCatalog catalog)
    {
        _context = context;
        _catalog = catalog;
    }

    public bool IsEditing(int slot) => _state.IsEditing(slot);

    public void Enter(CCSPlayerController player)
    {
        var wasEditing = _state.IsEditing(player.Slot);
        var (state, startPause) = _state.Enter(player.Slot, GameRulesAccessor.IsWarmup());
        _state = state;
        if (startPause)
        {
            Server.ExecuteCommand("mp_warmup_start");
            Server.ExecuteCommand("mp_warmup_pausetimer 1");
            _context.Bus.Publish(new SpawnEditorStateChanged(true));
            RefreshMarkers();
        }
        if (!wasEditing)
        {
            Notify(player, "spawns.editor.entered", _catalog.Set.Spawns.Count);
        }
        OpenMenu(player, refreshOnly: false);
    }

    public void Leave(CCSPlayerController? player, int slot)
    {
        if (!_state.IsEditing(slot))
        {
            return;
        }
        var (state, endPause, endWarmup) = _state.Leave(slot);
        _state = state;
        _nearest.Remove(slot);
        _markers.ClearRing(slot);
        if (player is { IsValid: true })
        {
            SetNoclip(player, false);
            _context.Bus.Publish(new HudMenuClose(new PlayerId(slot), SpawnEditorMenu.MenuId));
            Notify(player, "spawns.editor.left");
        }
        if (!endPause)
        {
            return;
        }
        _markers.Clear();
        Server.ExecuteCommand("mp_warmup_pausetimer 0");
        if (endWarmup)
        {
            Server.ExecuteCommand("mp_warmup_end");
        }
        _context.Bus.Publish(new SpawnEditorStateChanged(false));
    }

    // css_retake_edit [save|discard|exit]: no argument enters (or reopens the menu).
    public void HandleEditCommand(CCSPlayerController player, string? argument)
    {
        switch (argument?.Trim().ToLowerInvariant())
        {
            case null or "":
                Enter(player);
                break;
            case "save":
                if (Save(player))
                {
                    Leave(player, player.Slot);
                }
                break;
            case "discard":
                Discard(player);
                break;
            case "exit" when _catalog.Set.Dirty:
                Notify(player, "spawns.editor.unsaved");
                break;
            case "exit":
                Leave(player, player.Slot);
                break;
            default:
                Notify(player, "spawns.editor.usage_edit");
                break;
        }
    }

    public void AddHere(CCSPlayerController player, SpawnToAdd add)
    {
        if (Pose(player) is not { } pose)
        {
            return;
        }
        var spawn = new SpawnPoint(Guid.NewGuid(), add.Team, add.Site, add.CanPlant, pose.Origin, pose.Angles);
        Apply(_catalog.Set.Add(spawn));
        Notify(player, "spawns.editor.added", _catalog.Set.Label(spawn));
    }

    public void DeleteNearest(CCSPlayerController player)
    {
        if (NearestTo(player) is not { } nearest)
        {
            Notify(player, "spawns.editor.none_nearby");
            return;
        }
        var label = _catalog.Set.Label(nearest);
        Apply(_catalog.Set.Remove(nearest.Id));
        Notify(player, "spawns.editor.deleted", label);
    }

    public void TeleportToNumber(CCSPlayerController player, int number)
    {
        var spawns = _catalog.Set.Spawns;
        if (number < 1 || number > spawns.Count)
        {
            Notify(player, "spawns.editor.not_found", number);
            return;
        }
        TeleportTo(player, spawns[number - 1]);
    }

    public void TeleportToPosition(CCSPlayerController player, Vec3 position)
    {
        if (player.PlayerPawn.Value is not { IsValid: true } pawn)
        {
            return;
        }
        pawn.Teleport(new Vector(position.X, position.Y, position.Z), pawn.AbsRotation ?? new QAngle(0f, 0f, 0f), new Vector(0f, 0f, 0f));
    }

    public bool Save(CCSPlayerController? player)
    {
        if (_catalog.Save() is { } error)
        {
            Notify(player, "spawns.editor.save_failed", error);
            return false;
        }
        Notify(player, "spawns.editor.saved", _catalog.Set.Spawns.Count);
        RefreshMenus();
        return true;
    }

    public void Reload(CCSPlayerController? player)
    {
        _catalog.Reload();
        RefreshMarkers();
        Notify(player, "spawns.editor.reloaded", _catalog.Set.Spawns.Count);
    }

    public HookResult OnNoclipCommand(CCSPlayerController? player)
    {
        if (player is not { IsValid: true } || !_state.IsEditing(player.Slot))
        {
            return HookResult.Continue;
        }
        ToggleNoclip(player);
        return HookResult.Handled;
    }

    public void OnSelected(HudMenuSelected e)
    {
        if (e.MenuId != SpawnEditorMenu.MenuId || !_state.IsEditing(e.Player.Slot))
        {
            return;
        }
        var player = Utilities.GetPlayerFromSlot(e.Player.Slot);
        if (player is not { IsValid: true })
        {
            return;
        }
        Handle(player, e.ItemId);
        if (_state.IsEditing(player.Slot))
        {
            OpenMenu(player, refreshOnly: true);
        }
    }

    public void Tick()
    {
        if (!_state.IsActive || ++_tick % NearestRefreshTicks != 0)
        {
            return;
        }
        foreach (var slot in _state.Editors)
        {
            var player = Utilities.GetPlayerFromSlot(slot);
            if (player is not { IsValid: true })
            {
                continue;
            }
            var nearest = NearestTo(player);
            if (_entitiesAllowed)
            {
                _markers.ShowRing(slot, nearest);
            }
            if (_nearest.GetValueOrDefault(slot) != nearest?.Id)
            {
                _nearest[slot] = nearest?.Id;
                OpenMenu(player, refreshOnly: true);
            }
        }
    }

    public void OnCheckTransmit(CCheckTransmitInfoList infoList)
    {
        if (!_state.IsActive)
        {
            return;
        }
        foreach ((CCheckTransmitInfo info, CCSPlayerController? viewer) in infoList)
        {
            if (viewer is not null)
            {
                _markers.Hide(info, viewer.Slot, _state.IsEditing(viewer.Slot));
            }
        }
    }

    public void SuspendEntities()
    {
        _entitiesAllowed = false;
        _markers.Clear();
        _nearest.Clear();
    }

    // After a round cleanup (the editor's own mp_warmup_start restarts the round): markers and noclip come back.
    public void ResumeEntities()
    {
        _entitiesAllowed = true;
        RefreshMarkers();
        foreach (var slot in _state.Noclip)
        {
            if (Utilities.GetPlayerFromSlot(slot) is { IsValid: true } player)
            {
                SetNoclip(player, true);
            }
        }
    }

    // New map: entities are gone and the previous map's pause no longer applies.
    public void Reset()
    {
        var wasActive = _state.IsActive;
        _markers.Clear();
        _nearest.Clear();
        _state = EditorState.Idle;
        _entitiesAllowed = true;
        if (wasActive)
        {
            _context.Bus.Publish(new SpawnEditorStateChanged(false));
        }
    }

    public void OnDisconnect(int slot)
    {
        if (!_state.IsEditing(slot))
        {
            return;
        }
        if (_state.Editors.Count == 1 && _catalog.Set.Dirty)
        {
            _context.Logger.LogWarning("Last spawn editor left with unsaved changes: reloading the spawn file of {Map}", _catalog.MapName);
            _catalog.Reload();
        }
        Leave(null, slot);
    }

    public void Shutdown()
    {
        foreach (var slot in _state.Editors.ToList())
        {
            Leave(Utilities.GetPlayerFromSlot(slot), slot);
        }
        _markers.Clear();
    }

    private void Handle(CCSPlayerController player, string itemId)
    {
        switch (itemId)
        {
            case SpawnEditorMenu.SaveId:
                Save(player);
                return;
            case SpawnEditorMenu.ReloadId:
                Reload(player);
                return;
            case SpawnEditorMenu.NoclipId:
                ToggleNoclip(player);
                return;
            case SpawnEditorMenu.ExitId:
                HandleEditCommand(player, "exit");
                return;
            case SpawnEditorMenu.ExitSaveId:
                HandleEditCommand(player, "save");
                return;
            case SpawnEditorMenu.ExitDiscardId:
                Discard(player);
                return;
        }
        if (SpawnEditorMenu.ParseNearest(itemId) is { } nearestEdit)
        {
            EditSpawn(player, nearestEdit);
        }
        else if (SpawnEditorMenu.ParseAdd(itemId) is { } add)
        {
            AddHere(player, add);
        }
        else if (SpawnEditorMenu.ParseTeleport(itemId) is { } id && _catalog.Set.Spawns.FirstOrDefault(s => s.Id == id) is { } spawn)
        {
            TeleportTo(player, spawn);
        }
    }

    private void Discard(CCSPlayerController player)
    {
        _catalog.Reload();
        RefreshMarkers();
        Leave(player, player.Slot);
    }

    private void EditSpawn(CCSPlayerController player, NearestEdit edit)
    {
        if (_catalog.Set.Spawns.FirstOrDefault(s => s.Id == edit.Spawn) is not { } spawn)
        {
            Notify(player, "spawns.editor.none_nearby");
            return;
        }
        var label = _catalog.Set.Label(spawn);
        if (edit.Action == NearestAction.Delete)
        {
            Apply(_catalog.Set.Remove(spawn.Id));
            Notify(player, "spawns.editor.deleted", label);
            return;
        }
        var change = edit.Action switch
        {
            NearestAction.Team => new SpawnChange(Team: spawn.Team == TeamSide.T ? TeamSide.CT : TeamSide.T),
            NearestAction.Site => new SpawnChange(Site: spawn.Site == BombSite.A ? BombSite.B : BombSite.A),
            _ => new SpawnChange(CanPlant: !spawn.CanPlant),
        };
        Apply(_catalog.Set.Update(spawn.Id, change));
        var updated = _catalog.Set.Spawns.First(s => s.Id == spawn.Id);
        Notify(player, "spawns.editor.updated", _catalog.Set.Label(updated));
    }

    private void TeleportTo(CCSPlayerController player, SpawnPoint spawn)
    {
        var p = spawn.Position;
        player.PlayerPawn.Value?.Teleport(
            new Vector(p.X, p.Y, p.Z), new QAngle(spawn.Angle.Pitch, spawn.Angle.Yaw, spawn.Angle.Roll), new Vector(0f, 0f, 0f));
        Notify(player, "spawns.editor.teleported", _catalog.Set.Label(spawn));
    }

    private void ToggleNoclip(CCSPlayerController player)
    {
        var (state, enabled) = _state.ToggleNoclip(player.Slot);
        _state = state;
        SetNoclip(player, enabled);
        Notify(player, enabled ? "spawns.editor.noclip_on" : "spawns.editor.noclip_off");
    }

    private void Apply(SpawnSet set)
    {
        _catalog.Replace(set);
        RefreshMarkers();
        RefreshMenus();
    }

    private void RefreshMarkers()
    {
        _markers.Clear();
        _nearest.Clear();
        if (_state.IsActive && _entitiesAllowed)
        {
            _markers.Rebuild(_catalog.Set);
        }
    }

    private void RefreshMenus()
    {
        foreach (var slot in _state.Editors)
        {
            if (Utilities.GetPlayerFromSlot(slot) is { IsValid: true } player)
            {
                OpenMenu(player, refreshOnly: true);
            }
        }
    }

    private void OpenMenu(CCSPlayerController player, bool refreshOnly)
    {
        var view = new SpawnEditorView(_catalog.Set, NearestTo(player), _state.Noclip.Contains(player.Slot));
        _context.Bus.Publish(new HudMenuOpen(new PlayerId(player.Slot), SpawnEditorMenu.Build(view), refreshOnly));
    }

    private SpawnPoint? NearestTo(CCSPlayerController player) =>
        Pose(player) is { } pose ? _catalog.Set.Nearest(pose.Origin, NearestDistance) : null;

    // Spawns keep the editor's feet position and only the yaw of his view.
    private static (Vec3 Origin, ViewAngles Angles)? Pose(CCSPlayerController player)
    {
        if (player.PlayerPawn.Value is not { IsValid: true, AbsOrigin: { } origin } pawn)
        {
            return null;
        }
        return (new Vec3(origin.X, origin.Y, origin.Z), new ViewAngles(0f, pawn.EyeAngles.Y));
    }

    private static void SetNoclip(CCSPlayerController player, bool enabled)
    {
        if (player.PlayerPawn.Value is not { IsValid: true } pawn)
        {
            return;
        }
        var moveType = enabled ? MoveType_t.MOVETYPE_NOCLIP : MoveType_t.MOVETYPE_WALK;
        pawn.MoveType = moveType;
        pawn.ActualMoveType = moveType;
        Utilities.SetStateChanged(pawn, "CBaseEntity", "m_MoveType");
    }

    private void Notify(CCSPlayerController? player, string key, params object[] args)
    {
        if (player is { IsValid: true })
        {
            _context.Text.Chat(player, key, args);
            return;
        }
        Server.PrintToConsole(_context.Text.Server(key, args));
    }
}
