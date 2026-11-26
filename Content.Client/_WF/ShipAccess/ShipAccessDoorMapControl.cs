using System.Numerics;
using Content.Client._WF.Shuttles.UI;
using Content.Shared._WF.ShipAccess;
using Content.Shared.Doors.Components;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input;

namespace Content.Client._WF.ShipAccess;

/// <summary>
/// The access tab's door diagram: the hull from the ship view with every door the client can see drawn as a
/// node in its rule's colour. Unlike the nav map it fills whatever the tab gives it, and a left click on a
/// node selects that door. Firelocks are left out; they answer to the atmosphere, not the owner.
/// </summary>
public sealed class ShipAccessDoorMapControl : ShipViewControl
{
    /// <summary>Pixels past a node's edge that still count as a click on it.</summary>
    private const float SelectSlack = 6f;

    private static readonly Color SelectedRing = Color.White;
    private static readonly Color HoverRing = Color.FromHex("#a9bcc7");

    /// <summary>Node colours by rule, in enum order.</summary>
    private static readonly Color[] RuleColors =
    {
        Color.FromHex("#8fb58f"),
        Color.FromHex("#e8c04a"),
        Color.FromHex("#3fa0ff"),
        Color.FromHex("#b86bff"),
        Color.FromHex("#3fd6c8"),
        Color.FromHex("#40ff40"),
        Color.FromHex("#ff3030"),
    };

    private readonly List<ShipAccessDoorNode> _doors = new();
    private EntityUid? _grid;
    private EntityUid? _hovered;

    /// <summary>Where the left button went down, so a drag that ends on a node doesn't pick it.</summary>
    private Vector2? _pressPosition;

    /// <summary>Doors on the grid the client knows about, top row first. Doors outside the client's view are missing.</summary>
    public IReadOnlyList<ShipAccessDoorNode> Doors => _doors;

    /// <summary>The door drawn with a ring, or null.</summary>
    public EntityUid? Selected { get; set; }

    /// <summary>The viewer clicked a door node.</summary>
    public event Action<EntityUid>? DoorSelected;

    public ShipAccessDoorMapControl()
    {
        // The nav map fixes itself to a square; here the layout sets the size and the drawing follows it.
        SetSize = new Vector2(float.NaN, float.NaN);
        HideNavMapPanel();
        PostWallDrawingAction += DrawDoors;
    }

    /// <summary>Drawing centres on the control rather than on the nav map's fixed square.</summary>
    protected override Vector2 MidPointVector => new Vector2(PixelWidth, PixelHeight) / 2f;

    /// <summary>The hull is fitted to the shorter side.</summary>
    protected override int ScaledMinimapRadius => (int) (MathF.Min(PixelWidth, PixelHeight) / 2f - MinimapMargin * UIScale);

    /// <summary>The colour a rule is drawn in, on the diagram and in the legend.</summary>
    public static Color ColorFor(WFDoorAccessRule rule)
    {
        var i = (int) rule;
        return i >= 0 && i < RuleColors.Length ? RuleColors[i] : RuleColors[0];
    }

    public override void SetGrid(EntityUid? grid)
    {
        if (_grid == grid)
            return;

        _grid = grid;
        Selected = null;
        _hovered = null;
        _doors.Clear();
        base.SetGrid(grid);
    }

    /// <summary>Re-reads the doors on the grid. The screen calls this on its own refresh tick.</summary>
    public void RefreshDoors()
    {
        _doors.Clear();
        if (_grid == null)
            return;

        var query = EntManager.EntityQueryEnumerator<DoorComponent, TransformComponent, MetaDataComponent>();
        while (query.MoveNext(out var uid, out _, out var xform, out var meta))
        {
            // The node sits at the door's grid-local position, so only doors parented straight to the grid count.
            if (xform.ParentUid != _grid || EntManager.HasComponent<FirelockComponent>(uid))
                continue;

            var rule = EntManager.TryGetComponent<WFDoorAccessRuleComponent>(uid, out var comp) ? comp.Rule : WFDoorAccessRule.Default;
            _doors.Add(new ShipAccessDoorNode(uid, meta.EntityName, xform.LocalPosition, rule, comp?.HasOwnCode ?? false));
        }

        _doors.Sort((a, b) =>
        {
            var y = b.Position.Y.CompareTo(a.Position.Y);
            return y != 0 ? y : a.Position.X.CompareTo(b.Position.X);
        });

        if (Selected != null && !Contains(Selected.Value))
            Selected = null;

        if (_hovered != null && !Contains(_hovered.Value))
            _hovered = null;
    }

    private bool Contains(EntityUid uid)
    {
        foreach (var door in _doors)
        {
            if (door.Uid == uid)
                return true;
        }

        return false;
    }

    /// <summary>The nav map's zoom readout, beacon toggle and recentre button mean nothing on a door diagram.</summary>
    private void HideNavMapPanel()
    {
        foreach (var child in Children)
        {
            if (child is not BoxContainer column)
                continue;

            foreach (var row in column.Children)
            {
                if (row is PanelContainer panel)
                    panel.Visible = false;
            }
        }
    }

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        base.KeyBindDown(args);

        if (args.Function == EngineKeyFunctions.UIClick)
            _pressPosition = args.RelativePixelPosition;
    }

    protected override void KeyBindUp(GUIBoundKeyEventArgs args)
    {
        base.KeyBindUp(args);

        if (args.Function != EngineKeyFunctions.UIClick)
            return;

        var pressed = _pressPosition;
        _pressPosition = null;
        if (pressed == null || (pressed.Value - args.RelativePixelPosition).Length() > MinDragDistance)
            return;

        if (DoorAt(args.RelativePixelPosition) is not { } door)
            return;

        Selected = door.Uid;
        DoorSelected?.Invoke(door.Uid);
        args.Handle();
    }

    protected override void MouseMove(GUIMouseMoveEventArgs args)
    {
        base.MouseMove(args);
        _hovered = DoorAt(args.RelativePixelPosition)?.Uid;
    }

    protected override void MouseExited()
    {
        base.MouseExited();
        _hovered = null;
    }

    /// <summary>The door whose node is under a point on the control, the nearest when nodes overlap.</summary>
    private ShipAccessDoorNode? DoorAt(Vector2 pixel)
    {
        if (_doors.Count == 0)
            return null;

        var offset = GetOffset();
        var reach = NodeRadius + SelectSlack;
        ShipAccessDoorNode? closest = null;
        var closestDistance = float.PositiveInfinity;
        foreach (var door in _doors)
        {
            var distance = (NodePosition(door, offset) - pixel).Length();
            if (distance > reach || distance >= closestDistance)
                continue;

            closest = door;
            closestDistance = distance;
        }

        return closest;
    }

    private float NodeRadius => MathF.Max(3f, MinimapScale * 0.35f);

    private Vector2 NodePosition(ShipAccessDoorNode door, Vector2 offset)
    {
        var p = door.Position - offset;
        return ScalePosition(new Vector2(p.X, -p.Y));
    }

    private void DrawDoors(DrawingHandleScreen handle)
    {
        if (_doors.Count == 0)
            return;

        var offset = GetOffset();
        var radius = NodeRadius;

        foreach (var door in _doors)
        {
            var pos = NodePosition(door, offset);
            if (door.Uid == Selected)
                handle.DrawCircle(pos, radius + 3f, SelectedRing);
            else if (door.Uid == _hovered)
                handle.DrawCircle(pos, radius + 2f, HoverRing);

            handle.DrawCircle(pos, radius, ColorFor(door.Rule));
        }
    }
}

/// <summary>One door as the diagram last saw it.</summary>
public sealed record ShipAccessDoorNode(EntityUid Uid, string Name, Vector2 Position, WFDoorAccessRule Rule, bool HasOwnCode);
