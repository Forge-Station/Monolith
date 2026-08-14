using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Shared._Forge.Bss;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

// Forge-Change-full: BSS system map
namespace Content.Client.Shuttles.UI;

/// <summary>
/// Interactive BSS topology map with pan/zoom, matching MAP-tab camera controls.
/// </summary>
public sealed class BssSystemMapControl : Control
{
    private const float NodeRadius = 22f;
    private const float MinRange = 90f;
    private const float MaxRange = 900f;
    private const float ZoomFactor = 1.18f;
    private const float RecenterSpeed = 8f;

    private readonly Font _font;
    private readonly Font _hintFont;
    private readonly IGameTiming _timing;
    private BssSystemMapState _state = BssSystemMapState.Empty();
    private Vector2 _offset;
    private Vector2 _targetOffset;
    private float _range = 320f;
    private bool _dragging;
    private bool _recentering;
    private string? _centeredSector;

    public string? SelectedSector { get; private set; }
    public event Action<BssSystemMapNode?>? SelectionChanged;

    public BssSystemMapControl()
    {
        var cache = IoCManager.Resolve<IResourceCache>();
        _timing = IoCManager.Resolve<IGameTiming>();
        _font = new VectorFont(
            cache.GetResource<FontResource>("/Fonts/NotoSansDisplay/NotoSansDisplay-Bold.ttf"),
            14);
        _hintFont = new VectorFont(
            cache.GetResource<FontResource>("/EngineFonts/NotoSans/NotoSans-Regular.ttf"),
            12);
        MouseFilter = MouseFilterMode.Stop;
        MinSize = new Vector2(700f, 700f);
        RectClipContent = true;
    }

    public void UpdateState(BssSystemMapState state)
    {
        _state = state;
        if (SelectedSector != null &&
            state.Nodes.All(node => node.Id != SelectedSector || !node.Reachable || !node.Online))
        {
            SelectedSector = null;
            SelectionChanged?.Invoke(null);
        }

        if (state.CurrentSector != _centeredSector)
        {
            _centeredSector = state.CurrentSector;
            var current = state.Nodes.FirstOrDefault(node => node.Id == state.CurrentSector);
            if (current != null)
            {
                _targetOffset = current.Position;
                _recentering = true;
            }
        }

        InvalidateMeasure();
    }

    public void Select(string? sector)
    {
        SelectedSector = sector;
        SelectionChanged?.Invoke(GetSelectedNode());
        InvalidateMeasure();
    }

    public BssSystemMapNode? GetSelectedNode()
        => SelectedSector == null
            ? null
            : _state.Nodes.FirstOrDefault(node => node.Id == SelectedSector);

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        StepCamera();

        handle.DrawRect(PixelSizeBox, Color.FromHex("#070b12"));
        DrawGrid(handle);
        DrawStars(handle);
        DrawSectorTerritories(handle);

        foreach (var edge in _state.Edges)
        {
            var fromNode = _state.Nodes.FirstOrDefault(node => node.Id == edge.From);
            var toNode = _state.Nodes.FirstOrDefault(node => node.Id == edge.To);
            if (fromNode == null || toNode == null)
                continue;

            var from = WorldToPixel(fromNode.Position);
            var to = WorldToPixel(toNode.Position);
            var color = edge.Online
                ? Color.InterpolateBetween(fromNode.Color, toNode.Color, 0.5f).WithAlpha(0.85f)
                : Color.FromHex("#2a3038");
            handle.DrawLine(from, to, color);
        }

        foreach (var node in _state.Nodes)
        {
            var position = WorldToPixel(node.Position);
            var scale = GetScale();
            var half = NodeRadius * Math.Clamp(scale / 1.4f, 0.7f, 1.35f);
            var ring = node.Current
                ? Color.FromHex("#7dffb0")
                : node.Id == SelectedSector
                    ? Color.FromHex("#f2d36b")
                    : node.Reachable && node.Online
                        ? node.Color
                        : node.Color.WithAlpha(0.45f);

            DrawNodeSquare(handle, position, half + 5f, ring.WithAlpha(0.22f), filled: true);
            DrawNodeSquare(handle, position, half, ring, filled: true);
            DrawNodeSquare(handle, position, half - 4f, Color.FromHex("#10151d"), filled: true);
            DrawNodeSquare(
                handle,
                position,
                MathF.Max(4f, half - 9f),
                node.Color.WithAlpha(node.Online ? 0.95f : 0.35f),
                filled: true);

            DrawBackedLabel(
                handle,
                _font,
                position + new Vector2(0f, half + 16f),
                node.Name,
                Color.FromHex("#f6f3ea"));
        }

        DrawLegend(handle);

        var hint = Loc.GetString("shuttle-console-bss-map-hint");
        DrawBackedLabel(
            handle,
            _hintFont,
            new Vector2(PixelWidth / 2f, PixelHeight - 18f),
            hint,
            Color.FromHex("#c5d4e4"));
    }

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        base.KeyBindDown(args);
        if (args.Function == EngineKeyFunctions.UseSecondary)
        {
            _dragging = true;
            _recentering = false;
            args.Handle();
            return;
        }

        if (args.Function != EngineKeyFunctions.UIClick)
            return;

        var world = PixelToWorld(args.RelativePixelPosition);
        var scale = GetScale();
        var hit = NodeRadius * 1.6f / MathF.Max(scale, 0.001f);
        var selected = _state.Nodes
            .Where(node => node.Reachable && node.Online)
            .Select(node => (Node: node, Distance: Chebyshev(world, node.Position)))
            .Where(entry => entry.Distance <= hit)
            .OrderBy(entry => entry.Distance)
            .Select(entry => entry.Node)
            .FirstOrDefault();

        SelectedSector = selected?.Id;
        SelectionChanged?.Invoke(selected);
        args.Handle();
    }

    protected override void KeyBindUp(GUIBoundKeyEventArgs args)
    {
        base.KeyBindUp(args);
        if (args.Function == EngineKeyFunctions.UseSecondary)
        {
            _dragging = false;
            args.Handle();
        }
    }

    protected override void MouseMove(GUIMouseMoveEventArgs args)
    {
        base.MouseMove(args);
        if (!_dragging)
            return;

        var scale = GetScale();
        if (scale <= 0.001f)
            return;

        _offset -= new Vector2(args.Relative.X, -args.Relative.Y) * UIScale / scale;
        _recentering = false;
        args.Handle();
    }

    protected override void MouseWheel(GUIMouseWheelEventArgs args)
    {
        base.MouseWheel(args);
        var zoom = args.Delta.Y > 0 ? 1f / ZoomFactor : ZoomFactor;
        _range = Math.Clamp(_range * zoom, MinRange, MaxRange);
        args.Handle();
    }

    private void StepCamera()
    {
        if (!_recentering)
            return;

        var dt = (float) _timing.FrameTime.TotalSeconds;
        _offset = Vector2.Lerp(_offset, _targetOffset, Math.Clamp(dt * RecenterSpeed, 0f, 1f));
        if (Vector2.DistanceSquared(_offset, _targetOffset) < 0.25f)
        {
            _offset = _targetOffset;
            _recentering = false;
        }
    }

    private float GetScale()
    {
        var size = MathF.Min(MathF.Max(1f, PixelWidth), MathF.Max(1f, PixelHeight));
        return size / (2f * MathF.Max(_range, 1f));
    }

    private Vector2 WorldToPixel(Vector2 world)
    {
        var mid = PixelSize / 2f;
        var delta = world - _offset;
        return mid + new Vector2(delta.X, -delta.Y) * GetScale();
    }

    private Vector2 PixelToWorld(Vector2 pixel)
    {
        var mid = PixelSize / 2f;
        var local = (pixel - mid) / GetScale();
        return _offset + new Vector2(local.X, -local.Y);
    }

    private void DrawGrid(DrawingHandleScreen handle)
    {
        var color = Color.FromHex("#1b2736");
        var step = 40f;
        var topLeft = PixelToWorld(Vector2.Zero);
        var bottomRight = PixelToWorld(PixelSize);
        var minX = MathF.Min(topLeft.X, bottomRight.X);
        var maxX = MathF.Max(topLeft.X, bottomRight.X);
        var minY = MathF.Min(topLeft.Y, bottomRight.Y);
        var maxY = MathF.Max(topLeft.Y, bottomRight.Y);

        for (var x = MathF.Floor(minX / step) * step; x <= maxX; x += step)
            handle.DrawLine(WorldToPixel(new Vector2(x, minY)), WorldToPixel(new Vector2(x, maxY)), color);
        for (var y = MathF.Floor(minY / step) * step; y <= maxY; y += step)
            handle.DrawLine(WorldToPixel(new Vector2(minX, y)), WorldToPixel(new Vector2(maxX, y)), color);
    }

    private void DrawStars(DrawingHandleScreen handle)
    {
        var color = Color.FromHex("#c9d6e5").WithAlpha(0.35f);
        for (var i = 0; i < 48; i++)
        {
            var seed = i * 97.13f + _offset.X * 0.02f;
            var x = (MathF.Sin(seed) * 0.5f + 0.5f) * PixelWidth;
            var y = (MathF.Cos(seed * 1.37f) * 0.5f + 0.5f) * PixelHeight;
            handle.DrawCircle(new Vector2(x, y), i % 7 == 0 ? 1.8f : 1.1f, color);
        }
    }

    private void DrawSectorTerritories(DrawingHandleScreen handle)
    {
        var visible = _state.Nodes
            .Where(node => node.Group != "black" || node.Reachable)
            .ToList();
        if (visible.Count == 0)
            return;

        const float cell = 8f;
        const float influence = 100f;
        var cols = Math.Max(1, (int) MathF.Ceiling(PixelWidth / cell));
        var rows = Math.Max(1, (int) MathF.Ceiling(PixelHeight / cell));
        var owner = new BssSystemMapNode?[cols * rows];

        for (var row = 0; row < rows; row++)
        {
            for (var col = 0; col < cols; col++)
            {
                var world = PixelToWorld(new Vector2((col + 0.5f) * cell, (row + 0.5f) * cell));
                BssSystemMapNode? nearest = null;
                var best = influence;
                foreach (var node in visible)
                {
                    var dist = Vector2.Distance(world, node.Position);
                    if (dist >= best)
                        continue;
                    best = dist;
                    nearest = node;
                }

                if (nearest == null)
                    continue;

                owner[row * cols + col] = nearest;
                var alpha = nearest.Group switch
                {
                    "green" => 0.26f,
                    "yellow" => 0.24f,
                    "red" => 0.28f,
                    "black" => 0.32f,
                    _ => 0.18f,
                };
                if (nearest.Current || nearest.Id == SelectedSector)
                    alpha += 0.12f;

                var edge = Math.Clamp((influence - best) / (influence * 0.2f), 0f, 1f);
                handle.DrawRect(
                    UIBox2.FromDimensions(new Vector2(col * cell, row * cell), new Vector2(cell + 0.5f, cell + 0.5f)),
                    nearest.Color.WithAlpha(alpha * edge));
            }
        }

        for (var row = 0; row < rows; row++)
        {
            for (var col = 0; col < cols; col++)
            {
                var node = owner[row * cols + col];
                if (node == null)
                    continue;

                if (col + 1 < cols)
                {
                    var right = owner[row * cols + col + 1];
                    if (right != null && right.Group != node.Group)
                    {
                        var x = (col + 1) * cell;
                        handle.DrawLine(
                            new Vector2(x, row * cell),
                            new Vector2(x, (row + 1) * cell),
                            node.Color.WithAlpha(0.55f));
                    }
                }

                if (row + 1 < rows)
                {
                    var down = owner[(row + 1) * cols + col];
                    if (down != null && down.Group != node.Group)
                    {
                        var y = (row + 1) * cell;
                        handle.DrawLine(
                            new Vector2(col * cell, y),
                            new Vector2((col + 1) * cell, y),
                            node.Color.WithAlpha(0.55f));
                    }
                }
            }
        }
    }

    private static void DrawNodeSquare(DrawingHandleScreen handle, Vector2 center, float half, Color color, bool filled)
    {
        handle.DrawRect(
            UIBox2.FromDimensions(center - new Vector2(half, half), new Vector2(half * 2f, half * 2f)),
            color,
            filled);
    }

    private void DrawBackedLabel(DrawingHandleScreen handle, Font font, Vector2 center, string text, Color color)
    {
        var size = handle.GetDimensions(font, text, 1f);
        var pad = new Vector2(7f, 3f);
        var topLeft = center - new Vector2(size.X / 2f, size.Y / 2f) - pad;
        var box = UIBox2.FromDimensions(topLeft, size + pad * 2f);
        handle.DrawRect(box, Color.FromHex("#070b12").WithAlpha(0.9f));
        handle.DrawRect(box, color.WithAlpha(0.35f), filled: false);
        handle.DrawString(font, topLeft + pad, text, color);
    }

    private static float Chebyshev(Vector2 a, Vector2 b)
    {
        var delta = Vector2.Abs(a - b);
        return MathF.Max(delta.X, delta.Y);
    }

    private void DrawLegend(DrawingHandleScreen handle)
    {
        if (_state.Groups.Count == 0)
            return;

        var x = 14f;
        var y = 12f;
        foreach (var group in _state.Groups)
        {
            var nameSize = handle.GetDimensions(_font, group.Name, 1f);
            var plate = UIBox2.FromDimensions(new Vector2(x - 4f, y - 2f), new Vector2(28f + nameSize.X, 22f));
            handle.DrawRect(plate, Color.FromHex("#070b12").WithAlpha(0.82f));
            handle.DrawRect(UIBox2.FromDimensions(new Vector2(x, y + 3f), new Vector2(14f, 14f)), group.Color.WithAlpha(0.45f));
            handle.DrawRect(UIBox2.FromDimensions(new Vector2(x, y + 3f), new Vector2(14f, 14f)), group.Color, filled: false);
            handle.DrawString(_font, new Vector2(x + 20f, y), group.Name, Color.FromHex("#f6f3ea"));
            y += 24f;
        }
    }
}
