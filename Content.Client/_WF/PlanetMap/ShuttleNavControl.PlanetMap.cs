using System.Numerics;
using Content.Client._WF.PlanetMap;
using Content.Shared._WF.PlanetCargoHubs;
using Content.Shared._WF.PlanetMap;
using Robust.Client.Graphics;

namespace Content.Client.Shuttles.UI;

public partial class ShuttleNavControl
{
    // Muted map tones, so ships, docking ports and IFF stay readable on top.
    private static readonly Color[] WfMapGroundColours =
    {
        Color.Transparent,          // outside
        Color.FromHex("#0B1015"),   // void
        Color.FromHex("#2A4430"),   // grass
        Color.FromHex("#4F4632"),   // dry
        Color.FromHex("#5A646B"),   // snow
        Color.FromHex("#383D41"),   // rock
        Color.FromHex("#2D5236"),   // vegetation
        Color.FromHex("#565E64"),   // formations
        Color.FromHex("#1F4C72"),   // water
        Color.FromHex("#B0532A"),   // lava
        Color.FromHex("#6E4E9C"),   // plasma
        Color.FromHex("#5E2C36"),   // flesh
    };

    private static readonly Color WfMapRoadColour = Color.FromHex("#C9B57E");
    private static readonly Color WfMapShoulderColour = Color.FromHex("#6B6656");
    private static readonly Color WfMapTunnelColour = Color.FromHex("#6E6044");
    private static readonly Color WfMapTunnelCasingColour = Color.FromHex("#1C1A16");
    private static readonly Color WfMapTrailColour = Color.FromHex("#A8946A");
    private static readonly Color WfMapEdgeColour = Color.FromHex("#8A9BA8").WithAlpha(0.6f);
    private static readonly Color WfMapLandingColour = Color.FromHex("#7FD1FF");
    private static readonly Color WfMapSiteColour = Color.FromHex("#FFB347");
    private static readonly Color WfMapUnknownColour = Color.FromHex("#C8C8C8");
    private static readonly Color WfMapCityFill = Color.FromHex("#8C8C8C").WithAlpha(0.35f);

    /// <summary>Pixels per metre above which identified sites get their names.</summary>
    private const float WfMapLabelScale = 0.5f;

    private WFPlanetMapTerrain? _wfMapTerrain;
    private List<WFPlanetMapLine>? _wfMapLines;
    private float _wfMapScale;
    private readonly List<WFPlanetMapMesh.Patch> _wfMapPatches = new();
    private Vector2[] _wfMapBase = Array.Empty<Vector2>();
    private readonly List<(Box2 Bounds, Color Colour, Vector2[] Triangles)> _wfMapLineMeshes = new();
    private readonly List<Vector2> _wfMapScratch = new();
    private readonly List<Vector2> _wfMapPoints = new();

    /// <summary>Whether this frame's map marked the world's sites, so the signal overlay leaves them out.</summary>
    private bool _wfMapDrewSites;

    /// <summary>Draws a world's map under the radar: ground, water, roads, the landing zone, the edge and the sites.</summary>
    private bool DrawWfPlanetMap(DrawingHandleScreen handle, Matrix3x2 worldToView, EntityUid orbit)
    {
        if (!EntManager.TryGetComponent<WFPlanetMapComponent>(orbit, out var map))
            return false;

        var scale = MathF.Sqrt(worldToView.M11 * worldToView.M11 + worldToView.M12 * worldToView.M12);
        if (!float.IsFinite(scale) || scale <= 0f || !Matrix3x2.Invert(worldToView, out var viewToWorld))
            return false;

        WfMapRebuild(map, scale);

        var view = WfMapViewBounds(viewToWorld);
        var previous = handle.GetTransform();
        handle.SetTransform(worldToView * previous);
        try
        {
            if (_wfMapBase.Length > 0 && map.Terrain is { } terrain)
                handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, _wfMapBase, WfMapGroundColours[(int) terrain.Base]);

            foreach (var patch in _wfMapPatches)
            {
                if (patch.Bounds.Intersects(view))
                    handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, patch.Triangles, WfMapGroundColours[(int) patch.Ground]);
            }

            foreach (var (bounds, colour, triangles) in _wfMapLineMeshes)
            {
                if (bounds.Intersects(view))
                    handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, triangles, colour);
            }
        }
        finally
        {
            handle.SetTransform(previous);
        }

        WfMapDrawCircles(handle, worldToView, map, scale);
        WfMapDrawSites(handle, worldToView, map, scale);
        _wfMapDrewSites = map.Sites.Count > 0;
        return true;
    }

    /// <summary>The world box the view covers.</summary>
    private Box2 WfMapViewBounds(Matrix3x2 viewToWorld)
    {
        var a = Vector2.Transform(Vector2.Zero, viewToWorld);
        var b = Vector2.Transform(new Vector2(PixelSize.X, 0), viewToWorld);
        var c = Vector2.Transform(new Vector2(0, PixelSize.Y), viewToWorld);
        var d = Vector2.Transform((Vector2) PixelSize, viewToWorld);
        return new Box2(Vector2.Min(Vector2.Min(a, b), Vector2.Min(c, d)), Vector2.Max(Vector2.Max(a, b), Vector2.Max(c, d)));
    }

    /// <summary>Rebuilds the ground when its state changes and the lines when the zoom changes, since they keep a pixel width.</summary>
    private void WfMapRebuild(WFPlanetMapComponent map, float scale)
    {
        if (!ReferenceEquals(_wfMapTerrain, map.Terrain))
        {
            _wfMapTerrain = map.Terrain;
            _wfMapPatches.Clear();
            _wfMapBase = Array.Empty<Vector2>();
            if (map.Terrain is { } terrain)
            {
                _wfMapPatches.AddRange(WFPlanetMapMesh.Terrain(terrain));
                if (map.Radius > 0f)
                    _wfMapBase = WFPlanetMapMesh.Disc(map.Centre, map.Radius);
                else
                {
                    var min = (Vector2) terrain.Origin;
                    var max = min + new Vector2(terrain.Width, terrain.Height) * terrain.Cell;
                    _wfMapBase = new[] { min, new Vector2(max.X, min.Y), max, min, max, new Vector2(min.X, max.Y) };
                }
            }
        }

        if (ReferenceEquals(_wfMapLines, map.Lines) && MathF.Abs(_wfMapScale - scale) <= scale * 0.02f)
            return;

        _wfMapLines = map.Lines;
        _wfMapScale = scale;
        _wfMapLineMeshes.Clear();

        var pixel = UIScale / scale;
        var road = MathF.Max(map.RoadWidth, 2f * pixel);
        var casing = MathF.Max(road + 2f * map.ShoulderWidth, road + 2f * pixel);
        var trail = MathF.Max(1f, 1.2f * pixel);

        // Casings first, so every road's tarmac lies over every shoulder where two roads meet.
        WfMapAddLines(map, WFPlanetMapLineKind.Road, casing, WfMapShoulderColour, false);
        WfMapAddLines(map, WFPlanetMapLineKind.Tunnel, casing, WfMapTunnelCasingColour, false);
        WfMapAddLines(map, WFPlanetMapLineKind.Trail, trail, WfMapTrailColour, true, 5f * pixel, 4f * pixel);
        WfMapAddLines(map, WFPlanetMapLineKind.Road, road, WfMapRoadColour, false);
        WfMapAddLines(map, WFPlanetMapLineKind.Tunnel, road, WfMapTunnelColour, true, 6f * pixel, 3f * pixel);
    }

    private void WfMapAddLines(WFPlanetMapComponent map, WFPlanetMapLineKind kind, float width, Color colour, bool dashed,
        float dash = 0f, float gap = 0f)
    {
        foreach (var line in map.Lines)
        {
            if (line.Kind != kind || line.Points.Length < 2)
                continue;

            _wfMapPoints.Clear();
            foreach (var point in line.Points)
                _wfMapPoints.Add(new Vector2(point.X + 0.5f, point.Y + 0.5f));

            _wfMapScratch.Clear();
            if (dashed)
                WFPlanetMapMesh.Dashes(_wfMapScratch, _wfMapPoints, width, dash, gap);
            else
                WFPlanetMapMesh.Strip(_wfMapScratch, _wfMapPoints, width);
            if (_wfMapScratch.Count == 0)
                continue;

            var bounds = WFPlanetMapMesh.Bounds(_wfMapPoints).Enlarged(width);
            _wfMapLineMeshes.Add((bounds, colour, _wfMapScratch.ToArray()));
        }
    }

    /// <summary>The world's edge and a dashed ring round the landing zone.</summary>
    private void WfMapDrawCircles(DrawingHandleScreen handle, Matrix3x2 worldToView, WFPlanetMapComponent map, float scale)
    {
        var centre = Vector2.Transform(map.Centre, worldToView);
        if (map.Radius > 0f)
            handle.DrawCircle(centre, map.Radius * scale, WfMapEdgeColour, false);

        if (map.LandingRadius <= 0f)
            return;

        var radius = map.LandingRadius * scale;
        const int segments = 48;
        for (var i = 0; i < segments; i += 2)
        {
            var a = MathF.Tau * i / segments;
            var b = MathF.Tau * (i + 1) / segments;
            // Through the view, so the dashes turn with the ground under them.
            handle.DrawLine(Vector2.Transform(map.Centre + new Vector2(MathF.Cos(a), MathF.Sin(a)) * map.LandingRadius, worldToView),
                Vector2.Transform(map.Centre + new Vector2(MathF.Cos(b), MathF.Sin(b)) * map.LandingRadius, worldToView), WfMapLandingColour);
        }

        if (scale < WfMapLabelScale / 2f)
            return;
        var label = Loc.GetString("wf-planet-map-landing");
        var size = handle.GetDimensions(Font, label, UIScale * 0.7f);
        handle.DrawString(Font, centre + new Vector2(-size.X / 2f, -radius - size.Y), label, UIScale * 0.7f, WfMapLandingColour);
    }

    /// <summary>A glyph for every site by kind, a "?" for unknown ones, an outline for cities and names when zoomed in.</summary>
    private void WfMapDrawSites(DrawingHandleScreen handle, Matrix3x2 worldToView, WFPlanetMapComponent map, float scale)
    {
        var size = 4f * UIScale;
        var bounds = new UIBox2(new Vector2(-size * 4f), (Vector2) PixelSize + new Vector2(size * 4f));
        foreach (var site in map.Sites)
        {
            var point = Vector2.Transform(new Vector2(site.Position.X + 0.5f, site.Position.Y + 0.5f), worldToView);
            var known = site.Name.Length > 0;
            var depot = site.Kind == WFPlanetMapSiteKind.Depot;
            var colour = depot ? WFPlanetCargoHubSites.Colour : known ? WfMapSiteColour : WfMapUnknownColour;

            if (site.Kind == WFPlanetMapSiteKind.City)
            {
                // The radar turns with the ship, so the box is four corners and not a screen rectangle.
                var corners = WFPlanetMapMesh.BoxCorners(site.Bounds.BottomLeft, site.Bounds.TopRight, worldToView);
                var min = Vector2.Min(Vector2.Min(corners[0], corners[1]), Vector2.Min(corners[2], corners[3]));
                var max = Vector2.Max(Vector2.Max(corners[0], corners[1]), Vector2.Max(corners[2], corners[3]));
                if (new UIBox2(min, max).Intersects(bounds))
                {
                    handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList,
                        new[] { corners[0], corners[1], corners[2], corners[0], corners[2], corners[3] }, WfMapCityFill);
                    for (var i = 0; i < corners.Length; i++)
                        handle.DrawLine(corners[i], corners[(i + 1) % corners.Length], colour);
                }
                point = (corners[0] + corners[2]) / 2f;
            }

            if (!bounds.Contains(point))
                continue;

            WfMapGlyph(handle, site.Kind, point, size, colour);

            if (!known)
                continue;
            // A city's or a cargo depot's name shows at any zoom.
            if (site.Kind != WFPlanetMapSiteKind.City && !depot && scale < WfMapLabelScale)
                continue;

            var label = Loc.GetString(site.Name);
            var labelSize = handle.GetDimensions(Font, label, UIScale * 0.75f);
            handle.DrawString(Font, point + new Vector2(size * 2f, -labelSize.Y / 2f), label, UIScale * 0.75f, colour);
        }
    }

    private void WfMapGlyph(DrawingHandleScreen handle, WFPlanetMapSiteKind kind, Vector2 point, float size, Color colour)
    {
        var square = UIBox2.FromDimensions(point - new Vector2(size), new Vector2(size * 2f));
        switch (kind)
        {
            case WFPlanetMapSiteKind.Unknown:
                handle.DrawCircle(point, size * 1.4f, colour, false);
                var mark = Loc.GetString("wf-planet-map-unknown");
                var markSize = handle.GetDimensions(Font, mark, UIScale * 0.7f);
                handle.DrawString(Font, point - markSize / 2f, mark, UIScale * 0.7f, colour);
                break;
            case WFPlanetMapSiteKind.House:
                handle.DrawRect(UIBox2.FromDimensions(point - new Vector2(size, size * 0.4f), new Vector2(size * 2f, size * 1.4f)), colour, false);
                handle.DrawLine(point + new Vector2(-size * 1.3f, -size * 0.4f), point + new Vector2(0f, -size * 1.4f), colour);
                handle.DrawLine(point + new Vector2(0f, -size * 1.4f), point + new Vector2(size * 1.3f, -size * 0.4f), colour);
                break;
            case WFPlanetMapSiteKind.Fuel:
                handle.DrawRect(square, colour);
                handle.DrawLine(point + new Vector2(size, -size * 0.5f), point + new Vector2(size * 1.6f, 0f), colour);
                handle.DrawLine(point + new Vector2(size * 1.6f, 0f), point + new Vector2(size * 1.6f, size), colour);
                break;
            case WFPlanetMapSiteKind.Vault:
                handle.DrawRect(square, colour, false);
                handle.DrawRect(UIBox2.FromDimensions(point - new Vector2(size * 0.4f), new Vector2(size * 0.8f)), colour);
                break;
            case WFPlanetMapSiteKind.Facility:
                handle.DrawRect(square, colour, false);
                handle.DrawLine(square.TopLeft, square.BottomRight, colour);
                handle.DrawLine(square.TopRight, square.BottomLeft, colour);
                break;
            case WFPlanetMapSiteKind.City:
                handle.DrawRect(UIBox2.FromDimensions(point - new Vector2(size * 0.6f), new Vector2(size * 1.2f)), colour);
                break;
            case WFPlanetMapSiteKind.Camp:
                handle.DrawLine(point + new Vector2(-size * 1.2f, size), point + new Vector2(0f, -size * 1.2f), colour);
                handle.DrawLine(point + new Vector2(0f, -size * 1.2f), point + new Vector2(size * 1.2f, size), colour);
                handle.DrawLine(point + new Vector2(size * 1.2f, size), point + new Vector2(-size * 1.2f, size), colour);
                break;
            case WFPlanetMapSiteKind.Depot:
                // A crate in a ring, bigger than any other glyph.
                handle.DrawCircle(point, size * 2.2f, colour, false);
                handle.DrawRect(UIBox2.FromDimensions(point - new Vector2(size * 1.1f), new Vector2(size * 2.2f)), colour, false);
                handle.DrawLine(point + new Vector2(-size * 1.1f, -size * 0.35f), point + new Vector2(size * 1.1f, -size * 0.35f), colour);
                handle.DrawLine(point + new Vector2(-size * 1.1f, size * 0.35f), point + new Vector2(size * 1.1f, size * 0.35f), colour);
                break;
            case WFPlanetMapSiteKind.Wreck:
                handle.DrawLine(point - new Vector2(size), point + new Vector2(size), colour);
                handle.DrawLine(point + new Vector2(size, -size), point + new Vector2(-size, size), colour);
                break;
            default:
                handle.DrawRect(square, colour, false);
                break;
        }
    }
}
