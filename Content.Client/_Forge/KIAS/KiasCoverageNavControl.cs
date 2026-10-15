using System.Numerics;
using Content.Client.Shuttles.UI;
using Content.Shared._Forge.KIAS;
using Robust.Client.Graphics;
using Robust.Shared.Map;

namespace Content.Client._Forge.KIAS;

public sealed class KiasCoverageNavControl : ShuttleNavControl
{
    private KiasCoverageGeometry? _geometry;
    private readonly Vector2[] _vertices = new Vector2[67];

    public void SetCoverage(KiasCoverageGeometry? geometry)
    {
        _geometry = geometry;
        if (geometry == null || !EntManager.TryGetEntity(geometry.Target, out var target) || target is not { } uid) return;
        SetMatrix(new EntityCoordinates(uid, Vector2.Zero), Angle.Zero);
        ShowIFF = false;
        ShowDocks = false;
        WorldMinRange = 8;
        WorldMaxRange = Math.Max(32, geometry.Radius * 1.2f);
        if (geometry.Shape == KiasCoverageShape.Room && EntManager.TryGetEntity(geometry.Grid, out var grid)
            && grid is { } gridUid)
        {
            var transforms = EntManager.System<SharedTransformSystem>();
            var origin = transforms.GetWorldPosition(uid);
            var matrix = transforms.GetWorldMatrix(gridUid);
            foreach (var cell in geometry.Cells)
                WorldMaxRange = Math.Max(WorldMaxRange,
                    Vector2.Distance(origin, Vector2.Transform(new Vector2(cell.X + .5f, cell.Y + .5f), matrix)) * 1.2f + 2);
        }
        ActualRadarRange = WorldMaxRange;
    }

    protected override void DrawSensorCoverage(DrawingHandleScreen handle, Matrix3x2 worldToView, MapId mapId)
    {
        if (_geometry is not { } geometry || !EntManager.TryGetEntity(geometry.Target, out var target) || target is not { } uid
            || !EntManager.TryGetComponent<TransformComponent>(uid, out var transform) || transform.MapID != mapId) return;
        var transforms = EntManager.System<SharedTransformSystem>();
        if (geometry.Shape is KiasCoverageShape.Room or KiasCoverageShape.Data)
        {
            if (!EntManager.TryGetEntity(geometry.Grid, out var grid) || grid is not { } gridUid) return;
            var gridMatrix = transforms.GetWorldMatrix(gridUid);
            foreach (var cell in geometry.Cells) DrawCell(cell, Color.Cyan);
            foreach (var cell in geometry.BoundaryCells) DrawCell(cell, Color.Orange);
            return;
            void DrawCell(Vector2i cell, Color color)
            {
                // Переводим клетки грида в экранные координаты.
                var matrix = gridMatrix * worldToView;
                _vertices[0] = Vector2.Transform(new Vector2(cell.X, cell.Y), matrix);
                _vertices[1] = Vector2.Transform(new Vector2(cell.X + 1, cell.Y), matrix);
                _vertices[2] = Vector2.Transform(new Vector2(cell.X + 1, cell.Y + 1), matrix);
                _vertices[3] = Vector2.Transform(new Vector2(cell.X, cell.Y + 1), matrix);
                _vertices[4] = _vertices[0];
                handle.DrawPrimitives(DrawPrimitiveTopology.TriangleFan, _vertices.AsSpan(0, 5), color.WithAlpha(.2f));
                handle.DrawPrimitives(DrawPrimitiveTopology.LineStrip, _vertices.AsSpan(0, 5), color.WithAlpha(.8f));
            }
        }
        var center = transforms.GetWorldPosition(uid);
        var direction = transforms.GetWorldRotation(uid).ToWorldVec();
        _vertices[0] = Vector2.Transform(center, worldToView);
        for (var i = 0; i <= 65; i++)
        {
            var angle = Angle.FromDegrees(-geometry.Arc / 2 + geometry.Arc * i / 65);
            var point = center + angle.RotateVec(direction) * geometry.Radius;
            _vertices[i + 1] = Vector2.Transform(point, worldToView);
        }
        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleFan, _vertices, Color.Cyan.WithAlpha(0.2f));
        handle.DrawPrimitives(DrawPrimitiveTopology.LineStrip, _vertices, Color.Cyan.WithAlpha(0.8f));
    }
}
