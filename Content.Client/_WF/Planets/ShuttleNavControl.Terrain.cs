using System;
using System.Collections.Generic;
using System.Numerics;
using Content.Shared._WF.Planets;
using Content.Shared._CE.ZLevels.Core.Components;
using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Client.Shuttles.UI;

public partial class ShuttleNavControl
{
    [Dependency] private ITileDefinitionManager _wfRadarTiles = default!;
    [Dependency] private IGameTiming _wfRadarTime = default!;

    /// <summary>Whether the radar draws planet terrain.</summary>
    public bool ShowPlanetTerrain { get; set; } = true;
    // Keep the server's grid-relative origin even while the user pans onto map coordinates.
    private EntityCoordinates? _wfRadarOrigin;
    private EntityUid? _wfTerrainMap;
    private TimeSpan _wfTerrainRefresh;
    private readonly Vector2[][] _wfTerrainVertices = new Vector2[12][];
    private readonly Dictionary<int, int> _wfTerrainColours = new();
    private string? _wfTerrainDiagnostic;

    private void ReportWfTerrain(string status)
    {
        if (_wfTerrainDiagnostic == status)
            return;
        _wfTerrainDiagnostic = status;
        Logger.DebugS("wf.radar", status);
    }

    private void AddWfTerrainButton(LayoutContainer parent)
    {
        var button = new Button
        {
            Text = Loc.GetString(ShowPlanetTerrain ? "wf-radar-terrain-on" : "wf-radar-terrain-off"),
            ToolTip = Loc.GetString("wf-radar-terrain-tooltip"),
            ToggleMode = true,
            Pressed = ShowPlanetTerrain,
        };
        button.OnToggled += args =>
        {
            ShowPlanetTerrain = args.Pressed;
            button.Text = Loc.GetString(args.Pressed ? "wf-radar-terrain-on" : "wf-radar-terrain-off");
        };
        parent.AddChild(button);
        LayoutContainer.SetAnchorAndMarginPreset(button, LayoutContainer.LayoutPreset.TopRight, margin: 10);
    }

    // Subdued colours keep ships, docking ports and IFF readable over the terrain.
    private static readonly Color[] WfTerrainPalette =
    {
        Color.FromHex("#233B2C"), // land
        Color.FromHex("#172E45"), // water
        Color.FromHex("#49422F"), // sand / soil
        Color.FromHex("#424B50"), // snow / ice
        Color.FromHex("#303438"), // rock / artificial tile
        Color.FromHex("#080D12"), // scarred ground
        Color.FromHex("#B35B32"), // lava
        Color.FromHex("#67737B"), // rock formations / crystals
        Color.FromHex("#365942"), // vegetation
        Color.FromHex("#74549A"), // liquid plasma
        Color.FromHex("#623039"), // flesh terrain
        Color.FromHex("#9A5963"), // flesh walls
    };

    /// <summary>The orbit layer carrying a map's radar terrain recipe, resolved through transit maps and the world's network.</summary>
    private WFOrbitLayerComponent? GetWfTerrainRecipe(EntityUid? map)
    {
        if (EntManager.TryGetComponent<WFOrbitLayerComponent>(map, out var orbit))
            return orbit;

        // Transit maps sit between two permanent layers of the same world.
        if (EntManager.TryGetComponent<CEZTransitMapComponent>(map, out var transit))
            map = transit.LowerMap ?? transit.UpperMap;
        if (EntManager.TryGetComponent<WFOrbitLayerComponent>(map, out orbit))
            return orbit;
        if (!EntManager.TryGetComponent<WFPlanetLayerComponent>(map, out var layer) || layer.Network == null)
            return null;

        var query = EntManager.EntityQueryEnumerator<WFOrbitLayerComponent>();
        while (query.MoveNext(out var candidate))
        {
            if (candidate.Network == layer.Network)
                return candidate;
        }
        return null;
    }

    /// <summary>Draws sampled planet terrain under the radar, resampled every half second.</summary>
    private void DrawWfTerrain(DrawingHandleScreen handle, Matrix3x2 worldToView, EntityUid? map)
    {
        if (!ShowPlanetTerrain)
        {
            ReportWfTerrain("Terrain disabled by toggle.");
            return;
        }
        var orbit = GetWfTerrainRecipe(map);
        if (orbit == null || orbit.RadarLayers.Count == 0)
        {
            ReportWfTerrain($"No terrain recipe: map={map}, orbit={orbit != null}, layers={orbit?.RadarLayers.Count}.");
            return;
        }
        if (!Matrix3x2.Invert(worldToView, out var viewToWorld))
        {
            ReportWfTerrain($"Invalid radar transform: map={map}.");
            return;
        }

        if (_wfTerrainMap != map || _wfRadarTime.RealTime >= _wfTerrainRefresh)
        {
            _wfTerrainMap = map;
            _wfTerrainRefresh = _wfRadarTime.RealTime + TimeSpan.FromSeconds(0.5);
            var a = Vector2.Transform(Vector2.Zero, viewToWorld);
            var b = Vector2.Transform(new Vector2(PixelSize.X, 0), viewToWorld);
            var c = Vector2.Transform(new Vector2(0, PixelSize.Y), viewToWorld);
            var d = Vector2.Transform((Vector2) PixelSize, viewToWorld);
            var min = Vector2.Min(Vector2.Min(a, b), Vector2.Min(c, d));
            var max = Vector2.Max(Vector2.Max(a, b), Vector2.Max(c, d));
            var extent = MathF.Max(max.X - min.X, max.Y - min.Y);
            if (!float.IsFinite(extent) || extent <= 0)
                return;

            // About 50x50 world-aligned samples at any zoom; the extra cell covers movement between refreshes.
            var step = MathF.Max(1, MathF.Pow(2, MathF.Ceiling(MathF.Log2(extent / 48))));
            min = new Vector2(MathF.Floor(min.X / step) - 1, MathF.Floor(min.Y / step) - 1) * step;
            max += new Vector2(step);
            var groups = new List<Vector2>[WfTerrainPalette.Length];
            for (var i = 0; i < groups.Length; i++)
                groups[i] = new List<Vector2>();
            var sampler = EntManager.System<WFPlanetRadarSystem>();
            for (var ix = 0; ix < Math.Min(52, Math.Ceiling((max.X - min.X) / step)); ix++)
            for (var iy = 0; iy < Math.Min(52, Math.Ceiling((max.Y - min.Y) / step)); iy++)
            {
                var x = min.X + ix * step;
                var y = min.Y + iy * step;
                var sample = sampler.Sample(orbit, new Vector2(x + step / 2, y + step / 2));
                if (sample is not { } tile)
                    continue;
                var feature = tile.IsEmpty ? null : sampler.SampleFeature(orbit, new Vector2(x + step / 2, y + step / 2));
                var colour = tile.IsEmpty ? 5 : WfTerrainFeatureColour(feature) ?? WfTerrainColour(tile.TypeId);
                var vertices = groups[colour];
                vertices.Add(new Vector2(x, y));
                vertices.Add(new Vector2(x + step, y));
                vertices.Add(new Vector2(x + step, y + step));
                vertices.Add(new Vector2(x, y));
                vertices.Add(new Vector2(x + step, y + step));
                vertices.Add(new Vector2(x, y + step));
            }
            for (var i = 0; i < groups.Length; i++)
                _wfTerrainVertices[i] = groups[i].ToArray();
            ReportWfTerrain($"Terrain ready: map={map}, layers={orbit.RadarLayers.Count}.");
        }

        // Batched filled rects under one view transform, so panning stays smooth between samples.
        var previousTransform = handle.GetTransform();
        var terrainTransform = worldToView * previousTransform;
        handle.SetTransform(terrainTransform);
        try
        {
            for (var i = 0; i < _wfTerrainVertices.Length; i++)
            {
                var vertices = _wfTerrainVertices[i];
                if (vertices == null)
                    continue;
                for (var j = 0; j < vertices.Length; j += 6)
                    handle.DrawRect(new UIBox2(vertices[j], vertices[j + 2]), WfTerrainPalette[i]);
            }
        }
        finally
        {
            handle.SetTransform(previousTransform);
        }
    }

    private static int? WfTerrainFeatureColour(string? feature)
    {
        if (feature == null)
            return null;
        var id = feature.ToLowerInvariant();
        if (id.Contains("bloodriver")) return 10;
        if (id.Contains("fleshtree") || id.Contains("fleshpolyp")) return 11;
        if (id.Contains("wallmeat") || id.Contains("fleshblocker")) return 11;
        if (id.Contains("lava")) return 6;
        if (id.Contains("water")) return 1;
        if (id.Contains("plasma")) return 9;
        if (id.Contains("rock") || id.Contains("planetmapore") || id.Contains("boulder") || id.Contains("crystal")) return 7;
        if (id.Contains("tree") || id.Contains("bush")) return 8;
        return null;
    }

    private int WfTerrainColour(int tile)
    {
        if (_wfTerrainColours.TryGetValue(tile, out var colour))
            return colour;
        var id = _wfRadarTiles[tile].ID.ToLowerInvariant();
        colour = id.Contains("flesh") ? 10 : id.Contains("water") || id.Contains("ocean") || id.Contains("riverbed") ? 1
            : id.Contains("snow") || id.Contains("ice") ? 3
            : id.Contains("sand") || id.Contains("dirt") || id.Contains("mud") ? 2
            : id.Contains("grass") || id.Contains("jungle") ? 0 : 4;
        _wfTerrainColours[tile] = colour;
        return colour;
    }
}
