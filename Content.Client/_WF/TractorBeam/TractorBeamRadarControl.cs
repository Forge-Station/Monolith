using System.Numerics;
using Content.Shared._WF.TractorBeam;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared.Input;

namespace Content.Client._WF.TractorBeam;

/// <summary>A targeting plot containing only contacts supplied by the server.</summary>
public sealed class TractorBeamRadarControl : Control
{
    public Action<NetEntity>? OnTargetSelected;
    public TractorBeamTargetEntry[] Targets = Array.Empty<TractorBeamTargetEntry>();
    public NetEntity? Selected;
    public NetEntity? Locked;
    public TractorBeamEmitterEntry? Emitter;
    public float Range = 256f;
    private readonly Vector2[] _sectorTriangles = new Vector2[6];

    public TractorBeamRadarControl()
    {
        MinSize = new Vector2(360, 360);
        HorizontalExpand = true;
        VerticalExpand = true;
        MouseFilter = MouseFilterMode.Stop;
    }

    private Vector2 PlotPosition(Vector2 position, Vector2 size)
    {
        var scale = MathF.Min(size.X, size.Y) * 0.45f / MathF.Max(Range, 1f);
        return size / 2f + new Vector2(position.X, -position.Y) * scale;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        var size = (Vector2) PixelSize;
        var center = size / 2f;
        var radius = MathF.Min(size.X, size.Y) * 0.45f;
        var gridColor = new Color(0.12f, 0.3f, 0.35f);
        handle.DrawRect(new UIBox2(Vector2.Zero, size), new Color(0.015f, 0.035f, 0.045f));
        handle.DrawCircle(center, radius, gridColor, false);
        handle.DrawCircle(center, radius / 2, gridColor, false);
        handle.DrawLine(center - new Vector2(radius, 0), center + new Vector2(radius, 0), gridColor);
        handle.DrawLine(center - new Vector2(0, radius), center + new Vector2(0, radius), gridColor);
        if (Emitter is { } emitter)
            DrawOperatingCone(handle, size, emitter);
        handle.DrawCircle(center, 4 * UIScale, Color.White);

        foreach (var target in Targets)
        {
            var position = PlotPosition(target.Position, size);
            var selected = Selected == target.Entity;
            var locked = Locked == target.Entity;
            var color = locked ? Color.Cyan : selected ? Color.Yellow : Color.LightGreen;
            if (Emitter is { } dish)
            {
                var offset = target.Position - dish.Position;
                if (!TractorBeamOperatingCone.Contains(offset, dish.Direction, dish.Range, dish.ConeHalfAngle))
                    color = selected || locked ? Color.Red : new Color(0.4f, 0.45f, 0.45f);
                else if ((selected || locked) && TractorBeamOperatingCone.IsNearEdge(offset, dish.Direction, dish.Range, dish.ConeHalfAngle))
                    color = Color.Orange;
            }
            if (locked)
                handle.DrawLine(PlotPosition(Emitter?.Position ?? Vector2.Zero, size), position, color.WithAlpha(0.65f));
            handle.DrawCircle(position, 4 * UIScale, color);
            if (selected || locked)
                handle.DrawCircle(position, 9 * UIScale, color, false);
        }
    }

    private void DrawOperatingCone(DrawingHandleScreen handle, Vector2 size, TractorBeamEmitterEntry emitter)
    {
        if (emitter.Range <= 0 || emitter.Direction.LengthSquared() < 0.001f || emitter.ConeHalfAngle <= 0)
            return;

        var heading = MathF.Atan2(emitter.Direction.Y, emitter.Direction.X);
        var halfAngle = emitter.ConeHalfAngle;
        var safeAngle = halfAngle * 0.85f;
        var safeRange = emitter.Range * 0.85f;
        var origin = PlotPosition(emitter.Position, size);
        var outline = Color.Cyan.WithAlpha(0.7f);
        var warning = Color.Orange.WithAlpha(0.12f);
        // Draw the dish's actual forward sector even before a target has been acquired.
        Sector(heading - halfAngle, heading + halfAngle, 0, emitter.Range, Color.Cyan.WithAlpha(0.045f));
        Sector(heading - halfAngle, heading - safeAngle, 0, safeRange, warning);
        Sector(heading + safeAngle, heading + halfAngle, 0, safeRange, warning);
        Sector(heading - halfAngle, heading + halfAngle, safeRange, emitter.Range, warning);
        var start = Point(heading - halfAngle, emitter.Range);
        var end = Point(heading + halfAngle, emitter.Range);
        handle.DrawLine(origin, start, outline);
        handle.DrawLine(origin, end, outline);
        for (var i = 0; i < 64; i++)
        {
            var angle = heading - halfAngle + 2 * halfAngle * i / 64;
            var next = heading - halfAngle + 2 * halfAngle * (i + 1) / 64;
            handle.DrawLine(Point(angle, emitter.Range), Point(next, emitter.Range), outline);
            if (i % 2 == 0)
                handle.DrawLine(Point(angle, safeRange), Point(next, safeRange), Color.Orange.WithAlpha(0.5f));
        }
        handle.DrawLine(origin, Point(heading - safeAngle, safeRange), Color.Orange.WithAlpha(0.35f));
        handle.DrawLine(origin, Point(heading + safeAngle, safeRange), Color.Orange.WithAlpha(0.35f));
        handle.DrawCircle(origin, 3 * UIScale, Color.Cyan);
        handle.DrawLine(origin, Point(heading, emitter.Range * 0.12f), Color.Cyan);

        Vector2 Point(float angle, float distance)
        {
            return PlotPosition(emitter.Position + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * distance, size);
        }

        void Sector(float from, float to, float innerRadius, float outerRadius, Color color)
        {
            const int segments = 32;
            for (var i = 0; i < segments; i++)
            {
                var first = from + (to - from) * i / segments;
                var second = from + (to - from) * (i + 1) / segments;
                _sectorTriangles[0] = Point(first, innerRadius);
                _sectorTriangles[1] = Point(first, outerRadius);
                _sectorTriangles[2] = Point(second, outerRadius);
                _sectorTriangles[3] = _sectorTriangles[0];
                _sectorTriangles[4] = _sectorTriangles[2];
                _sectorTriangles[5] = Point(second, innerRadius);
                handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, _sectorTriangles, color);
            }
        }
    }

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        base.KeyBindDown(args);
        if (args.Function != EngineKeyFunctions.UIClick)
            return;

        NetEntity? closest = null;
        var distance = 16f * 16f;
        foreach (var target in Targets)
        {
            var candidateDistance = Vector2.DistanceSquared(PlotPosition(target.Position, Size), args.RelativePosition);
            if (candidateDistance >= distance)
                continue;
            distance = candidateDistance;
            closest = target.Entity;
        }

        if (closest is { } entity)
        {
            OnTargetSelected?.Invoke(entity);
            args.Handle();
        }
    }
}
