using System.Numerics;
using Content.Client._WF.PlanetSignals;
using Content.Shared._FarHorizons.StarSystem;
using Content.Shared._FarHorizons.StarSystem.Helpers;
using Content.Shared._WF.PlanetCargoHubs;
using Content.Shared._WF.Planets;
using Content.Shared._WF.PlanetScan;
using Content.Shared._WF.PlanetSignals;
using Robust.Client.Graphics;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Client.Shuttles.UI;

public partial class ShuttleNavControl
{
    private static readonly Color WfSiteColor = Color.FromHex("#ffb347");
    private static readonly Color WfVesselColor = Color.FromHex("#9fc3ff");
    private static readonly Color WfPersonColor = Color.FromHex("#7fe0ff");
    private static readonly Color WfPersonHurtColor = Color.FromHex("#ff4b4b");
    private static readonly Color WfExpeditionColor = Color.FromHex("#3fe05a");

    /// <summary>Where each world was drawn this frame, in pixels: its blip, the circle round it and its name.</summary>
    private readonly List<(EntityUid Body, Vector2 Centre, float Radius, UIBox2 Label)> _wfPlanetHits = new();

    /// <summary>The cargo depots on the world whose tag is being drawn, by site name.</summary>
    private readonly List<string> _wfDepots = new();

    private float _wfViewScale = 1f;
    private Vector2? _wfPressedAt;
    private WFPlanetSignalsWindow? _wfSignalsWindow;

    /// <summary>
    /// On a world's layers, marks its sites, the vessels on its other layers and the people a medical tracker gives
    /// away, wherever they are; the ship's own expedition site is
    /// marked in green, with an arrow at the rim while it is off the scope.
    /// </summary>
    private void DrawWfPlanetSignals(DrawingHandleScreen handle, Matrix3x2 worldToView, TransformComponent xform)
    {
        _wfPlanetHits.Clear();
        _wfViewScale = MathF.Sqrt(worldToView.M11 * worldToView.M11 + worldToView.M12 * worldToView.M12);

        if (!ShowIFF || xform.MapUid is not { } mapUid || WfSignalsOfLayer(mapUid) is not { } signals)
            return;

        var map = EntManager.GetNetEntity(mapUid);
        var ours = WfOurGrid();
        var centre = MidPointVector;
        var rim = MathF.Min(PixelSize.X, PixelSize.Y) / 2f - 20f * UIScale;
        var size = 5f * UIScale;

        foreach (var signal in signals.Signals)
        {
            if (signal.Kind == WFPlanetSignalKind.Vessel && signal.Map == map)
                continue;

            var mine = ours != null && signal.Expedition == ours;
            // The planet map marks sites by kind; only the ship's own expedition site keeps its marker here.
            if (signal.Kind == WFPlanetSignalKind.Site && !mine && _wfMapDrewSites)
                continue;

            var point = Vector2.Transform(new Vector2(signal.Position.X + 0.5f, signal.Position.Y + 0.5f), worldToView);
            var offset = point - centre;

            if (offset.Length() > rim)
            {
                if (!mine)
                    continue;

                // Off the scope: an arrow at the rim, pointing the way, with the distance.
                var direction = offset / offset.Length();
                var tip = centre + direction * rim;
                var side = new Vector2(-direction.Y, direction.X) * size;
                handle.DrawLine(tip, tip - direction * size * 2f + side, WfExpeditionColor);
                handle.DrawLine(tip, tip - direction * size * 2f - side, WfExpeditionColor);
                var distance = offset.Length() / MathF.Max(_wfViewScale, 0.0001f);
                var arrowLabel = Loc.GetString("wf-planet-signals-marker-distance", ("distance", distance.ToString("0")));
                var arrowSize = handle.GetDimensions(Font, arrowLabel, UIScale * 0.8f);
                handle.DrawString(Font, tip - direction * size * 4f - arrowSize / 2f, arrowLabel, UIScale * 0.8f, WfExpeditionColor);
                continue;
            }

            var color = mine ? WfExpeditionColor : signal.Kind switch
            {
                WFPlanetSignalKind.Site => WFPlanetCargoHubSites.IsHub(signal.Name) ? WFPlanetCargoHubSites.Colour : WfSiteColor,
                WFPlanetSignalKind.Person => signal.Condition == WFPlanetSignalCondition.Alive ? WfPersonColor : WfPersonHurtColor,
                _ => WfVesselColor,
            };
            if (signal.Kind == WFPlanetSignalKind.Person)
            {
                handle.DrawLine(point - new Vector2(size, 0f), point + new Vector2(size, 0f), color);
                handle.DrawLine(point - new Vector2(0f, size), point + new Vector2(0f, size), color);
            }
            else if (signal.Kind == WFPlanetSignalKind.Site)
            {
                var corner = mine ? size * 1.6f : size;
                handle.DrawLine(point + new Vector2(0f, -corner), point + new Vector2(corner, 0f), color);
                handle.DrawLine(point + new Vector2(corner, 0f), point + new Vector2(0f, corner), color);
                handle.DrawLine(point + new Vector2(0f, corner), point + new Vector2(-corner, 0f), color);
                handle.DrawLine(point + new Vector2(-corner, 0f), point + new Vector2(0f, -corner), color);
            }
            else
            {
                handle.DrawRect(UIBox2.FromDimensions(point - new Vector2(size, size), new Vector2(size, size) * 2f), color, false);
            }

            var label = signal.Kind == WFPlanetSignalKind.Person
                ? Loc.GetString("wf-planet-signals-marker-person", ("name", signal.Name), ("condition", WFPlanetSignalsWindow.ConditionOf(signal)))
                : signal.Kind == WFPlanetSignalKind.Site
                ? mine
                    ? Loc.GetString("wf-planet-signals-marker-expedition", ("site", WFPlanetSignalsWindow.NameOf(signal)))
                    : WFPlanetSignalsWindow.NameOf(signal)
                : Loc.GetString("wf-planet-signals-marker-vessel", ("name", WFPlanetSignalsWindow.NameOf(signal)),
                    ("layer", WFPlanetSignalsWindow.LayerOf(signal)));
            var labelSize = handle.GetDimensions(Font, label, UIScale * 0.8f);
            handle.DrawString(Font, point + new Vector2(size * 2f, -labelSize.Y / 2f), label, UIScale * 0.8f, color);
        }
    }

    /// <summary>Over a world's name on the radar out in space: how many vessels, sites and people it holds, and whether the ship's expedition is there.</summary>
    private void WfDrawPlanetSignalTag(DrawingHandleScreen handle, EntityUid body, Vector2 uiPosition, Vector2 labelOffset, string label)
    {
        if (!EntManager.TryGetComponent<WFPlanetSignalsComponent>(body, out var signals))
            return;

        var labelAt = (uiPosition + labelOffset) * UIScale;
        var labelSize = handle.GetDimensions(Font, label, UIScale * 0.9f);
        _wfPlanetHits.Add((body, uiPosition * UIScale, MathF.Max(16f * UIScale, WfPlanetRadius(body)), UIBox2.FromDimensions(labelAt, labelSize)));

        int vessels = 0, sites = 0;
        var ours = WfOurGrid();
        var mine = false;
        _wfDepots.Clear();
        foreach (var signal in signals.Signals)
        {
            if (signal.Kind == WFPlanetSignalKind.Vessel)
                vessels++;
            else if (signal.Kind == WFPlanetSignalKind.Site)
            {
                sites++;
                if (WFPlanetCargoHubSites.IsHub(signal.Name))
                    _wfDepots.Add(signal.Name);
            }
            mine |= ours != null && signal.Expedition == ours;
        }

        if (vessels == 0 && sites == 0 && signals.People == 0)
            return;

        // Stacked upward from the name, so the detailed lines below it keep their place.
        var at = labelAt;
        var tag = Loc.GetString("wf-planet-signals-tag", ("vessels", vessels), ("sites", sites), ("people", signals.People));
        at.Y -= handle.GetDimensions(Font, tag, UIScale * 0.7f).Y;
        handle.DrawString(Font, at, tag, UIScale * 0.7f, WfSiteColor);

        // A cargo depot on the world is named over it, in the depots' colour.
        foreach (var depot in _wfDepots)
        {
            var line = Loc.GetString("wf-planet-signals-tag-depot", ("depot", Loc.GetString(depot)));
            at.Y -= handle.GetDimensions(Font, line, UIScale * 0.8f).Y;
            handle.DrawString(Font, at, line, UIScale * 0.8f, WFPlanetCargoHubSites.Colour);
        }

        if (!mine)
            return;

        var expedition = Loc.GetString("wf-planet-signals-tag-expedition");
        at.Y -= handle.GetDimensions(Font, expedition, UIScale * 0.7f).Y;
        handle.DrawString(Font, at, expedition, UIScale * 0.7f, WfExpeditionColor);
    }

    /// <summary>
    /// Opens the signal list of the world under a click. False when nothing was hit, the press was a drag, this radar's
    /// clicks aim guns, or a grid is under the click: a ship or drone over a world's circle is what was clicked.
    /// </summary>
    private bool WfTryOpenPlanetSignals(Vector2 pixel, Vector2 relative)
    {
        var pressed = _wfPressedAt;
        _wfPressedAt = null;
        if (OnRadarClick != null || pressed is not { } down || (down - pixel).Length() > 6f * UIScale
            || WfGridUnderClick(relative))
            return false;

        foreach (var (body, centre, radius, label) in _wfPlanetHits)
        {
            if ((pixel - centre).Length() > radius && !label.Contains(pixel))
                continue;

            _wfSignalsWindow ??= new WFPlanetSignalsWindow();
            _wfSignalsWindow.Show(body, WfOurGrid());
            return true;
        }

        return false;
    }

    private List<Entity<MapGridComponent>> _wfClickGrids = new();

    /// <summary>Whether a grid lies within a few pixels of a click, a planet's own ground aside.</summary>
    private bool WfGridUnderClick(Vector2 relative)
    {
        var coords = EntManager.System<SharedTransformSystem>().ToMapCoordinates(GetMouseEntityCoordinates(relative));
        if (coords.MapId == MapId.Nullspace)
            return false;

        var reach = 8f * UIScale / MathF.Max(_wfViewScale, 0.0001f);
        _wfClickGrids.Clear();
        EntManager.System<SharedMapSystem>().FindGridsIntersecting(coords.MapId, Box2.CenteredAround(coords.Position, new Vector2(reach * 2f)), ref _wfClickGrids);
        foreach (var grid in _wfClickGrids)
        {
            if (!EntManager.HasComponent<MapComponent>(grid.Owner))
                return true;
        }

        return false;
    }

    /// <summary>The signal list of the world a planet layer belongs to.</summary>
    private WFPlanetSignalsComponent? WfSignalsOfLayer(EntityUid map)
    {
        var network = EntManager.TryGetComponent<WFOrbitLayerComponent>(map, out var orbit) ? orbit.Network
            : EntManager.TryGetComponent<WFPlanetLayerComponent>(map, out var layer) ? layer.Network
            : null;
        if (network == null)
            return null;

        var query = EntManager.AllEntityQueryEnumerator<WFSectorPlanetComponent, WFPlanetSignalsComponent>();
        while (query.MoveNext(out _, out var sector, out var signals))
        {
            if (sector.Network == network)
                return signals;
        }

        return null;
    }

    /// <summary>The drawn radius of a world's circle, in pixels.</summary>
    private float WfPlanetRadius(EntityUid body)
    {
        var xform = EntManager.GetComponent<TransformComponent>(body);
        if (!EntManager.TryGetComponent<StarSystemMapComponent>(xform.MapUid, out var system) || system.StarSystem == null)
            return 0f;

        var position = xform.LocalPosition;
        foreach (var planet in system.StarSystem.Planets)
        {
            if ((planet.Position - position).LengthSquared() < 1f)
                return WFPlanetScanReader.ScopeRadius(planet, WfSurfaceOf(body)) * _wfViewScale;
        }

        return 0f;
    }

    private WFPlanetSurfacePrototype? WfSurfaceOf(EntityUid body)
    {
        return EntManager.TryGetComponent<WFSectorPlanetComponent>(body, out var sector) && sector.Surface is { } id &&
            IoCManager.Resolve<IPrototypeManager>().TryIndex(id, out var surface)
            ? surface
            : null;
    }

    /// <summary>The ship this radar is on, as the signals name it.</summary>
    private NetEntity? WfOurGrid()
    {
        return EntManager.TryGetComponent<TransformComponent>(_consoleEntity, out var xform) && xform.GridUid is { } grid
            ? EntManager.GetNetEntity(grid)
            : null;
    }
}
