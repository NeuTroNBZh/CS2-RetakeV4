using System.Drawing;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using RetakeV4.Domain.Common;
using RetakeV4.Domain.Spawns;
using SpawnPoint = RetakeV4.Domain.Spawns.SpawnPoint;

namespace RetakeV4.Modules.Spawns;

// Editor-only markers: one pillar and one label per spawn (site B pillars are translucent), plus a ring around each editor's nearest spawn.
internal sealed class SpawnMarkers
{
    private const float PillarHeight = 72f;
    private const float LabelOffset = 20f;
    private const float RingRadius = 30f;
    private const float RingHeight = 5f;
    private const int RingSegments = 8;

    private static readonly Color TColor = Color.FromArgb(255, 255, 60, 60);
    private static readonly Color CtColor = Color.FromArgb(255, 60, 130, 255);
    private static readonly Color RingColor = Color.FromArgb(255, 255, 255, 0);

    private readonly List<CBaseEntity> _shared = new();
    private readonly Dictionary<int, (Guid Spawn, List<CBeam> Beams)> _rings = new();

    public void Rebuild(SpawnSet set)
    {
        Remove(_shared);
        foreach (var spawn in set.Spawns)
        {
            AddIfCreated(Pillar(spawn));
            AddIfCreated(Label(set, spawn));
        }
    }

    public void ShowRing(int slot, SpawnPoint? nearest)
    {
        if (_rings.TryGetValue(slot, out var ring) && ring.Spawn == nearest?.Id)
        {
            return;
        }
        ClearRing(slot);
        if (nearest is null)
        {
            return;
        }
        var beams = Enumerable.Range(0, RingSegments).Select(i => RingSegment(nearest, i)).OfType<CBeam>().ToList();
        _rings[slot] = (nearest.Id, beams);
    }

    public void ClearRing(int slot)
    {
        if (_rings.Remove(slot, out var ring))
        {
            Remove(ring.Beams);
        }
    }

    public void Clear()
    {
        Remove(_shared);
        foreach (var slot in _rings.Keys.ToList())
        {
            ClearRing(slot);
        }
    }

    // Players outside the editor see nothing; an editor only sees his own ring.
    public void Hide(CCheckTransmitInfo info, int viewerSlot, bool viewerIsEditor)
    {
        if (!viewerIsEditor)
        {
            Hide(info, _shared);
        }
        foreach (var (slot, ring) in _rings)
        {
            if (!viewerIsEditor || slot != viewerSlot)
            {
                Hide(info, ring.Beams);
            }
        }
    }

    private static void Hide(CCheckTransmitInfo info, IEnumerable<CBaseEntity> entities)
    {
        foreach (var entity in entities.Where(e => e.IsValid))
        {
            info.TransmitEntities.Remove(entity);
        }
    }

    private void AddIfCreated(CBaseEntity? entity)
    {
        if (entity is not null)
        {
            _shared.Add(entity);
        }
    }

    private static CBeam? Pillar(SpawnPoint spawn)
    {
        var color = ColorOf(spawn.Team);
        var p = spawn.Position;
        return Beam(
            new Vector(p.X, p.Y, p.Z),
            new Vector(p.X, p.Y, p.Z + PillarHeight),
            spawn.Site == BombSite.B ? Color.FromArgb(120, color.R, color.G, color.B) : color,
            3f);
    }

    private static CPointWorldText? Label(SpawnSet set, SpawnPoint spawn)
    {
        var text = Utilities.CreateEntityByName<CPointWorldText>("point_worldtext");
        if (text is not { IsValid: true })
        {
            return null;
        }
        text.MessageText = set.Label(spawn);
        text.Enabled = true;
        text.FontSize = 80f;
        text.WorldUnitsPerPx = 0.25f;
        text.Fullbright = true;
        text.Color = ColorOf(spawn.Team);
        text.JustifyHorizontal = PointWorldTextJustifyHorizontal_t.POINT_WORLD_TEXT_JUSTIFY_HORIZONTAL_CENTER;
        text.JustifyVertical = PointWorldTextJustifyVertical_t.POINT_WORLD_TEXT_JUSTIFY_VERTICAL_CENTER;
        text.ReorientMode = PointWorldTextReorientMode_t.POINT_WORLD_TEXT_REORIENT_AROUND_UP;
        var p = spawn.Position;
        text.Teleport(new Vector(p.X, p.Y, p.Z + PillarHeight + LabelOffset), new QAngle(0f, 0f, 0f), new Vector(0f, 0f, 0f));
        text.DispatchSpawn();
        return text;
    }

    private static CBeam? RingSegment(SpawnPoint spawn, int index)
    {
        var from = 2 * Math.PI * index / RingSegments;
        var to = 2 * Math.PI * (index + 1) / RingSegments;
        var p = spawn.Position;
        return Beam(
            new Vector(p.X + RingRadius * (float)Math.Cos(from), p.Y + RingRadius * (float)Math.Sin(from), p.Z + RingHeight),
            new Vector(p.X + RingRadius * (float)Math.Cos(to), p.Y + RingRadius * (float)Math.Sin(to), p.Z + RingHeight),
            RingColor,
            2f);
    }

    private static CBeam? Beam(Vector start, Vector end, Color color, float width)
    {
        var beam = Utilities.CreateEntityByName<CBeam>("beam");
        if (beam is not { IsValid: true })
        {
            return null;
        }
        beam.Render = color;
        beam.Width = width;
        beam.EndWidth = width;
        beam.Amplitude = 0f;
        beam.Speed = 0f;
        beam.Teleport(start, new QAngle(0f, 0f, 0f), new Vector(0f, 0f, 0f));
        beam.EndPos.X = end.X;
        beam.EndPos.Y = end.Y;
        beam.EndPos.Z = end.Z;
        beam.DispatchSpawn();
        return beam;
    }

    private static Color ColorOf(TeamSide team) => team == TeamSide.T ? TColor : CtColor;

    private static void Remove<T>(List<T> entities) where T : CBaseEntity
    {
        foreach (var entity in entities.Where(e => e.IsValid))
        {
            entity.Remove();
        }
        entities.Clear();
    }
}
