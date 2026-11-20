using System.Numerics;
using Content.Client._WF.NpcCrew;
using Robust.Client.Graphics;
using Robust.Shared.Map.Components;

namespace Content.Client.Shuttles.UI;

public partial class ShuttleNavControl
{
    public bool WfShowEncounters = true;

    private readonly Dictionary<string, Vector2> _wfCrewAnchors = new();

    /// <summary>
    /// For admins: tags each NPC-crewed ship with its crew and what it is doing, joins the ships of a battlegroup
    /// and points each ship at the grid its task is aimed at.
    /// </summary>
    private void DrawCrewGroups(DrawingHandleScreen handle, TransformComponent consoleXform, Matrix3x2 worldToView)
    {
        var setup = EntManager.System<WFCrewSetupClientSystem>();
        if (!WfShowEncounters || !setup.WatchRadar() || setup.Crews.Count == 0)
            return;

        _wfCrewAnchors.Clear();
        foreach (var crew in setup.Crews)
        {
            if (!TryCrewPoint(crew.Grid, consoleXform, worldToView, out var point))
                continue;

            var battlegroup = crew.Settings.Battlegroup;
            var color = CrewColor(battlegroup.Length > 0 ? battlegroup : crew.Group);
            // Only a crew with someone alive aboard joins its battlegroup or points at a target; the dead link nothing.
            if (battlegroup.Length > 0 && crew.Alive > 0)
            {
                if (_wfCrewAnchors.TryGetValue(battlegroup, out var anchor))
                    DrawCrewLine(handle, anchor, point, color.WithAlpha(0.6f));
                else
                    _wfCrewAnchors.Add(battlegroup, point);
            }

            if (crew.Alive > 0 && crew.ActivityTarget is { } target && TryCrewPoint(target, consoleXform, worldToView, out var aim))
                DrawCrewLine(handle, point, point + (aim - point) * 0.35f, Color.White.WithAlpha(0.5f));

            if (point.X < 0f || point.Y < 0f || point.X > PixelSize.X || point.Y > PixelSize.Y)
                continue;

            handle.DrawCircle(point, 3f * UIScale, color);
            if (setup.Tags.TryGetValue((crew.Grid, crew.Group), out var tag))
                handle.DrawString(Font, point + new Vector2(10f, -38f) * UIScale, tag, UIScale * 0.8f, color);
        }
    }

    /// <summary>The centre of a grid on this radar, in pixels.</summary>
    private bool TryCrewPoint(NetEntity net, TransformComponent consoleXform, Matrix3x2 worldToView, out Vector2 point)
    {
        point = default;
        if (!EntManager.TryGetEntity(net, out var uid)
            || !EntManager.TryGetComponent<MapGridComponent>(uid, out var grid)
            || !EntManager.TryGetComponent<TransformComponent>(uid, out var xform)
            || xform.MapID != consoleXform.MapID)
            return false;

        point = Vector2.Transform(grid.LocalAABB.Center, _transform.GetWorldMatrix(xform) * worldToView);
        return true;
    }

    private void DrawCrewLine(DrawingHandleScreen handle, Vector2 start, Vector2 end, Color color)
    {
        if (MathF.Max(start.X, end.X) < 0f || MathF.Min(start.X, end.X) > PixelSize.X
            || MathF.Max(start.Y, end.Y) < 0f || MathF.Min(start.Y, end.Y) > PixelSize.Y)
            return;

        handle.DrawLine(start, end, color);
    }

    /// <summary>A steady colour per name, so a battlegroup keeps its colour between frames and radars.</summary>
    private static Color CrewColor(string name)
    {
        var hash = 17;
        foreach (var letter in name)
        {
            hash = unchecked(hash * 31 + letter);
        }

        return Color.FromHsv(new Vector4((hash & 0xff) / 255f, 0.55f, 1f, 1f));
    }
}
