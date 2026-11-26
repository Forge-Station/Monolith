using System.Numerics;

namespace Content.Shared._WF.ShipShields;

/// <summary>Caches tactical-map outline segments with the same sector boundaries as shield collisions.</summary>
public sealed class WFShipShieldRadarGeometry
{
    /// <summary>A shield-local line and its allocated strength.</summary>
    public readonly record struct Segment(Vector2 Start, Vector2 End, float Strength);

    /// <summary>Protected lines, rebuilt only when the hull or allocation changes.</summary>
    public readonly List<Segment> Segments = new();
    private Vector2[][] _contours = Array.Empty<Vector2[]>();
    private Vector2 _center;
    private float _direction;
    private float _concentration;
    private float _arc;

    /// <summary>Updates the cached lines without rebuilding them for health or camera changes.</summary>
    public void Update(Vector2[][] contours, Vector2 center, float direction, float concentration, float arc)
    {
        if (WFShipShieldMesh.ContoursEqual(_contours, contours) && _center == center &&
            _direction == direction && _concentration == concentration && _arc == arc)
            return;
        _contours = contours;
        _center = center;
        _direction = direction;
        _concentration = concentration;
        _arc = arc;
        Segments.Clear();
        foreach (var contour in contours)
        for (var i = 0; i < contour.Length; i++)
        {
            var start = contour[i];
            var end = contour[(i + 1) % contour.Length];
            if (concentration <= 0f || arc >= WFShipShieldShuntMath.FullArc - 0.00001f)
            {
                Segments.Add(new Segment(start, end, 1f));
                continue;
            }
            AddSector(start, end, direction, arc, WFShipShieldShuntMath.PeakStrength(concentration, arc));
            if (concentration < 1f)
                AddSector(start, end, direction + MathF.PI, MathF.Tau - arc, 1f - concentration);
        }
    }

    private void AddSector(Vector2 start, Vector2 end, float direction, float arc, float strength)
    {
        foreach (var segment in WFShipShieldShuntMath.ProtectedSegments(start, end, _center, direction, 1f, arc))
            Segments.Add(new Segment(segment.Start, segment.End, strength));
    }
}
