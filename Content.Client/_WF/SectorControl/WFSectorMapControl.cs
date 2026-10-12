using System.Globalization;
using System.Numerics;
using Content.Client._WF.Encounters;
using Content.Shared._WF.Encounters;
using Content.Shared._WF.SectorControl;
using Content.Shared.Ghost;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Shared;
using Robust.Shared.Configuration;
using Robust.Shared.Input;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client._WF.SectorControl;

/// <summary>The sector from above, zoomable out to 60 km either side of centre: the cell grid, territory, the grids and encounters a shuttle console's MAP tab lists as blips, and the player's own ship.</summary>
public sealed class WFSectorMapControl : Control
{
    [Dependency] private IEntityManager _entManager = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IConfigurationManager _cfg = default!;

    /// <summary>The narrowest half-width the view zooms in to, in metres.</summary>
    public const float MinRange = 1500f;

    /// <summary>The widest half-width the view zooms out to, in metres.</summary>
    public const float MaxRange = 60000f;

    /// <summary>The space left around the grids and territory when the view is fitted to them.</summary>
    public const float FitMargin = 3000f;

    /// <summary>How long the cursor must rest over one cell or blip before the hover readout shows.</summary>
    public static readonly TimeSpan ReadoutDelay = TimeSpan.FromSeconds(0.5);

    private const float ZoomStep = 1.25f;

    /// <summary>How far the cursor may drift, in UI pixels, and still count as resting.</summary>
    private const float RestDrift = 4f;

    /// <summary>How narrow a cell may get on screen, in pixels, before the grid is no longer outlined.</summary>
    private const float GridPixels = 6f;

    /// <summary>How far from a blip, in UI pixels, the cursor still points at it.</summary>
    private const float BlipReach = 8f;

    /// <summary>The radius of a grid's blip, in UI pixels.</summary>
    private const float BlipRadius = 4f;

    /// <summary>How far from the origin the centre can be dragged.</summary>
    private const float CentreLimit = MaxRange * 1.5f;

    private static readonly Color BackingColor = Color.FromHex("#0b0e12");
    private static readonly Color GridColor = Color.White.WithAlpha(0.08f);
    private static readonly Color CardColor = Color.FromHex("#c8d4e0");
    private static readonly Color DimColor = Color.FromHex("#8a96a3");
    private static readonly Color ShadowColor = Color.Black.WithAlpha(0.8f);
    private static readonly ResPath MarkerGlyphs = new("/Textures/_WF/Encounters/markers.rsi");

    private readonly WFSectorOverlay _overlay = new();
    private readonly Font _font;
    private readonly List<UIBox2> _placedLabels = new();
    private readonly Dictionary<WFEncounterIcon, Texture> _glyphs = new();
    private readonly List<WFSectorLandmarkView> _landmarks = new();
    private readonly List<WFSectorEncounterView> _encounters = new();
    private readonly List<WFSectorReadoutLine> _readout = new();
    private readonly HashSet<NetEntity> _listed = new();
    private readonly Vector2[] _triangle = new Vector2[3];
    private readonly Vector2[] _diamond = new Vector2[4];

    private Vector2 _centre;
    private float _range = 20000f;

    /// <summary>The map the view was last fitted to; a new map, or Nullspace, fits it again.</summary>
    private MapId _fitted = MapId.Nullspace;

    private bool _panning;
    private Vector2 _lastPointer;
    private Vector2? _hover;
    private TimeSpan? _lastClick;
    private Vector2 _lastClickPosition;

    /// <summary>Where, over which cell and blip and since when the cursor has been resting.</summary>
    private Vector2 _restPoint;
    private WFSectorCell? _restCell;
    private WFSectorBlip? _restBlip;
    private TimeSpan _restSince;

    /// <summary>The world point at the middle of the control.</summary>
    public Vector2 Centre => _centre;

    /// <summary>The half-width of the view, in metres.</summary>
    public float Range => _range;

    /// <summary>Pixels per metre at the current zoom.</summary>
    public float PixelsPerMetre => PixelSize.X > 0 ? PixelSize.X / 2f / _range : 0f;

    /// <summary>Whether the cursor has rested over one cell and blip for <see cref="ReadoutDelay"/> and the view is not being panned.</summary>
    public bool ReadoutShown => _hover != null && !_panning && _timing.RealTime - _restSince >= ReadoutDelay;

    /// <summary>The blip under the cursor, while it is over the map and not panning.</summary>
    public WFSectorBlip? HoveredBlip => _hover is { } hover && !_panning ? BlipAt(hover) : null;

    public WFSectorMapControl()
    {
        IoCManager.InjectDependencies(this);
        RectClipContent = true;
        MouseFilter = MouseFilterMode.Stop;
        var cache = IoCManager.Resolve<IResourceCache>();
        _font = new VectorFont(cache.GetResource<FontResource>("/Fonts/NotoSans/NotoSans-Regular.ttf"), 12);
    }

    /// <summary>The territory shown: the player's map, else any map the client has, the sector map first.</summary>
    public bool TryGetView(out WFSectorMapView view)
    {
        var system = _entManager.System<WFSectorClientSystem>();
        if (_player.LocalEntity is { } player
            && _entManager.TryGetComponent(player, out TransformComponent? own)
            && system.TryGetMap(own.MapID, out view))
        {
            return true;
        }

        return system.TryGetAnyMap(out view);
    }

    /// <summary>Map space to the control's pixels: <c>(world - centre) * scale</c>, Y flipped, about the midpoint.</summary>
    public Matrix3x2 WorldToView()
    {
        var scale = PixelsPerMetre;
        return Matrix3x2.CreateTranslation(-_centre)
               * Matrix3x2.CreateScale(scale, -scale)
               * Matrix3x2.CreateTranslation((Vector2) PixelSize / 2f);
    }

    /// <summary>The map position under a pixel of the control.</summary>
    public Vector2 ViewToWorld(Vector2 pixel)
    {
        var scale = PixelsPerMetre;
        if (scale <= 0f)
            return _centre;

        var offset = (pixel - (Vector2) PixelSize / 2f) / scale;
        return _centre + new Vector2(offset.X, -offset.Y);
    }

    /// <summary>Fits the view to the grids and territory again.</summary>
    public void ShowWholeSector()
    {
        if (PixelSize.X > 0 && PixelSize.Y > 0 && TryGetView(out var view))
            Fit(view);
        else
            _fitted = MapId.Nullspace;
    }

    /// <summary>Centres the view on the player's own grid, or on the player when they are not aboard one (a ghost always); false if neither is on the map shown.</summary>
    public bool CentreOnPlayer()
    {
        if (!TryGetView(out var view) || !TryGetOwnPosition(view.Map, out var position))
            return false;

        SetCentre(position);
        return true;
    }

    /// <summary>Zooms by a factor, above one to zoom out, keeping the map point under a pixel where it is.</summary>
    public void ZoomAt(Vector2 pixel, float factor)
    {
        if (PixelSize.X <= 0)
            return;

        var anchor = ViewToWorld(pixel);
        _range = Math.Clamp(_range * factor, MinRange, MaxRange);
        var offset = (pixel - (Vector2) PixelSize / 2f) / PixelsPerMetre;
        SetCentre(anchor - new Vector2(offset.X, -offset.Y));
    }

    /// <summary>The hover line for a map position: the cell's callsign, its distance from the origin and who holds or contests it.</summary>
    public string Readout(WFSectorMapView view, Vector2 world)
    {
        var cell = WFSectorHex.CellOf(world, view.CellSize);
        var callsign = WFSectorHex.Callsign(cell);
        var distance = (world.Length() / 1000f).ToString("0.0", CultureInfo.InvariantCulture);
        if (view.Disputes.TryGetValue(cell, out var disputing))
        {
            return Loc.GetString("wf-sector-map-hover-disputed",
                ("callsign", callsign), ("distance", distance), ("faction", view.Names[disputing]));
        }

        foreach (var retake in view.Retakes)
        {
            if (retake.Cell != cell || !view.Owners.TryGetValue(cell, out var holder))
                continue;

            return Loc.GetString("wf-sector-map-hover-retaking", ("callsign", callsign), ("distance", distance),
                ("faction", view.Names[holder]), ("time", WFSectorOverlay.FormatRemaining(retake.Remaining(_timing.CurTime))));
        }

        foreach (var contest in view.Contests)
        {
            if (contest.Cell != cell)
                continue;

            var remaining = WFSectorOverlay.FormatRemaining(contest.Remaining(_timing.CurTime));
            return Loc.GetString("wf-sector-map-hover-contested",
                ("callsign", callsign), ("distance", distance), ("faction", view.Names[contest.Faction]), ("time", remaining));
        }

        foreach (var clearing in view.Clearing)
        {
            if (clearing.Cell != cell)
                continue;

            return Loc.GetString("wf-sector-map-hover-clearing", ("callsign", callsign), ("distance", distance),
                ("time", WFSectorOverlay.FormatRemaining(clearing.Remaining(_timing.CurTime))));
        }

        if (view.Owners.TryGetValue(cell, out var owner))
        {
            return Loc.GetString("wf-sector-map-hover-held",
                ("callsign", callsign), ("distance", distance), ("faction", view.Names[owner]));
        }

        return Loc.GetString("wf-sector-map-hover-open", ("callsign", callsign), ("distance", distance));
    }

    /// <summary>
    /// Fills a blip's card, one line each: its name, what it is beside a swatch of its colour, its range and bearing from
    /// the player (or the sector core), and for a grid the cell it is in, who holds it and any contest's countdown.
    /// </summary>
    public void Card(WFSectorMapView view, WFSectorBlip blip, List<WFSectorReadoutLine> into)
    {
        into.Clear();
        into.Add(new WFSectorReadoutLine(blip.Name, Color.White, null));
        into.Add(new WFSectorReadoutLine(Loc.GetString(KindKey(blip)), CardColor, blip.Color));

        var located = TryGetOwnPosition(view.Map, out var from);
        var distance = (Vector2.Distance(from, blip.Position) / 1000f).ToString("0.0", CultureInfo.InvariantCulture);
        into.Add(new WFSectorReadoutLine(Loc.GetString(located ? "wf-sector-map-card-range" : "wf-sector-map-card-range-core",
            ("distance", distance), ("bearing", Bearing(from, blip.Position))), CardColor, null));

        if (blip.Kind == WFSectorBlipKind.Encounter)
            return;

        var cell = WFSectorHex.CellOf(blip.Position, view.CellSize);
        var callsign = WFSectorHex.Callsign(cell);
        into.Add(view.Owners.TryGetValue(cell, out var owner)
            ? new WFSectorReadoutLine(Loc.GetString("wf-sector-map-card-held", ("callsign", callsign), ("faction", view.Names[owner])), CardColor, null)
            : new WFSectorReadoutLine(Loc.GetString("wf-sector-map-card-open", ("callsign", callsign)), CardColor, null));

        foreach (var contest in view.Contests)
        {
            if (contest.Cell != cell)
                continue;

            var remaining = WFSectorOverlay.FormatRemaining(contest.Remaining(_timing.CurTime));
            into.Add(new WFSectorReadoutLine(Loc.GetString("wf-sector-map-card-contested",
                ("faction", view.Names[contest.Faction]), ("time", remaining)), CardColor, null));
            break;
        }

        foreach (var retake in view.Retakes)
        {
            if (retake.Cell != cell)
                continue;

            into.Add(new WFSectorReadoutLine(Loc.GetString("wf-sector-map-card-retaking",
                ("time", WFSectorOverlay.FormatRemaining(retake.Remaining(_timing.CurTime)))), CardColor, null));
            break;
        }

        foreach (var clearing in view.Clearing)
        {
            if (clearing.Cell != cell)
                continue;

            into.Add(new WFSectorReadoutLine(Loc.GetString("wf-sector-map-card-clearing",
                ("time", WFSectorOverlay.FormatRemaining(clearing.Remaining(_timing.CurTime)))), CardColor, null));
            break;
        }

        if (view.Disputes.TryGetValue(cell, out var disputing))
        {
            into.Add(new WFSectorReadoutLine(Loc.GetString("wf-sector-map-card-disputed",
                ("faction", view.Names[disputing])), CardColor, null));
        }
    }

    /// <summary>The lines the readout shows now: the card of the blip under the cursor, else the cell's line; none until the cursor has rested.</summary>
    public IReadOnlyList<WFSectorReadoutLine> ShownReadout()
    {
        _readout.Clear();
        if (!ReadoutShown
            || _hover is not { } hover
            || !new UIBox2(Vector2.Zero, PixelSize).Contains(hover)
            || !TryGetView(out var view))
        {
            return _readout;
        }

        if (BlipAt(hover) is { } blip)
            Card(view, blip, _readout);
        else
            _readout.Add(new WFSectorReadoutLine(Readout(view, ViewToWorld(hover)), Color.White, null));

        return _readout;
    }

    /// <summary>The compass bearing from one point to another as three digits, north 000, clockwise.</summary>
    public static string Bearing(Vector2 from, Vector2 to)
    {
        var delta = to - from;
        var degrees = (int) MathF.Round(MathF.Atan2(delta.X, delta.Y) * 180f / MathF.PI);
        return ((degrees % 360 + 360) % 360).ToString("000", CultureInfo.InvariantCulture);
    }

    /// <summary>The blip on screen nearest a pixel of the control and within reach of it, a named grid or an encounter; none over empty space.</summary>
    public WFSectorBlip? BlipAt(Vector2 pixel)
    {
        if (PixelSize.X <= 0 || !TryGetView(out var view))
            return null;

        CollectBlips(view.Map);
        return Nearest(pixel, WorldToView(), new UIBox2(Vector2.Zero, PixelSize));
    }

    /// <summary>Whether a blip's name is printed: only the hovered blip's, at any zoom.</summary>
    public bool LabelShown(WFSectorBlip blip)
    {
        return WFSectorBlip.Same(HoveredBlip, blip);
    }

    /// <summary>Where the player's own grid is on a map and which way it faces, when the player is aboard one there; a ghost is never aboard, even over a grid.</summary>
    public bool TryGetOwnShip(MapId map, out Vector2 position, out Angle heading)
    {
        position = default;
        heading = default;
        if (OwnGrid(map) is not { } grid)
            return false;

        var transform = _entManager.System<SharedTransformSystem>();
        var (worldPosition, worldRotation) = transform.GetWorldPositionRotation(grid);
        position = _entManager.TryGetComponent(grid, out PhysicsComponent? body)
            ? transform.ToMapCoordinates(new EntityCoordinates(grid, body.LocalCenter)).Position
            : worldPosition;
        heading = worldRotation;
        return true;
    }

    /// <summary>Where the player is on a map: their own grid when aboard one, else where they stand or float; the origin and false when they are not on it.</summary>
    public bool TryGetOwnPosition(MapId map, out Vector2 position)
    {
        if (TryGetOwnShip(map, out position, out _))
            return true;

        if (_player.LocalEntity is { } player
            && _entManager.TryGetComponent(player, out TransformComponent? xform)
            && xform.MapID == map)
        {
            position = _entManager.System<SharedTransformSystem>().GetWorldPosition(xform);
            return true;
        }

        position = Vector2.Zero;
        return false;
    }

    /// <summary>Fills a list with the grids on a map that the sector map names, as the MAP tab lists them without a scan, leaving out the player's own ship.</summary>
    public void CollectLandmarks(MapId map, List<WFSectorLandmarkView> into)
    {
        _entManager.System<WFSectorClientSystem>().CollectLandmarks(map, OwnGrid(map), into);
    }

    /// <summary>Fills a list with one marker per encounter on a map, at its first ship and in that ship's side colour, as the MAP tab lists them.</summary>
    public void CollectEncounters(MapId map, IReadOnlyList<WFEncounterMarker> markers, List<WFSectorEncounterView> into)
    {
        into.Clear();
        _listed.Clear();
        var system = _entManager.System<WFEncounterMarkerClientSystem>();
        foreach (var marker in markers)
        {
            if (marker.Map != map || !_listed.Add(marker.Encounter))
                continue;

            var color = marker.Color ?? WFEncounterColors.Category(marker.Category);
            into.Add(new WFSectorEncounterView(marker.Encounter, marker.Name, marker.Category, color, system.GetPosition(marker)));
        }
    }

    /// <summary>The grid the player is aboard on a map; none for a ghost, even over a grid.</summary>
    private EntityUid? OwnGrid(MapId map)
    {
        if (_player.LocalEntity is not { } player
            || _entManager.HasComponent<GhostComponent>(player)
            || !_entManager.TryGetComponent(player, out TransformComponent? xform)
            || xform.GridUid is not { } grid
            || !_entManager.TryGetComponent(grid, out TransformComponent? gridXform)
            || gridXform.MapID != map
            || grid == gridXform.MapUid)
        {
            return null;
        }

        return grid;
    }

    /// <summary>Gathers the landmarks and encounters of a map into the lists the blips are drawn and found from.</summary>
    private void CollectBlips(MapId map)
    {
        CollectLandmarks(map, _landmarks);
        CollectEncounters(map, _entManager.System<WFEncounterMarkerClientSystem>().Markers, _encounters);
    }

    /// <summary>The blip in the gathered lists nearest a pixel, on the control and within reach.</summary>
    private WFSectorBlip? Nearest(Vector2 pixel, in Matrix3x2 worldToView, UIBox2 bounds)
    {
        WFSectorBlip? best = null;
        var reach = BlipReach * UIScale;
        var bestDistance = reach * reach;
        foreach (var landmark in _landmarks)
        {
            var point = Vector2.Transform(landmark.Position, worldToView);
            var distance = (point - pixel).LengthSquared();
            if (distance > bestDistance || !bounds.Contains(point))
                continue;

            bestDistance = distance;
            best = WFSectorBlip.Of(landmark);
        }

        foreach (var encounter in _encounters)
        {
            var point = Vector2.Transform(encounter.Position, worldToView);
            var distance = (point - pixel).LengthSquared();
            if (distance > bestDistance || !bounds.Contains(point))
                continue;

            bestDistance = distance;
            best = WFSectorBlip.Of(encounter);
        }

        return best;
    }

    /// <summary>The Fluent key for what a blip is: station, ship or the encounter's category.</summary>
    private static string KindKey(WFSectorBlip blip)
    {
        return blip.Kind switch
        {
            WFSectorBlipKind.Station => "wf-sector-map-kind-station",
            WFSectorBlipKind.Ship => "wf-sector-map-kind-ship",
            _ => blip.Category switch
            {
                WFEncounterCategory.Patrol => "wf-sector-map-category-patrol",
                WFEncounterCategory.Threat => "wf-sector-map-category-threat",
                WFEncounterCategory.Distress => "wf-sector-map-category-distress",
                _ => "wf-sector-map-category-traffic",
            },
        };
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        if (TryGetView(out var view))
            EnsureFitted(view);

        // The map, or a blip, can move under a still cursor.
        if (_hover is { } hover && !_panning && (CellAt(hover) != _restCell || !WFSectorBlip.Same(BlipAt(hover), _restBlip)))
            RestartRest(hover);
    }

    /// <summary>The cell under a pixel of the control, or none with no view of the map.</summary>
    private WFSectorCell? CellAt(Vector2 pixel)
    {
        return TryGetView(out var view) ? WFSectorHex.CellOf(ViewToWorld(pixel), view.CellSize) : null;
    }

    /// <summary>Starts the readout's delay again from a pixel, hiding the readout.</summary>
    private void RestartRest(Vector2 pixel)
    {
        _restPoint = pixel;
        _restCell = CellAt(pixel);
        _restBlip = BlipAt(pixel);
        _restSince = _timing.RealTime;
    }

    private void EnsureFitted(WFSectorMapView view)
    {
        if (_fitted != view.Map && PixelSize.X > 0 && PixelSize.Y > 0)
            Fit(view);
    }

    /// <summary>Centres on the named grids within reach, the player's ship and the territory, and zooms to show them all with a margin.</summary>
    private void Fit(WFSectorMapView view)
    {
        var box = view.Extent;
        CollectLandmarks(view.Map, _landmarks);
        foreach (var landmark in _landmarks)
        {
            // A grid past the widest zoom could never be shown with the rest.
            if (landmark.Position.Length() <= MaxRange)
                box = Grow(box, landmark.Position);
        }

        if (TryGetOwnShip(view.Map, out var own, out _))
            box = Grow(box, own);

        var bounds = box ?? new Box2(Vector2.Zero, Vector2.Zero);
        var aspect = (float) PixelSize.X / PixelSize.Y;
        var halfWidth = bounds.Width / 2f + FitMargin;
        var halfHeight = bounds.Height / 2f + FitMargin;
        _range = Math.Clamp(MathF.Max(halfWidth, halfHeight * aspect), MinRange, MaxRange);
        SetCentre(bounds.Center);
        _fitted = view.Map;
    }

    private static Box2 Grow(Box2? box, Vector2 point)
    {
        return box is { } current ? current.ExtendToContain(point) : new Box2(point, point);
    }

    private void SetCentre(Vector2 centre)
    {
        _centre = Vector2.Clamp(centre, new Vector2(-CentreLimit), new Vector2(CentreLimit));
    }

    /// <summary>The right button pans and the left button, double-clicked, centres on the player; a click of either hides the readout.</summary>
    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        base.KeyBindDown(args);
        if (args.Function != EngineKeyFunctions.UIClick && args.Function != EngineKeyFunctions.UIRightClick)
            return;

        var position = args.RelativePixelPosition;
        _hover = position;
        RestartRest(position);
        args.Handle();
        if (args.Function == EngineKeyFunctions.UIRightClick)
        {
            _lastPointer = position;
            _panning = true;
            return;
        }

        var now = _timing.RealTime;
        if (_lastClick is { } last
            && now - last <= TimeSpan.FromMilliseconds(_cfg.GetCVar(CVars.DoubleClickDelay))
            && (position - _lastClickPosition).Length() < _cfg.GetCVar(CVars.DoubleClickRange))
        {
            _lastClick = null;
            CentreOnPlayer();
            return;
        }

        _lastClick = now;
        _lastClickPosition = position;
    }

    protected override void KeyBindUp(GUIBoundKeyEventArgs args)
    {
        base.KeyBindUp(args);
        if (args.Function != EngineKeyFunctions.UIRightClick)
            return;

        _panning = false;
        _hover = args.RelativePixelPosition;
        RestartRest(args.RelativePixelPosition);
        args.Handle();
    }

    protected override void MouseMove(GUIMouseMoveEventArgs args)
    {
        base.MouseMove(args);
        var position = args.RelativePixelPosition;
        var entered = _hover == null;
        _hover = position;
        if (!_panning)
        {
            if (entered
                || CellAt(position) != _restCell
                || (position - _restPoint).Length() > RestDrift * UIScale
                || !WFSectorBlip.Same(BlipAt(position), _restBlip))
            {
                RestartRest(position);
            }

            return;
        }

        var delta = position - _lastPointer;
        _lastPointer = position;
        var scale = PixelsPerMetre;
        if (scale > 0f)
            SetCentre(_centre - new Vector2(delta.X, -delta.Y) / scale);
    }

    protected override void MouseExited()
    {
        base.MouseExited();
        _hover = null;
    }

    protected override void MouseWheel(GUIMouseWheelEventArgs args)
    {
        base.MouseWheel(args);
        if (args.Delta.Y == 0f)
            return;

        ZoomAt(args.RelativePixelPosition, MathF.Pow(ZoomStep, -args.Delta.Y));
        _hover = args.RelativePixelPosition;
        RestartRest(args.RelativePixelPosition);
        args.Handle();
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        var size = (Vector2) PixelSize;
        var bounds = new UIBox2(Vector2.Zero, size);
        handle.DrawRect(bounds, BackingColor);
        if (size.X <= 0f || size.Y <= 0f)
            return;

        if (!TryGetView(out var view))
        {
            var text = Loc.GetString("wf-sector-map-no-data");
            var dimensions = handle.GetDimensions(_font, text, UIScale);
            handle.DrawString(_font, (size - dimensions) / 2f, text, UIScale, DimColor);
            return;
        }

        EnsureFitted(view);
        var system = _entManager.System<WFSectorClientSystem>();
        var worldToView = WorldToView();
        var scale = PixelsPerMetre;
        DrawGrid(handle, view, worldToView, scale);
        _overlay.DrawFills(handle, view, worldToView);
        _overlay.DrawBorders(handle, view, worldToView);
        _overlay.DrawContests(handle, system, view, worldToView, _font, UIScale, bounds);
        _overlay.DrawRetakes(handle, system, view, worldToView, _font, UIScale, bounds);
        _overlay.DrawClearing(handle, system, view, worldToView, _font, UIScale, bounds);
        _overlay.DrawDisputes(handle, system, view, worldToView);
        _overlay.DrawCallsigns(handle, view.CellLabels, view.CellSize, worldToView, _font, UIScale, scale, bounds);
        DrawBlips(handle, view, worldToView, bounds);
        DrawOwnShip(handle, view.Map, worldToView);
        // The legend goes over the grids, so a label never runs across it.
        _overlay.DrawLegend(handle, system, _font, UIScale);
        DrawReadout(handle, bounds);
    }

    /// <summary>Faintly outlines every cell of the sector, unless a cell would be too narrow on screen to see.</summary>
    private void DrawGrid(DrawingHandleScreen handle, WFSectorMapView view, in Matrix3x2 worldToView, float scale)
    {
        if (view.Grid.Length == 0 || view.CellSize * MathF.Sqrt(3f) * scale < GridPixels * UIScale)
            return;

        _overlay.Draw(handle, DrawPrimitiveTopology.LineList, view.Grid, worldToView, GridColor);
    }

    /// <summary>
    /// A blip for each grid the MAP tab would list, then each encounter's glyph, then their names: all of them when zoomed
    /// in, else only the hovered one's, which is placed first; a name that would overlap one already printed is left out.
    /// </summary>
    private void DrawBlips(DrawingHandleScreen handle, WFSectorMapView view, in Matrix3x2 worldToView, UIBox2 bounds)
    {
        CollectBlips(view.Map);
        var hovered = _hover is { } hover && !_panning ? Nearest(hover, worldToView, bounds) : null;
        foreach (var landmark in _landmarks)
        {
            var point = Vector2.Transform(landmark.Position, worldToView);
            if (bounds.Contains(point))
                DrawLandmarkBlip(handle, point, landmark.Kind, landmark.Color);
        }

        var glyph = new Vector2(14f * UIScale);
        foreach (var encounter in _encounters)
        {
            var point = Vector2.Transform(encounter.Position, worldToView);
            if (bounds.Contains(point))
                handle.DrawTextureRect(Glyph(GlyphFor(encounter.Category)), UIBox2.FromDimensions(point - glyph / 2f, glyph), WFEncounterColors.Category(encounter.Category));
        }

        _placedLabels.Clear();
        if (hovered is { } shown)
            DrawBlipLabel(handle, shown, worldToView, bounds);
    }

    /// <summary>A grid's blip in its IFF colour: a ring round a dot for a station, a filled diamond for a ship.</summary>
    private void DrawLandmarkBlip(DrawingHandleScreen handle, Vector2 point, WFSectorBlipKind kind, Color color)
    {
        var radius = BlipRadius * UIScale;
        if (kind == WFSectorBlipKind.Station)
        {
            handle.DrawCircle(point, radius + 1.5f * UIScale, ShadowColor.WithAlpha(0.6f));
            handle.DrawCircle(point, radius, color, false);
            handle.DrawCircle(point, radius - UIScale, color, false);
            handle.DrawCircle(point, 1.5f * UIScale, color);
            return;
        }

        var tip = radius * 1.25f;
        _diamond[0] = point + new Vector2(0f, -tip);
        _diamond[1] = point + new Vector2(tip, 0f);
        _diamond[2] = point + new Vector2(0f, tip);
        _diamond[3] = point + new Vector2(-tip, 0f);
        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleFan, _diamond, color);
        for (var i = 0; i < 4; i++)
        {
            handle.DrawLine(_diamond[i], _diamond[(i + 1) % 4], ShadowColor);
        }
    }

    /// <summary>Prints a blip's name beside it: a grid's in its IFF colour, an encounter's in its category's.</summary>
    private void DrawBlipLabel(DrawingHandleScreen handle, WFSectorBlip blip, in Matrix3x2 worldToView, UIBox2 bounds)
    {
        var point = Vector2.Transform(blip.Position, worldToView);
        if (!bounds.Contains(point))
            return;

        if (blip.Kind == WFSectorBlipKind.Encounter)
            DrawLabel(handle, point, 10f * UIScale, blip.Name, WFEncounterColors.Category(blip.Category), bounds);
        else
            DrawLabel(handle, point, (BlipRadius + 3f) * UIScale, blip.Name, blip.Color, bounds);
    }

    /// <summary>Prints a name to the right of a point, or the left if there is no room, unless it would overlap a name already printed.</summary>
    private void DrawLabel(DrawingHandleScreen handle, Vector2 point, float gap, string text, Color color, UIBox2 bounds)
    {
        if (text.Length == 0)
            return;

        var labelScale = UIScale * 0.85f;
        var dimensions = handle.GetDimensions(_font, text, labelScale);
        var right = UIBox2.FromDimensions(point + new Vector2(gap, -dimensions.Y / 2f), dimensions);
        var left = UIBox2.FromDimensions(point - new Vector2(gap + dimensions.X, dimensions.Y / 2f), dimensions);
        UIBox2 box;
        if (LabelFits(right, bounds))
            box = right;
        else if (LabelFits(left, bounds))
            box = left;
        else
            return;

        _placedLabels.Add(box);
        handle.DrawString(_font, box.TopLeft + new Vector2(1f, 1f), text, labelScale, ShadowColor);
        handle.DrawString(_font, box.TopLeft, text, labelScale, color);
    }

    private bool LabelFits(UIBox2 box, UIBox2 bounds)
    {
        if (!bounds.Contains(box.TopLeft) || !bounds.Contains(box.BottomRight))
            return false;

        foreach (var placed in _placedLabels)
        {
            if (placed.Intersects(box))
                return false;
        }

        return true;
    }

    /// <summary>A category's own glyph, as the radar draws it.</summary>
    private static WFEncounterIcon GlyphFor(WFEncounterCategory category)
    {
        return category switch
        {
            WFEncounterCategory.Patrol => WFEncounterIcon.Shield,
            WFEncounterCategory.Threat => WFEncounterIcon.Skull,
            WFEncounterCategory.Distress => WFEncounterIcon.Cross,
            _ => WFEncounterIcon.Crate,
        };
    }

    /// <summary>The texture for a glyph, loaded once.</summary>
    private Texture Glyph(WFEncounterIcon icon)
    {
        if (!_glyphs.TryGetValue(icon, out var texture))
        {
            texture = _entManager.System<SpriteSystem>().Frame0(new SpriteSpecifier.Rsi(MarkerGlyphs, icon.ToString().ToLowerInvariant()));
            _glyphs[icon] = texture;
        }

        return texture;
    }

    /// <summary>A small white triangle on the player's own grid, pointing the way it faces.</summary>
    private void DrawOwnShip(DrawingHandleScreen handle, MapId map, in Matrix3x2 worldToView)
    {
        if (!TryGetOwnShip(map, out var position, out var heading))
        {
            DrawGhost(handle, map, worldToView);
            return;
        }

        var point = Vector2.Transform(position, worldToView);
        var forward = heading.RotateVec(new Vector2(0f, 1f));
        var direction = Vector2.Normalize(new Vector2(forward.X, -forward.Y));
        var side = new Vector2(-direction.Y, direction.X);
        var size = 7f * UIScale;
        _triangle[0] = point + direction * size;
        _triangle[1] = point - direction * size * 0.6f + side * size * 0.6f;
        _triangle[2] = point - direction * size * 0.6f - side * size * 0.6f;
        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, _triangle, Color.White);
        handle.DrawLine(_triangle[0], _triangle[1], ShadowColor);
        handle.DrawLine(_triangle[1], _triangle[2], ShadowColor);
        handle.DrawLine(_triangle[2], _triangle[0], ShadowColor);
    }

    /// <summary>A small white ring where the player's ghost is on a map.</summary>
    private void DrawGhost(DrawingHandleScreen handle, MapId map, in Matrix3x2 worldToView)
    {
        if (_player.LocalEntity is not { } player
            || !_entManager.HasComponent<GhostComponent>(player)
            || !_entManager.TryGetComponent(player, out TransformComponent? xform)
            || xform.MapID != map)
        {
            return;
        }

        var point = Vector2.Transform(_entManager.System<SharedTransformSystem>().GetWorldPosition(xform), worldToView);
        handle.DrawCircle(point, 5f * UIScale, ShadowColor, false);
        handle.DrawCircle(point, 4f * UIScale, Color.White, false);
    }

    /// <summary>
    /// The readout in a box beside the cursor once it has rested: a blip's card, or the cell's line. It goes below and right
    /// of the cursor, flips to the other side where it would run off the control, and is kept inside it.
    /// </summary>
    private void DrawReadout(DrawingHandleScreen handle, UIBox2 bounds)
    {
        var lines = ShownReadout();
        if (lines.Count == 0 || _hover is not { } hover)
            return;

        var lineHeight = (float) _font.GetLineHeight(UIScale);
        var swatch = lineHeight * 0.6f;
        var gap = 4f * UIScale;
        var width = 0f;
        foreach (var line in lines)
        {
            var lineWidth = handle.GetDimensions(_font, line.Text, UIScale).X;
            if (line.Swatch != null)
                lineWidth += swatch + gap;

            width = MathF.Max(width, lineWidth);
        }

        var pad = 4f * UIScale;
        var size = new Vector2(width + pad * 2f, lineHeight * lines.Count + pad * 2f);
        var offset = 16f * UIScale;
        var at = hover + new Vector2(offset);
        if (at.X + size.X > bounds.Right)
            at.X = hover.X - offset - size.X;

        if (at.Y + size.Y > bounds.Bottom)
            at.Y = hover.Y - offset - size.Y;

        at = Vector2.Clamp(at, bounds.TopLeft, Vector2.Max(bounds.TopLeft, bounds.BottomRight - size));
        handle.DrawRect(UIBox2.FromDimensions(at, size), Color.Black.WithAlpha(0.75f));
        var y = at.Y + pad;
        foreach (var line in lines)
        {
            var x = at.X + pad;
            if (line.Swatch is { } color)
            {
                handle.DrawRect(UIBox2.FromDimensions(new Vector2(x, y + (lineHeight - swatch) / 2f), new Vector2(swatch)), color);
                x += swatch + gap;
            }

            handle.DrawString(_font, new Vector2(x, y), line.Text, UIScale, line.Color);
            y += lineHeight;
        }
    }
}

/// <summary>What a blip on the sector map stands for.</summary>
public enum WFSectorBlipKind : byte
{
    Ship,
    Station,
    Encounter,
}

/// <summary>An encounter on the sector map: its name, its category, its first ship's side colour and where that ship is in map space.</summary>
public readonly record struct WFSectorEncounterView(NetEntity Encounter, string Name, WFEncounterCategory Category, Color Color, Vector2 Position);

/// <summary>A blip on the sector map, a named grid or an encounter, with what its card shows.</summary>
public readonly record struct WFSectorBlip(WFSectorBlipKind Kind, string Name, Vector2 Position, Color Color, EntityUid Grid, NetEntity Encounter, WFEncounterCategory Category)
{
    /// <summary>The blip of a named grid.</summary>
    public static WFSectorBlip Of(WFSectorLandmarkView landmark)
    {
        return new WFSectorBlip(landmark.Kind, landmark.Name, landmark.Position, landmark.Color, landmark.Grid, NetEntity.Invalid, default);
    }

    /// <summary>The blip of an encounter's marker.</summary>
    public static WFSectorBlip Of(WFSectorEncounterView encounter)
    {
        return new WFSectorBlip(WFSectorBlipKind.Encounter, encounter.Name, encounter.Position, encounter.Color, EntityUid.Invalid, encounter.Encounter, encounter.Category);
    }

    /// <summary>Whether two blips are the same grid or encounter, wherever each has moved since; two nones are the same.</summary>
    public static bool Same(WFSectorBlip? a, WFSectorBlip? b)
    {
        if (a is not { } first || b is not { } second)
            return a.HasValue == b.HasValue;

        return first.Kind == second.Kind && first.Grid == second.Grid && first.Encounter == second.Encounter;
    }
}

/// <summary>One line of the hover readout, with a swatch of a colour before it when there is one.</summary>
public readonly record struct WFSectorReadoutLine(string Text, Color Color, Color? Swatch);
