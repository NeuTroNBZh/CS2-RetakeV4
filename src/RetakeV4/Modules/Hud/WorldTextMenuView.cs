using System.Drawing;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using RetakeV4.Adapters;
using RetakeV4.Domain.Geometry;
using RetakeV4.Domain.Hud;

namespace RetakeV4.Modules.Hud;

internal sealed record WorldTextLine(string Text, Color Color);

// Entity 0 is the title, one line above the first menu line. Entities are reused when the menu changes.
internal sealed class WorldTextMenuView
{
    private readonly HudConfig _config;
    private readonly List<CPointWorldText> _entities = new();
    private readonly List<WorldTextLine> _shown = new();
    private readonly HashSet<uint> _parented = new();

    public WorldTextMenuView(HudConfig config) => _config = config;

    public IReadOnlyList<CPointWorldText> Entities => _entities;

    public bool Show(IReadOnlyList<WorldTextLine> lines)
    {
        if (_entities.Any(e => !e.IsValid))
        {
            Destroy();
        }
        for (var i = 0; i < lines.Count; i++)
        {
            if (i < _entities.Count)
            {
                Update(i, lines[i]);
                continue;
            }
            var entity = Create(lines[i]);
            if (entity is null)
            {
                return false;
            }
            _entities.Add(entity);
            _shown.Add(lines[i]);
        }
        while (_entities.Count > lines.Count)
        {
            RemoveLast();
        }
        return true;
    }

    public void Position(PlayerViewPoint view, ViewAngles opened, AimMenuLayout layout)
    {
        var facing = Facing(opened);
        for (var i = 0; i < _entities.Count; i++)
        {
            var p = AimMenuGeometry.LinePosition(layout, view.Eye, opened, i - 1);
            _entities[i].Teleport(new Vector(p.X, p.Y, p.Z), facing, new Vector(0f, 0f, 0f));
        }
        if (_config.Menu.FollowMode == MenuFollowMode.Parent)
        {
            Parent(view.Pawn);
        }
    }

    public void Destroy()
    {
        foreach (var entity in _entities.Where(e => e.IsValid))
        {
            entity.Remove();
        }
        _entities.Clear();
        _shown.Clear();
        _parented.Clear();
    }

    private void Update(int index, WorldTextLine line)
    {
        var entity = _entities[index];
        var shown = _shown[index];
        if (shown.Text != line.Text)
        {
            entity.AcceptInput("SetMessage", entity, entity, line.Text, 0);
        }
        if (shown.Color != line.Color)
        {
            entity.Color = line.Color;
            Utilities.SetStateChanged(entity, "CPointWorldText", "m_Color");
        }
        _shown[index] = line;
    }

    private CPointWorldText? Create(WorldTextLine line)
    {
        var entity = Utilities.CreateEntityByName<CPointWorldText>("point_worldtext");
        if (entity is not { IsValid: true })
        {
            return null;
        }
        entity.MessageText = line.Text;
        entity.Enabled = true;
        entity.FontSize = _config.Theme.FontSize;
        entity.WorldUnitsPerPx = _config.Menu.WorldUnitsPerPx;
        entity.Fullbright = true;
        entity.Color = line.Color;
        entity.JustifyHorizontal = PointWorldTextJustifyHorizontal_t.POINT_WORLD_TEXT_JUSTIFY_HORIZONTAL_CENTER;
        entity.JustifyVertical = PointWorldTextJustifyVertical_t.POINT_WORLD_TEXT_JUSTIFY_VERTICAL_CENTER;
        entity.ReorientMode = PointWorldTextReorientMode_t.POINT_WORLD_TEXT_REORIENT_NONE;
        entity.DispatchSpawn();
        return entity;
    }

    private void RemoveLast()
    {
        var last = _entities[^1];
        _parented.Remove(last.Index);
        if (last.IsValid)
        {
            last.Remove();
        }
        _entities.RemoveAt(_entities.Count - 1);
        _shown.RemoveAt(_shown.Count - 1);
    }

    private void Parent(CBasePlayerPawn pawn)
    {
        foreach (var entity in _entities)
        {
            if (_parented.Add(entity.Index))
            {
                entity.AcceptInput("SetParent", pawn, pawn, "!activator", 0);
            }
        }
    }

    // The three formulas of the HUD prototype (css_hudprobe_orient 0|1|2).
    private QAngle Facing(ViewAngles opened) => _config.Menu.Orientation switch
    {
        1 => new QAngle(0f, opened.Yaw - 90f, 90f),
        2 => new QAngle(opened.Pitch, opened.Yaw + 180f, 0f),
        _ => new QAngle(0f, opened.Yaw + 270f, 90f - opened.Pitch),
    };
}
