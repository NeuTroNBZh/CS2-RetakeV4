using System.Drawing;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;
using RetakeV4.Domain.Geometry;
using RetakeV4.Domain.Hud;

namespace HudProbe;

[MinimumApiVersion(370)]
public sealed class HudProbePlugin : BasePlugin
{
    public override string ModuleName => "HudProbe (RetakeV4 spike - throwaway)";
    public override string ModuleVersion => "0.0.1";

    private static readonly string[] Labels = { "AK-47", "M4A1-S", "AWP : ON", "Deagle", "Grenades", "Fermer" };
    private static readonly Color Accent = Color.FromArgb(255, 79, 195, 247);

    private readonly Dictionary<int, ProbeMenu> _menus = new();
    private AimMenuLayout _layout = new(Labels.Length, 60f, 4f, 8f, 18f);
    private int _orientMode;
    private bool _parentMode;

    private sealed class ProbeMenu
    {
        public required CCSPlayerController Owner { get; init; }
        public required ViewAngles Opened { get; init; }
        public List<CPointWorldText> Lines { get; } = new();
        public int Cursor { get; set; }
        public int? Aimed { get; set; }
    }

    public override void Load(bool hotReload)
    {
        RegisterListener<Listeners.OnTick>(OnTick);
        RegisterListener<Listeners.CheckTransmit>(OnCheckTransmit);
        RegisterListener<Listeners.OnPlayerButtonsChanged>(OnButtons);
        for (var i = 1; i <= 9; i++)
        {
            var slot = i;
            AddCommandListener($"slot{slot}", (player, _) => OnSlot(player, slot), HookMode.Pre);
        }
    }

    [GameEventHandler]
    public HookResult OnRoundPrestart(EventRoundPrestart @event, GameEventInfo info)
    {
        foreach (var menu in _menus.Values.ToList())
        {
            Close(menu.Owner);
        }
        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnDisconnect(EventPlayerDisconnect @event, GameEventInfo info)
    {
        if (@event.Userid is { } player)
        {
            Close(player);
        }
        return HookResult.Continue;
    }

    [ConsoleCommand("css_hudprobe", "Open/close the probe menu")]
    public void OnProbe(CCSPlayerController? player, CommandInfo command)
    {
        if (player is null || !player.IsValid)
        {
            return;
        }
        if (_menus.ContainsKey(player.Slot))
        {
            Close(player);
            return;
        }
        Open(player);
    }

    [ConsoleCommand("css_hudprobe_orient", "0|1|2 : text facing formula")]
    public void OnOrient(CCSPlayerController? player, CommandInfo command)
    {
        _orientMode = int.TryParse(command.GetArg(1), out var mode) ? Math.Clamp(mode, 0, 2) : 0;
        command.ReplyToCommand($"[HudProbe] orient mode = {_orientMode}");
    }

    [ConsoleCommand("css_hudprobe_dist", "<units> : menu distance")]
    public void OnDistance(CCSPlayerController? player, CommandInfo command)
    {
        if (float.TryParse(command.GetArg(1), out var distance) && distance > 5f)
        {
            _layout = new AimMenuLayout(_layout.LineCount, distance, _layout.LineHeightUnits, _layout.FirstLineUpUnits, _layout.HalfWidthUnits);
        }
        command.ReplyToCommand($"[HudProbe] distance = {_layout.DistanceUnits}");
    }

    [ConsoleCommand("css_hudprobe_parent", "Toggle SetParent-to-pawn mode (applies to next open)")]
    public void OnParent(CCSPlayerController? player, CommandInfo command)
    {
        _parentMode = !_parentMode;
        command.ReplyToCommand($"[HudProbe] parent mode = {_parentMode}");
    }

    private void Open(CCSPlayerController player)
    {
        var pawn = player.PlayerPawn.Value;
        if (pawn is null || !pawn.IsValid)
        {
            return;
        }
        var menu = new ProbeMenu { Owner = player, Opened = new ViewAngles(pawn.EyeAngles.X, pawn.EyeAngles.Y) };
        foreach (var label in Labels)
        {
            var line = CreateLine(label);
            if (line is null)
            {
                DestroyLines(menu);
                player.PrintToChat(" [HudProbe] point_worldtext creation failed");
                return;
            }
            menu.Lines.Add(line);
        }
        _menus[player.Slot] = menu;
        Position(menu, pawn);
        if (_parentMode)
        {
            menu.Lines.ForEach(line => line.AcceptInput("SetParent", pawn, pawn, "!activator", 0));
        }
    }

    private static CPointWorldText? CreateLine(string text)
    {
        var line = Utilities.CreateEntityByName<CPointWorldText>("point_worldtext");
        if (line is null || !line.IsValid)
        {
            return null;
        }
        line.MessageText = text;
        line.Enabled = true;
        line.FontSize = 24f;
        line.WorldUnitsPerPx = 0.1f;
        line.Fullbright = true;
        line.Color = Color.White;
        line.JustifyHorizontal = PointWorldTextJustifyHorizontal_t.POINT_WORLD_TEXT_JUSTIFY_HORIZONTAL_CENTER;
        line.JustifyVertical = PointWorldTextJustifyVertical_t.POINT_WORLD_TEXT_JUSTIFY_VERTICAL_CENTER;
        line.ReorientMode = PointWorldTextReorientMode_t.POINT_WORLD_TEXT_REORIENT_NONE;
        line.DispatchSpawn();
        return line;
    }

    private void OnTick()
    {
        foreach (var menu in _menus.Values.ToList())
        {
            var pawn = menu.Owner.IsValid ? menu.Owner.PlayerPawn.Value : null;
            if (pawn is null || !pawn.IsValid)
            {
                Close(menu.Owner);
                continue;
            }
            if (!_parentMode)
            {
                Position(menu, pawn);
            }
            UpdateSelection(menu, pawn);
        }
    }

    private void Position(ProbeMenu menu, CCSPlayerPawn pawn)
    {
        if (pawn.AbsOrigin is null)
        {
            return;
        }
        var eye = new Vec3(pawn.AbsOrigin.X, pawn.AbsOrigin.Y, pawn.AbsOrigin.Z + pawn.ViewOffset.Z);
        var facing = FacingAngles(menu.Opened);
        for (var i = 0; i < menu.Lines.Count; i++)
        {
            var p = AimMenuGeometry.LinePosition(_layout, eye, menu.Opened, i);
            menu.Lines[i].Teleport(new Vector(p.X, p.Y, p.Z), facing, new Vector(0f, 0f, 0f));
        }
    }

    private QAngle FacingAngles(ViewAngles opened) => _orientMode switch
    {
        1 => new QAngle(0f, opened.Yaw - 90f, 90f),
        2 => new QAngle(opened.Pitch, opened.Yaw + 180f, 0f),
        _ => new QAngle(0f, opened.Yaw + 270f, 90f - opened.Pitch),
    };

    private void UpdateSelection(ProbeMenu menu, CCSPlayerPawn pawn)
    {
        var current = new ViewAngles(pawn.EyeAngles.X, pawn.EyeAngles.Y);
        menu.Aimed = AimLineResolver.Resolve(_layout, menu.Opened, current);
        var highlighted = menu.Aimed ?? menu.Cursor;
        for (var i = 0; i < menu.Lines.Count; i++)
        {
            var color = i == highlighted ? Accent : Color.White;
            if (menu.Lines[i].Color != color)
            {
                menu.Lines[i].Color = color;
                Utilities.SetStateChanged(menu.Lines[i], "CPointWorldText", "m_Color");
            }
        }
        if (menu.Aimed is not null)
        {
            BlockAttack(pawn);
        }
    }

    private static void BlockAttack(CCSPlayerPawn pawn)
    {
        var weapon = pawn.WeaponServices?.ActiveWeapon.Value;
        if (weapon is null || !weapon.IsValid)
        {
            return;
        }
        weapon.NextPrimaryAttackTick = Server.TickCount + 2;
        weapon.NextSecondaryAttackTick = Server.TickCount + 2;
        Utilities.SetStateChanged(weapon, "CBasePlayerWeapon", "m_nNextPrimaryAttackTick");
        Utilities.SetStateChanged(weapon, "CBasePlayerWeapon", "m_nNextSecondaryAttackTick");
    }

    private void OnButtons(CCSPlayerController player, PlayerButtons pressed, PlayerButtons released)
    {
        if (!_menus.TryGetValue(player.Slot, out var menu))
        {
            return;
        }
        if ((pressed & PlayerButtons.Attack) != 0 && menu.Aimed is { } aimed)
        {
            Select(menu, aimed, "aim+click");
        }
        else if ((pressed & PlayerButtons.Forward) != 0)
        {
            menu.Cursor = Math.Max(0, menu.Cursor - 1);
        }
        else if ((pressed & PlayerButtons.Back) != 0)
        {
            menu.Cursor = Math.Min(Labels.Length - 1, menu.Cursor + 1);
        }
        else if ((pressed & PlayerButtons.Use) != 0)
        {
            Select(menu, menu.Cursor, "E");
        }
    }

    private HookResult OnSlot(CCSPlayerController? player, int slot)
    {
        if (player is null || !_menus.TryGetValue(player.Slot, out var menu))
        {
            return HookResult.Continue;
        }
        if (slot <= Labels.Length)
        {
            Select(menu, slot - 1, $"key {slot}");
        }
        return HookResult.Handled;
    }

    private void Select(ProbeMenu menu, int index, string how)
    {
        menu.Owner.PrintToChat($" [HudProbe] selected '{Labels[index]}' via {how}");
        Logger.LogInformation("HudProbe: {Player} selected {Label} via {How}", menu.Owner.PlayerName, Labels[index], how);
        if (Labels[index] == "Fermer")
        {
            Close(menu.Owner);
        }
    }

    private void OnCheckTransmit(CCheckTransmitInfoList infoList)
    {
        if (_menus.Count == 0)
        {
            return;
        }
        foreach ((CCheckTransmitInfo info, CCSPlayerController? viewer) in infoList)
        {
            if (viewer is null)
            {
                continue;
            }
            foreach (var menu in _menus.Values.Where(m => m.Owner.Slot != viewer.Slot))
            {
                menu.Lines.Where(line => line.IsValid).ToList().ForEach(line => info.TransmitEntities.Remove(line));
            }
        }
    }

    private void Close(CCSPlayerController player)
    {
        if (_menus.Remove(player.Slot, out var menu))
        {
            DestroyLines(menu);
        }
    }

    private static void DestroyLines(ProbeMenu menu)
    {
        menu.Lines.Where(line => line.IsValid).ToList().ForEach(line => line.Remove());
        menu.Lines.Clear();
    }
}
