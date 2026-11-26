using System.Numerics;
using Content.Client._WF.Performance;
using Robust.Client.Graphics;
using Robust.Shared.Map;

namespace Content.Client.Shuttles.UI;

public partial class ShuttleNavControl
{
    /// <summary>The asteroid grids' IFF colour (<c>WFAsteroidGridBase</c>), for rocks drawn from their field list.</summary>
    private static readonly Color WfFieldRockColor = Color.FromHex("#d67e27");

    private readonly List<WFFarRock> _wfFarRocks = new();

    /// <summary>
    /// Queues the rocks whose grids this client hasn't been sent, from their field lists, so a distant field still
    /// shows on radar. Drawn like an asteroid grid: a faint fill on the fill pass, then an outline.
    /// </summary>
    private void WfQueueFieldRocks(DrawingHandleScreen handle, Matrix3x2 worldToView, MapId map, Box2 viewAABB,
        bool renderFill)
    {
        var fields = EntManager.System<WFAsteroidFieldSystem>();
        _wfFarRocks.Clear();
        fields.GetFarRocks(map, viewAABB.Enlarged(12f), _wfFarRocks);
        var fillColor = Color.ToSrgb(Color.InterpolateBetween(BackingColor, WfFieldRockColor, 0.01f)).WithAlpha(1f);
        foreach (var far in _wfFarRocks)
        {
            if (fields.GetShape(far.Proto, far.Rock.Variant) is not { } shape)
                continue;

            var rockToView = Matrix3Helpers.CreateTransform(far.Rock.Position, new Angle(far.Rock.Rotation)) * worldToView;
            if (renderFill)
                WfQueue(handle, DrawPrimitiveTopology.TriangleList, shape.Fill, 0, shape.Fill.Count, rockToView, fillColor);
            else
                WfQueue(handle, DrawPrimitiveTopology.LineList, shape.Lines, 0, shape.Lines.Count, rockToView, WfFieldRockColor);
        }
    }
}
