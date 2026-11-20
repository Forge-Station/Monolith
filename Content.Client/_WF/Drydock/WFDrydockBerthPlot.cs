using System.Numerics;
using Content.Client._WF.CombatConsole;
using Content.Client._WF.Stylesheets;
using Content.Client.Resources;
using Content.Shared._WF.Drydock;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;

namespace Content.Client._WF.Drydock;

/// <summary>
/// Live top-down plot of a drydock berth: the bay, its cranes, the ship in it and the work still to do.
/// The berth mouth faces the bottom of the plot, so ships sail up into it.
/// </summary>
public sealed partial class WFDrydockBerthPlot : Control
{
    /// <summary>Tiles of approach lane drawn past the berth mouth.</summary>
    private const float LaneLength = 9f;

    /// <summary>Smallest side of the plot, in tiles, while it frames a ship.</summary>
    private const float FocusSize = 26f;

    /// <summary>Tiles of clearance around a framed ship.</summary>
    private const float FocusMargin = 5f;

    /// <summary>Tiles of clearance kept around the berth and its cranes.</summary>
    private const float ViewPadding = 2.5f;

    /// <summary>Seconds between rescans of the ship's and station's tiles.</summary>
    private static readonly TimeSpan ScanInterval = TimeSpan.FromSeconds(1);

    /// <summary>Most vertices in one draw, a multiple of 6 well inside the renderer's 65,532-vertex batch.</summary>
    private const int MaxDrawVertices = 30000;

    /// <summary>Most tiles of the station drawn around the berth.</summary>
    private const int MaxStationTiles = 12000;

    /// <summary>Scale bar lengths to pick from, in tiles.</summary>
    private static readonly int[] ScaleSteps = { 1, 2, 5, 10, 20, 50 };

    [Dependency] private IEntityManager _entMan = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IResourceCache _cache = default!;

    private readonly SharedTransformSystem _transform;
    private readonly SharedMapSystem _map;
    private readonly Font _digits;
    private Font? _small;
    private Font? _large;
    private string? _fontSkin;

    private WFDrydockConsoleState? _state;
    private TimeSpan _nextScan;

    /// <summary>Ship tiles as triangles, and their outer edges, in the ship's frame.</summary>
    private readonly List<Vector2> _shipFill = new();
    private readonly List<Vector2> _shipEdges = new();
    private Box2 _shipBounds;
    private EntityUid? _scannedShip;

    /// <summary>Station tiles around the berth as triangles, and their outer edges, in the plot's tile frame.</summary>
    private readonly List<Vector2> _stationFill = new();
    private readonly List<Vector2> _stationEdges = new();

    private readonly HashSet<Vector2i> _tileSet = new();
    private readonly List<Vector2i> _tiles = new();
    private readonly List<(WFDrydockCraneComponent Crane, Vector2 Position, Angle Rotation)> _cranes = new();
    private Vector2[] _vertices = Array.Empty<Vector2>();
    private readonly Vector2[] _quad = new Vector2[4];

    /// <summary>Line along the plot's foot describing the berth, centred large when there is no berth to draw.</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Color of the status line; null for the skin's accent.</summary>
    public Color? StatusTint { get; set; }

    public WFDrydockBerthPlot()
    {
        IoCManager.InjectDependencies(this);
        _transform = _entMan.System<SharedTransformSystem>();
        _map = _entMan.System<SharedMapSystem>();
        _digits = new VectorFont(_cache.GetResource<FontResource>("/Fonts/RobotoMono/RobotoMono-Bold.ttf"), 10);
        RectClipContent = true;
        MouseFilter = MouseFilterMode.Ignore;
        MinSize = new Vector2(300, 240);
    }

    /// <summary>Takes the berth, ship and work marks to draw from the console state.</summary>
    public void SetState(WFDrydockConsoleState state)
    {
        if (_state?.Ship != state.Ship || _state?.Station != state.Station || _state?.BerthArea != state.BerthArea)
            _nextScan = TimeSpan.Zero;
        _state = state;
    }

    /// <summary>Maps the station grid's frame to the plot: berth centre at the origin, mouth toward +Y, in pixels.</summary>
    private readonly record struct PlotFrame(Vector2 Center, Vector2 Along, Vector2 Across, float Scale, Vector2 Origin)
    {
        /// <summary>A station point in plot tiles: across the bay, then toward its mouth.</summary>
        public Vector2 ToView(Vector2 station)
        {
            var d = station - Center;
            return new Vector2(Vector2.Dot(d, Across), Vector2.Dot(d, Along));
        }

        public Vector2 ViewToScreen(Vector2 view) => Origin + view * Scale;

        public Vector2 ToScreen(Vector2 station) => ViewToScreen(ToView(station));

        public Vector2 ViewToStation(Vector2 view) => Center + Across * view.X + Along * view.Y;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        var skin = WFInstrumentTheme.Skin;
        LoadFonts(skin);
        var time = (float) _timing.RealTime.TotalSeconds;

        if (_state is not { } state || !TryFrame(state, out var frame, out var station))
        {
            DrawGrid(handle, skin, Vector2.Zero, 18 * UIScale);
            var blink = MathF.Sin(time * 3f) > -0.2f;
            if (blink)
                Text(handle, _large!, Status.ToUpperInvariant(), PixelSizeBox.Center, StatusTint ?? skin.Accent, center: true);
            DrawSweep(handle, skin, time);
            return;
        }

        if (_timing.RealTime >= _nextScan)
        {
            _nextScan = _timing.RealTime + ScanInterval;
            ScanStation(frame, station);
            ScanShip(state);
        }

        DrawGrid(handle, skin, frame.ToScreen(Vector2.Zero), frame.Scale);
        DrawPoints(handle, frame, DrawPrimitiveTopology.TriangleList, _stationFill, null, skin.TextMuted.WithAlpha(0.2f));
        DrawPoints(handle, frame, DrawPrimitiveTopology.LineList, _stationEdges, null, skin.TextMuted.WithAlpha(0.7f));
        DrawLane(handle, frame, state, skin, time);
        DrawBerth(handle, frame, state, skin);

        var shipMatrix = ShipMatrix(state, station);
        if (shipMatrix is { } toStation)
        {
            DrawPoints(handle, frame, DrawPrimitiveTopology.TriangleList, _shipFill, toStation, skin.Text.WithAlpha(0.14f));
            DrawPoints(handle, frame, DrawPrimitiveTopology.LineList, _shipEdges, toStation, skin.Text.WithAlpha(0.85f));
            DrawMarks(handle, frame, state, toStation, skin, time);
        }

        DrawCranes(handle, frame, skin, time);
        if (shipMatrix is { } matrix)
            DrawShipPointer(handle, frame, matrix, skin);

        DrawLegend(handle, frame, state, skin, time);
        DrawSweep(handle, skin, time);
    }

    private void LoadFonts(WolfgateSkin skin)
    {
        if (_fontSkin == skin.Id)
            return;
        _small = _cache.GetFont(skin.MenuFonts, 9);
        _large = _cache.GetFont(skin.MenuFonts, 14);
        _fontSkin = skin.Id;
    }

    /// <summary>Fits the berth and its approach lane into the control, or returns false when the berth can't be drawn.</summary>
    private bool TryFrame(WFDrydockConsoleState state, out PlotFrame frame, out EntityUid station)
    {
        frame = default;
        station = EntityUid.Invalid;
        if (state.BerthLabel == null || !_entMan.TryGetEntity(state.Station, out var stationUid) ||
            !_entMan.HasComponent<MapGridComponent>(stationUid) || PixelWidth < 8 || PixelHeight < 8)
            return false;

        station = stationUid.Value;
        var along = state.Approach.LengthSquared() > 1e-6f ? Vector2.Normalize(state.Approach) : new Vector2(0, -1);
        var across = new Vector2(-along.Y, along.X);
        var area = state.BerthArea;
        var unit = new PlotFrame(area.Center, along, across, 1f, Vector2.Zero);

        var min = new Vector2(float.MaxValue);
        var max = new Vector2(float.MinValue);
        void Include(Vector2 view)
        {
            min = Vector2.Min(min, view);
            max = Vector2.Max(max, view);
        }

        CollectCranes(state, station);
        if (ShipFocus(state, station, unit) is { } focus)
        {
            // A ship in the berth fills the plot, so its damage reads at any berth size.
            min = focus.BottomLeft;
            max = focus.TopRight;
        }
        else
        {
            Include(unit.ToView(area.BottomLeft));
            Include(unit.ToView(area.BottomRight));
            Include(unit.ToView(area.TopLeft));
            Include(unit.ToView(area.TopRight));
            Include(new Vector2(0, max.Y + LaneLength));

            // Cranes stand at the head of the bay; keep their bodies in view.
            foreach (var (_, position, _) in _cranes)
                Include(unit.ToView(position));
        }

        min -= new Vector2(ViewPadding);
        max += new Vector2(ViewPadding);
        var size = max - min;
        var scale = MathF.Min(PixelWidth / size.X, PixelHeight / size.Y);
        var origin = PixelSizeBox.Center - (min + max) / 2f * scale;
        frame = new PlotFrame(area.Center, along, across, scale, origin);
        return true;
    }

    /// <summary>
    /// The ship's footprint in plot tiles, grown to at least <see cref="FocusSize"/> a side and padded, or null when
    /// no scanned ship sits in or beside the berth.
    /// </summary>
    private Box2? ShipFocus(WFDrydockConsoleState state, EntityUid station, PlotFrame unit)
    {
        if (ShipMatrix(state, station) is not { } toStation)
            return null;

        var min = new Vector2(float.MaxValue);
        var max = new Vector2(float.MinValue);
        void Include(Vector2 corner)
        {
            var view = unit.ToView(Vector2.Transform(corner, toStation));
            min = Vector2.Min(min, view);
            max = Vector2.Max(max, view);
        }

        Include(_shipBounds.BottomLeft);
        Include(_shipBounds.BottomRight);
        Include(_shipBounds.TopLeft);
        Include(_shipBounds.TopRight);

        var center = (min + max) / 2f;
        var berth = BerthView(unit, state).Enlarged(FocusMargin);
        if (!berth.Contains(center))
            return null;

        var half = Vector2.Max((max - min) / 2f + new Vector2(FocusMargin), new Vector2(FocusSize / 2f));
        return new Box2(center - half, center + half);
    }

    /// <summary>Collects the linked cranes standing on the station, with their local positions and rotations.</summary>
    private void CollectCranes(WFDrydockConsoleState state, EntityUid station)
    {
        _cranes.Clear();
        if (!_entMan.TryGetEntity(state.BerthConsole, out var console))
            return;

        var query = _entMan.EntityQueryEnumerator<WFDrydockCraneComponent, TransformComponent>();
        while (query.MoveNext(out var crane, out var xform))
        {
            if (crane.LinkedConsole == console && xform.ParentUid == station)
                _cranes.Add((crane, xform.LocalPosition, xform.LocalRotation));
        }
    }

    /// <summary>Records the station's tiles inside the plot, and the outline they make, in plot tiles.</summary>
    private void ScanStation(PlotFrame frame, EntityUid station)
    {
        _stationFill.Clear();
        _stationEdges.Clear();
        if (!_entMan.TryGetComponent<MapGridComponent>(station, out var grid))
            return;

        var corners = new[]
        {
            frame.ViewToStation((Vector2.Zero - frame.Origin) / frame.Scale),
            frame.ViewToStation((new Vector2(PixelWidth, 0) - frame.Origin) / frame.Scale),
            frame.ViewToStation((new Vector2(0, PixelHeight) - frame.Origin) / frame.Scale),
            frame.ViewToStation((PixelSizeBox.BottomRight - frame.Origin) / frame.Scale),
        };
        var box = new Box2(Vector2.Min(Vector2.Min(corners[0], corners[1]), Vector2.Min(corners[2], corners[3])),
            Vector2.Max(Vector2.Max(corners[0], corners[1]), Vector2.Max(corners[2], corners[3])));

        _tiles.Clear();
        foreach (var tile in _map.GetLocalTilesIntersecting(station, grid, box))
        {
            if (_tiles.Count >= MaxStationTiles)
                break;
            _tiles.Add(tile.GridIndices);
        }

        var unit = frame with { Scale = 1f, Origin = Vector2.Zero };
        Outline(_tiles, grid.TileSize, _stationFill, _stationEdges, p => unit.ToView(p));
    }

    /// <summary>Records the ship's tiles and outline in its own frame.</summary>
    private void ScanShip(WFDrydockConsoleState state)
    {
        _shipFill.Clear();
        _shipEdges.Clear();
        _scannedShip = null;
        if (!_entMan.TryGetEntity(state.Ship, out var ship) || !_entMan.TryGetComponent<MapGridComponent>(ship, out var grid))
            return;

        _tiles.Clear();
        var tiles = _map.GetAllTiles(ship.Value, grid);
        while (tiles.MoveNext(out var tile))
        {
            _tiles.Add(tile!.Value.GridIndices);
        }

        Outline(_tiles, grid.TileSize, _shipFill, _shipEdges, p => p);
        _shipBounds = grid.LocalAABB;
        _scannedShip = ship;
    }

    /// <summary>Turns tiles into fill triangles and the edges they share with empty space, through a point mapping.</summary>
    private void Outline(List<Vector2i> tiles, ushort tileSize, List<Vector2> fill, List<Vector2> edges, Func<Vector2, Vector2> map)
    {
        _tileSet.Clear();
        foreach (var tile in tiles)
            _tileSet.Add(tile);

        foreach (var tile in tiles)
        {
            var a = map(new Vector2(tile.X, tile.Y) * tileSize);
            var b = map(new Vector2(tile.X + 1, tile.Y) * tileSize);
            var c = map(new Vector2(tile.X + 1, tile.Y + 1) * tileSize);
            var d = map(new Vector2(tile.X, tile.Y + 1) * tileSize);
            fill.Add(a);
            fill.Add(b);
            fill.Add(c);
            fill.Add(a);
            fill.Add(c);
            fill.Add(d);

            if (!_tileSet.Contains(tile + new Vector2i(0, -1)))
            {
                edges.Add(a);
                edges.Add(b);
            }
            if (!_tileSet.Contains(tile + new Vector2i(1, 0)))
            {
                edges.Add(b);
                edges.Add(c);
            }
            if (!_tileSet.Contains(tile + new Vector2i(0, 1)))
            {
                edges.Add(c);
                edges.Add(d);
            }
            if (!_tileSet.Contains(tile + new Vector2i(-1, 0)))
            {
                edges.Add(d);
                edges.Add(a);
            }
        }
    }

    /// <summary>The ship's frame to the station's, or null when the ship isn't known here.</summary>
    private Matrix3x2? ShipMatrix(WFDrydockConsoleState state, EntityUid station)
    {
        if (_scannedShip is not { } ship || !_entMan.TryGetEntity(state.Ship, out var current) || current != ship ||
            !_entMan.HasComponent<TransformComponent>(ship))
            return null;

        return _transform.GetWorldMatrix(ship) * _transform.GetInvWorldMatrix(station);
    }

    /// <summary>Maps a point to the screen: from the ship's frame when a matrix is given, else from plot tiles.</summary>
    private static Vector2 Screen(PlotFrame frame, Vector2 point, Matrix3x2? toStation)
    {
        return toStation is { } matrix
            ? frame.ToScreen(Vector2.Transform(point, matrix))
            : frame.ViewToScreen(point);
    }

    /// <summary>Draws triangles or lines, in slices that fit the renderer's batch.</summary>
    private void DrawPoints(DrawingHandleScreen handle, PlotFrame frame, DrawPrimitiveTopology topology, List<Vector2> points,
        Matrix3x2? toStation, Color color)
    {
        if (_vertices.Length < points.Count)
            _vertices = new Vector2[points.Count];
        for (var i = 0; i < points.Count; i++)
            _vertices[i] = Screen(frame, points[i], toStation);

        for (var start = 0; start < points.Count; start += MaxDrawVertices)
        {
            var length = Math.Min(MaxDrawVertices, points.Count - start);
            handle.DrawPrimitives(topology, new ReadOnlySpan<Vector2>(_vertices, start, length), color);
        }
    }

    /// <summary>Draws faint plotting-paper lines through tile corners, with every fifth line stronger.</summary>
    private void DrawGrid(DrawingHandleScreen handle, WolfgateSkin skin, Vector2 anchor, float step)
    {
        // Tile lines only once tiles are big enough to tell apart; otherwise every fifth line alone.
        var fine = step >= 9 * UIScale ? skin.Accent.WithAlpha(0.03f) : Color.Transparent;
        var coarse = skin.Accent.WithAlpha(0.075f);
        var startX = anchor.X - MathF.Ceiling(anchor.X / step) * step;
        var startY = anchor.Y - MathF.Ceiling(anchor.Y / step) * step;
        var indexX = (int) MathF.Round((startX - anchor.X) / step);
        var indexY = (int) MathF.Round((startY - anchor.Y) / step);
        var column = indexX;
        for (var x = startX; x <= PixelWidth; x += step, column++)
        {
            if (column % 5 == 0 || fine.A > 0)
                handle.DrawLine(new Vector2(x, 0), new Vector2(x, PixelHeight), column % 5 == 0 ? coarse : fine);
        }
        var row = indexY;
        for (var y = startY; y <= PixelHeight; y += step, row++)
        {
            if (row % 5 == 0 || fine.A > 0)
                handle.DrawLine(new Vector2(0, y), new Vector2(PixelWidth, y), row % 5 == 0 ? coarse : fine);
        }
    }

    /// <summary>Draws the approach lane's chevrons below the mouth, chasing toward the berth while its lights are on.</summary>
    private void DrawLane(DrawingHandleScreen handle, PlotFrame frame, WFDrydockConsoleState state, WolfgateSkin skin, float time)
    {
        // Plot Y grows toward the mouth, so the mouth is the berth's largest Y.
        var berth = BerthView(frame, state);
        var mouth = berth.Top;
        var halfWidth = MathF.Min(berth.Width * 0.3f, 3f);
        var lit = state.BerthLightsOn || state.BookedBerth != null;
        var pulse = (1f - time / 1.6f % 1f) * LaneLength;
        var width = MathF.Max(1.5f * UIScale, frame.Scale * 0.18f);
        for (var distance = 1f; distance <= LaneLength; distance += 1.5f)
        {
            var reach = MathF.Max(0f, 1f - MathF.Abs(distance - pulse) / 2.5f);
            var color = lit ? skin.Good.WithAlpha(0.3f + 0.7f * reach) : skin.TextMuted.WithAlpha(0.22f);
            var tip = frame.ViewToScreen(new Vector2(0, mouth + distance - 0.6f));
            var left = frame.ViewToScreen(new Vector2(-halfWidth, mouth + distance + 0.4f));
            var right = frame.ViewToScreen(new Vector2(halfWidth, mouth + distance + 0.4f));
            Thick(handle, left, tip, width, color);
            Thick(handle, tip, right, width, color);
        }
    }

    /// <summary>The berth area in plot tiles.</summary>
    private static Box2 BerthView(PlotFrame frame, WFDrydockConsoleState state)
    {
        var unit = frame with { Scale = 1f, Origin = Vector2.Zero };
        var area = state.BerthArea;
        var a = unit.ToView(area.BottomLeft);
        var b = unit.ToView(area.TopRight);
        return new Box2(Vector2.Min(a, b), Vector2.Max(a, b));
    }

    /// <summary>Draws the berth's dashed bounds, corner brackets and name.</summary>
    private void DrawBerth(DrawingHandleScreen handle, PlotFrame frame, WFDrydockConsoleState state, WolfgateSkin skin)
    {
        var view = BerthView(frame, state);
        var box = new UIBox2(frame.ViewToScreen(view.BottomLeft), frame.ViewToScreen(view.TopRight));
        var dash = MathF.Max(4 * UIScale, frame.Scale * 0.8f);
        var color = skin.Accent.WithAlpha(0.55f);
        Dashed(handle, box.TopLeft, box.TopRight, dash, color);
        Dashed(handle, box.TopRight, box.BottomRight, dash, color);
        Dashed(handle, box.BottomRight, box.BottomLeft, dash, color);
        Dashed(handle, box.BottomLeft, box.TopLeft, dash, color);

        var length = MathF.Min(MathF.Min(box.Width, box.Height) / 4f, 3f * frame.Scale);
        var width = 2 * UIScale;
        void Bracket(Vector2 corner, float dx, float dy)
        {
            Thick(handle, corner, corner + new Vector2(dx * length, 0), width, skin.Accent);
            Thick(handle, corner, corner + new Vector2(0, dy * length), width, skin.Accent);
        }

        Bracket(box.TopLeft, 1f, 1f);
        Bracket(box.TopRight, -1f, 1f);
        Bracket(box.BottomRight, -1f, -1f);
        Bracket(box.BottomLeft, 1f, -1f);

        var label = Loc.GetString("drydock-plot-berth", ("berth", state.BerthLabel ?? string.Empty)).ToUpperInvariant();
        Text(handle, _small!, label, box.TopLeft + new Vector2(4 * UIScale, 3 * UIScale), skin.Accent, center: false);
    }

    /// <summary>Draws the work left on the ship: missing hull in red, missing parts as red diamonds, damage in amber.</summary>
    private void DrawMarks(DrawingHandleScreen handle, PlotFrame frame, WFDrydockConsoleState state, Matrix3x2 toStation,
        WolfgateSkin skin, float time)
    {
        var size = frame.Scale;
        for (var i = 0; i < state.Marks.Count; i++)
        {
            var mark = state.Marks[i];
            var at = Screen(frame, mark.Position, toStation);
            var flicker = 0.6f + 0.4f * MathF.Sin(time * 4f + i * 1.7f);
            switch (mark.Kind)
            {
                case WFDrydockMarkKind.Tile:
                {
                    var half = new Vector2(MathF.Max(2 * UIScale, size * 0.42f));
                    var box = new UIBox2(at - half, at + half);
                    handle.DrawRect(box, skin.Danger.WithAlpha(0.16f * flicker));
                    handle.DrawRect(box, skin.Danger.WithAlpha(0.9f * flicker), false);
                    handle.DrawLine(box.TopLeft, box.BottomRight, skin.Danger.WithAlpha(0.6f * flicker));
                    break;
                }
                case WFDrydockMarkKind.Part:
                {
                    var r = MathF.Max(2.5f * UIScale, size * 0.38f);
                    var color = skin.Danger.WithAlpha(flicker);
                    handle.DrawLine(at + new Vector2(0, -r), at + new Vector2(r, 0), color);
                    handle.DrawLine(at + new Vector2(r, 0), at + new Vector2(0, r), color);
                    handle.DrawLine(at + new Vector2(0, r), at + new Vector2(-r, 0), color);
                    handle.DrawLine(at + new Vector2(-r, 0), at + new Vector2(0, -r), color);
                    break;
                }
                case WFDrydockMarkKind.Damage:
                    handle.DrawCircle(at, MathF.Max(1.5f * UIScale, size * 0.2f), skin.Caution.WithAlpha(0.85f));
                    break;
                case WFDrydockMarkKind.Refit:
                    handle.DrawCircle(at, MathF.Max(3 * UIScale, size * 0.45f), skin.Accent.WithAlpha(flicker), false);
                    handle.DrawCircle(at, MathF.Max(1 * UIScale, size * 0.12f), skin.Accent);
                    break;
            }
        }
    }

    /// <summary>Draws each linked crane: its arm lane, body, arm, boom and tool head, as the client animates them.</summary>
    private void DrawCranes(DrawingHandleScreen handle, PlotFrame frame, WolfgateSkin skin, float time)
    {
        var size = frame.Scale;
        foreach (var (crane, position, rotation) in _cranes)
        {
            Vector2 At(Vector2 local) => frame.ToScreen(rotation.RotateVec(local) + position);

            var stalled = crane.PowerRatio < WFDrydockCraneComponent.StallRatio;
            var fault = crane.Blocked || stalled;
            var faultLit = fault && MathF.Sin(time * 5f) > 0f;

            // The lane the arm runs down, red while something solid blocks it.
            var laneStart = At(crane.ArmToLocal(0f, 0f));
            var laneEnd = At(crane.ArmToLocal(0f, crane.MaxArm));
            if (crane.Blocked)
                Dashed(handle, laneStart, laneEnd, MathF.Max(3 * UIScale, size * 0.6f), skin.Danger.WithAlpha(0.7f));
            else
                handle.DrawLine(laneStart, laneEnd, skin.EdgeLight.WithAlpha(0.22f));

            // Body: two tiles square, one tile above the arm root.
            var center = new Vector2(crane.ArmX + (crane.Mirrored ? -0.5f : 0.5f), crane.ArmRoot + 1f);
            _quad[0] = At(center + new Vector2(-1, -1));
            _quad[1] = At(center + new Vector2(1, -1));
            _quad[2] = At(center + new Vector2(-1, 1));
            _quad[3] = At(center + new Vector2(1, 1));
            handle.DrawPrimitives(DrawPrimitiveTopology.TriangleStrip, _quad, skin.EdgeSoft);
            var outline = faultLit ? skin.Danger : skin.EdgeLight;
            handle.DrawLine(_quad[0], _quad[1], outline);
            handle.DrawLine(_quad[1], _quad[3], outline);
            handle.DrawLine(_quad[3], _quad[2], outline);
            handle.DrawLine(_quad[2], _quad[0], outline);
            if (crane.Active && MathF.Sin(time * 9f) > 0f)
                handle.DrawCircle(At(center), MathF.Max(2 * UIScale, size * 0.3f), skin.Caution);

            // Arm, then the boom slewing off the carriage, then the tool head on it.
            var armWidth = MathF.Max(2 * UIScale, size * 0.32f);
            Thick(handle, laneStart, At(crane.ArmToLocal(0f, crane.VisualArm)), armWidth, skin.TextMuted);
            var pivotLocal = crane.ArmToLocal(0f, crane.VisualPosition);
            var dir = crane.VisualBoomDirection;
            var pivot = At(pivotLocal);
            var tip = At(pivotLocal + dir * crane.VisualExtension);
            Thick(handle, pivot, tip, MathF.Max(1.5f * UIScale, size * 0.2f), skin.Accent.WithAlpha(0.85f));
            var carriage = new Vector2(MathF.Max(2 * UIScale, size * 0.35f));
            handle.DrawRect(new UIBox2(pivot - carriage, pivot + carriage), skin.Text);

            var head = At(pivotLocal + dir * Math.Clamp(crane.VisualTrolley, 1f, crane.VisualExtension));
            var headRadius = MathF.Max(3 * UIScale, size * 0.5f);
            if (crane.Active)
            {
                handle.DrawCircle(head, headRadius * (1.2f + 0.25f * MathF.Sin(time * 14f)), skin.Caution.WithAlpha(0.22f));
                for (var i = 0; i < 3; i++)
                {
                    var angle = time * 23f + i * 2.1f;
                    var spark = new Vector2(MathF.Cos(angle), MathF.Sin(angle * 1.3f)) * headRadius * (0.8f + 0.6f * (i % 2));
                    handle.DrawLine(head, head + spark, skin.Caution.WithAlpha(0.8f));
                }
            }
            handle.DrawCircle(head, headRadius, crane.Active ? skin.Caution : skin.Accent, false);
            handle.DrawLine(head - new Vector2(headRadius * 1.4f, 0), head + new Vector2(headRadius * 1.4f, 0), skin.Accent.WithAlpha(0.6f));
            handle.DrawLine(head - new Vector2(0, headRadius * 1.4f), head + new Vector2(0, headRadius * 1.4f), skin.Accent.WithAlpha(0.6f));
        }
    }

    /// <summary>Points to a ship outside the plot from its edge, with its distance from the berth.</summary>
    private void DrawShipPointer(DrawingHandleScreen handle, PlotFrame frame, Matrix3x2 toStation, WolfgateSkin skin)
    {
        var shipCenter = Vector2.Transform(_shipBounds.Center, toStation);
        var at = frame.ToScreen(shipCenter);
        var inset = 16 * UIScale;
        var inner = new UIBox2(inset, inset, PixelWidth - inset, PixelHeight - inset);
        if (inner.Contains(at))
            return;

        var mid = PixelSizeBox.Center;
        var dir = at - mid;
        if (dir.LengthSquared() < 1f)
            return;
        var t = MathF.Min(inner.Width / 2 / MathF.Max(1e-3f, MathF.Abs(dir.X)),
            inner.Height / 2 / MathF.Max(1e-3f, MathF.Abs(dir.Y)));
        var edge = mid + dir * t;
        var unit = Vector2.Normalize(dir);
        var side = new Vector2(-unit.Y, unit.X);
        var size = 8 * UIScale;
        _quad[0] = edge + unit * size;
        _quad[1] = edge - unit * size * 0.6f + side * size * 0.7f;
        _quad[2] = edge - unit * size * 0.6f - side * size * 0.7f;
        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, new ReadOnlySpan<Vector2>(_quad, 0, 3), skin.Good);

        var distance = (shipCenter - frame.Center).Length();
        var text = Loc.GetString("drydock-plot-meters", ("meters", (int) MathF.Round(distance)));
        var width = handle.GetDimensions(_digits, text, UIScale).X;
        var labelAt = edge - unit * (size * 2.4f) - new Vector2(width / 2, _digits.GetHeight(UIScale) / 2);
        labelAt = Vector2.Clamp(labelAt, new Vector2(2 * UIScale), new Vector2(PixelWidth - width - 2 * UIScale, PixelHeight - 14 * UIScale));
        handle.DrawString(_digits, labelAt, text, UIScale, skin.Good);
    }

    /// <summary>Draws the scale bar and the status line with its cursor.</summary>
    private void DrawLegend(DrawingHandleScreen handle, PlotFrame frame, WFDrydockConsoleState state, WolfgateSkin skin, float time)
    {
        // A round number of tiles that spans 60 to 150 pixels.
        var tiles = 1;
        foreach (var candidate in ScaleSteps)
        {
            tiles = candidate;
            if (candidate * frame.Scale >= 60 * UIScale)
                break;
        }

        var length = tiles * frame.Scale;
        var right = PixelWidth - 10 * UIScale;
        var y = PixelHeight - 10 * UIScale;
        var color = skin.TextMuted;
        handle.DrawLine(new Vector2(right - length, y), new Vector2(right, y), color);
        handle.DrawLine(new Vector2(right - length, y - 4 * UIScale), new Vector2(right - length, y), color);
        handle.DrawLine(new Vector2(right, y - 4 * UIScale), new Vector2(right, y), color);
        var scaleText = Loc.GetString("drydock-plot-meters", ("meters", tiles));
        var scaleWidth = handle.GetDimensions(_digits, scaleText, UIScale).X;
        handle.DrawString(_digits, new Vector2(right - length / 2 - scaleWidth / 2, y - 6 * UIScale - _digits.GetHeight(UIScale)),
            scaleText, UIScale, color);

        if (Status.Length == 0)
            return;
        var status = Status.ToUpperInvariant();
        var statusAt = new Vector2(10 * UIScale, PixelHeight - 8 * UIScale - _small!.GetHeight(UIScale));
        var tint = StatusTint ?? skin.Accent;
        var width = Text(handle, _small!, status, statusAt, tint, center: false);
        if (time % 1f < 0.55f)
        {
            var cursor = UIBox2.FromDimensions(statusAt + new Vector2(width + 3 * UIScale, 1 * UIScale),
                new Vector2(6 * UIScale, _small!.GetHeight(UIScale) - 2 * UIScale));
            handle.DrawRect(cursor, tint);
        }
    }

    /// <summary>Draws a faint refresh band sweeping down the screen.</summary>
    private void DrawSweep(DrawingHandleScreen handle, WolfgateSkin skin, float time)
    {
        var y = time / 5f % 1f * (PixelHeight + 40 * UIScale) - 20 * UIScale;
        handle.DrawRect(new UIBox2(0, y - 18 * UIScale, PixelWidth, y), skin.Accent.WithAlpha(0.025f));
        handle.DrawLine(new Vector2(0, y), new Vector2(PixelWidth, y), skin.Accent.WithAlpha(0.09f));
    }

    /// <summary>Draws text at its top-left, or centred on a point, and returns its width.</summary>
    private float Text(DrawingHandleScreen handle, Font font, string text, Vector2 at, Color color, bool center)
    {
        var scale = WFInstrumentText.FitScale(handle, font, text, UIScale, PixelWidth - 20 * UIScale);
        if (scale == 0)
            return 0;
        var size = handle.GetDimensions(font, text, scale);
        var position = center ? at - new Vector2(size.X / 2, font.GetHeight(scale) / 2) : at;
        handle.DrawString(font, position, text, scale, color);
        return size.X;
    }

    /// <summary>Draws a line of the given pixel width.</summary>
    private void Thick(DrawingHandleScreen handle, Vector2 a, Vector2 b, float width, Color color)
    {
        var d = b - a;
        var length = d.Length();
        if (length < 0.01f)
            return;
        var n = new Vector2(-d.Y, d.X) / length * (width / 2);
        _quad[0] = a + n;
        _quad[1] = a - n;
        _quad[2] = b + n;
        _quad[3] = b - n;
        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleStrip, _quad, color);
    }

    private static void Dashed(DrawingHandleScreen handle, Vector2 a, Vector2 b, float dash, Color color)
    {
        var d = b - a;
        var length = d.Length();
        if (length < 0.01f)
            return;
        var unit = d / length;
        for (var at = 0f; at < length; at += dash * 2)
            handle.DrawLine(a + unit * at, a + unit * MathF.Min(at + dash, length), color);
    }
}
