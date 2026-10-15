using System.Linq;
using System.Numerics;
using System.Text;
using Content.Shared._Forge.KIAS.Controllers;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Shared.Input;

namespace Content.Client._Forge.KIAS.Controllers;

public sealed class KiasGraphCanvas : Control
{
    public event Action<KiasControllerEditMessage>? Edited;
    public event Action<KiasGraphNodeView?>? Selected;
    public event Action<string>? Feedback;
    private KiasControllerEditorState _state = new();
    private readonly Font _font;
    private Vector2 _pan = new(40, 40), _mouse, _previous, _dragOffset;
    private float _zoom = 1;
    private int? _drag, _selected;
    private bool _panning;
    private (int Node, KiasGraphPort Port)? _wire;
    public bool CanEdit;
    private const float Width = 260, Header = 46, Row = 24;

    public KiasGraphCanvas()
    {
        RectClipContent = true;
        MouseFilter = MouseFilterMode.Stop;
        HorizontalExpand = VerticalExpand = true;
        MinSize = new Vector2(260, 240);
        _font = new VectorFont(IoCManager.Resolve<IResourceCache>().GetResource<FontResource>("/EngineFonts/NotoSans/NotoSans-Regular.ttf"), 12);
    }

    public void SetState(KiasControllerEditorState state)
    {
        var changed = state.Revision != _state.Revision;
        _state = state;
        if (changed && _selected is { } id) Selected?.Invoke(state.Nodes.FirstOrDefault(node => node.Id == id));
    }
    public Vector2 GraphPosition(Vector2 pixel) => (pixel - _pan * UIScale) / (_zoom * UIScale);
    public Vector2 Center => GraphPosition(PixelSize / 2);
    private Vector2 Screen(Vector2 graph) => (graph * _zoom + _pan) * UIScale;
    private static Vector2 Position(KiasGraphNodeView node) => new(node.X, node.Y);
    private static float Height(KiasGraphNodeView node) => Header + Row * Math.Max(1, Math.Max(
        node.Ports.Count(port => port.Direction == KiasPortDirection.Input), node.Ports.Count(port => port.Direction == KiasPortDirection.Output))) + 8;
    private Vector2 PortPosition(KiasGraphNodeView node, KiasGraphPort port)
    {
        var index = 0;
        foreach (var other in node.Ports)
        {
            if (other.Id == port.Id) break;
            if (other.Direction == port.Direction) index++;
        }
        return Position(node) + new Vector2(port.Direction == KiasPortDirection.Input ? 0 : Width, Header + index * Row + 10);
    }
    private (KiasGraphNodeView Node, KiasGraphPort Port)? PortAt(Vector2 point)
    {
        (KiasGraphNodeView Node, KiasGraphPort Port)? nearest = null;
        var closest = float.PositiveInfinity;
        foreach (var node in _state.Nodes.AsEnumerable().Reverse())
            foreach (var port in node.Ports)
            {
                var distance = Vector2.DistanceSquared(point, PortPosition(node, port));
                if (distance <= 100 / (_zoom * _zoom) && distance < closest)
                {
                    closest = distance;
                    nearest = (node, port);
                }
            }
        return nearest;
    }
    private KiasGraphNodeView? NodeAt(Vector2 point) => _state.Nodes.LastOrDefault(node =>
        UIBox2.FromDimensions(Position(node), new Vector2(Width, Height(node))).Contains(point));
    private static Color TypeColor(KiasPortType type) => type switch
    {
        KiasPortType.Signal => Color.Gold, KiasPortType.Bool => Color.LimeGreen,
        KiasPortType.Number => Color.Cyan, KiasPortType.String => Color.LightPink,
        KiasPortType.Entity => Color.Orange, _ => Color.MediumPurple
    };
    private string Fit(string text, float width)
    {
        var result = new StringBuilder();
        var remaining = width * _zoom * UIScale;
        foreach (var rune in text.EnumerateRunes())
        {
            remaining -= _font.GetCharMetrics(rune, _zoom * UIScale)?.Advance ?? 0;
            if (remaining < 12 * _zoom * UIScale) return result + "…";
            result.Append(rune);
        }
        return result.ToString();
    }
    private static void Curve(DrawingHandleScreen handle, Vector2 start, Vector2 end, Color color)
    {
        var bend = Math.Max(45, Math.Abs(end.X - start.X) * .45f);
        var a = start + new Vector2(bend, 0); var b = end - new Vector2(bend, 0);
        var previous = start;
        for (var i = 1; i <= 24; i++)
        {
            var t = i / 24f; var u = 1 - t;
            var next = u * u * u * start + 3 * u * u * t * a + 3 * u * t * t * b + t * t * t * end;
            handle.DrawLine(previous, next, color); previous = next;
        }
    }
    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        handle.DrawRect(UIBox2.FromDimensions(Vector2.Zero, PixelSize), new Color(.055f, .065f, .085f));
        var byId = _state.Nodes.ToDictionary(node => node.Id);
        foreach (var wire in _state.Wires)
        {
            if (!byId.TryGetValue(wire.FromNode, out var from) || !byId.TryGetValue(wire.ToNode, out var to)) continue;
            var output = from.Ports.FirstOrDefault(port => port.Id == wire.FromPort);
            var input = to.Ports.FirstOrDefault(port => port.Id == wire.ToPort);
            if (output != null && input != null) Curve(handle, Screen(PortPosition(from, output)), Screen(PortPosition(to, input)), TypeColor(output.Type));
        }
        foreach (var node in _state.Nodes)
        {
            var top = Screen(Position(node));
            var rect = UIBox2.FromDimensions(top, new Vector2(Width, Height(node)) * (_zoom * UIScale));
            if (!rect.Intersects(UIBox2.FromDimensions(Vector2.Zero, PixelSize))) continue;
            handle.DrawRect(rect, _selected == node.Id ? new Color(.22f, .29f, .38f) : new Color(.12f, .15f, .20f));
            handle.DrawRect(rect, _selected == node.Id ? Color.Cyan : Color.Gray, false);
            var title = KiasGraphCatalog.External(node.Kind) ? $"{node.Kind.ToString().ToUpperInvariant()} {KiasControllerLabels.Profile(node.Profile, node.Ports)}" : Loc.GetString($"kias-controller-node-{node.Kind.ToString().ToLowerInvariant()}");
            title = Fit(title, Width - 16);
            handle.DrawString(_font, top + new Vector2(8, 18) * (_zoom * UIScale), title, _zoom * UIScale, Color.White);
            var detail = Fit(KiasControllerLabels.Summary(node), Width - 16);
            handle.DrawString(_font, top + new Vector2(8, 35) * (_zoom * UIScale), detail, _zoom * UIScale, Color.LightGray);
            foreach (var port in node.Ports)
            {
                var point = Screen(PortPosition(node, port));
                var color = TypeColor(port.Type);
                if (_wire is { } pending && KiasControllerLabels.Incompatibility(pending.Port, port) != null) color = Color.DimGray;
                handle.DrawCircle(point, 5 * UIScale, color);
                var label = Loc.TryGetString(port.Name, out var localized) ? localized : port.Id;
                label = Fit(label, 112);
                var textPoint = point + new Vector2(port.Direction == KiasPortDirection.Input ? 10 : -125, 4) * (_zoom * UIScale);
                handle.DrawString(_font, textPoint, label, _zoom * UIScale, color);
            }
        }
        if (_wire is { } drawing && byId.TryGetValue(drawing.Node, out var owner))
        {
            var point = Screen(PortPosition(owner, drawing.Port));
            Curve(handle, drawing.Port.Direction == KiasPortDirection.Output ? point : _mouse,
                drawing.Port.Direction == KiasPortDirection.Output ? _mouse : point, TypeColor(drawing.Port.Type));
        }
    }
    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        base.KeyBindDown(args);
        var point = GraphPosition(args.RelativePixelPosition);
        if (args.Function == EngineKeyFunctions.UIRightClick)
        {
            UserInterfaceManager.ControlFocused = this;
            _wire = null;
            if (CanEdit && PortAt(point) is { } hit)
            {
                var wire = _state.Wires.FirstOrDefault(wire => wire.FromNode == hit.Node.Id && wire.FromPort == hit.Port.Id
                    || wire.ToNode == hit.Node.Id && wire.ToPort == hit.Port.Id);
                if (wire != null) Edited?.Invoke(new() { Edit = KiasGraphEdit.Disconnect, Wire = wire.Copy() });
            }
            else { _panning = true; _previous = args.RelativePixelPosition; }
            args.Handle(); return;
        }
        if (args.Function != EngineKeyFunctions.UIClick) return;
        UserInterfaceManager.ControlFocused = this;
        if (CanEdit && PortAt(point) is { } endpoint)
        {
            _wire = (endpoint.Node.Id, endpoint.Port); _mouse = args.RelativePixelPosition;
        }
        else if (NodeAt(point) is { } node)
        {
            _selected = node.Id; Selected?.Invoke(node);
            if (CanEdit) { _drag = node.Id; _dragOffset = point - Position(node); }
        }
        else { _selected = null; Selected?.Invoke(null); }
        args.Handle();
    }
    protected override void MouseMove(GUIMouseMoveEventArgs args)
    {
        base.MouseMove(args); _mouse = args.RelativePixelPosition;
        var point = GraphPosition(_mouse);
        var tooltip = _panning || _drag != null || _wire != null ? null
            : PortAt(point) is { } hovered ? KiasControllerLabels.PortHelp(hovered.Port)
            : NodeAt(point) is { } hoveredNode ? KiasControllerLabels.Summary(hoveredNode) : null;
        if (tooltip != ToolTip)
        {
            HideTooltip();
            ToolTip = tooltip;
        }
        if (_panning) { _pan += (_mouse - _previous) / UIScale; _previous = _mouse; }
        if (_drag is { } id && _state.Nodes.FirstOrDefault(node => node.Id == id) is { } node)
        {
            var position = GraphPosition(_mouse) - _dragOffset;
            node.X = Math.Clamp(position.X, -100000, 100000); node.Y = Math.Clamp(position.Y, -100000, 100000);
        }
    }

    protected override void MouseExited()
    {
        HideTooltip();
        ToolTip = null;
        base.MouseExited();
    }
    protected override void KeyBindUp(GUIBoundKeyEventArgs args)
    {
        base.KeyBindUp(args);
        if (args.Function == EngineKeyFunctions.UIRightClick) { _panning = false; UserInterfaceManager.ControlFocused = null; args.Handle(); return; }
        if (args.Function != EngineKeyFunctions.UIClick) return;
        UserInterfaceManager.ControlFocused = null;
        if (_drag is { } id && _state.Nodes.FirstOrDefault(node => node.Id == id) is { } node)
            Edited?.Invoke(new() { Edit = KiasGraphEdit.Move, Node = id, X = node.X, Y = node.Y });
        _drag = null;
        if (_wire is { } pending && PortAt(GraphPosition(args.RelativePixelPosition)) is { } target)
        {
            if (KiasControllerLabels.Incompatibility(pending.Port, target.Port) is { } reason) Feedback?.Invoke(reason);
            else
            {
                var output = pending.Port.Direction == KiasPortDirection.Output ? pending : (target.Node.Id, target.Port);
                var input = pending.Port.Direction == KiasPortDirection.Input ? pending : (target.Node.Id, target.Port);
                Edited?.Invoke(new() { Edit = KiasGraphEdit.Connect, Wire = new()
                    { FromNode = output.Item1, FromPort = output.Item2.Id, ToNode = input.Item1, ToPort = input.Item2.Id } });
            }
        }
        _wire = null; args.Handle();
    }
    protected override void MouseWheel(GUIMouseWheelEventArgs args)
    {
        base.MouseWheel(args);
        var anchor = GraphPosition(args.RelativePixelPosition);
        _zoom = Math.Clamp(_zoom * MathF.Pow(1.15f, args.Delta.Y), .35f, 2f);
        _pan = args.RelativePixelPosition / UIScale - anchor * _zoom;
        args.Handle();
    }
}
