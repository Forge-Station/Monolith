using System.Numerics;
using Content.Client.Explosion;
using Content.Client.Shuttles;
using Content.Client.Viewport;
using Content.Shared._Mono.ShipGuns;
using Content.Shared._WF.Shuttles;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.Explosion.Components;
using Content.Shared.Maps;
using Content.Shared.Projectiles;
using Robust.Client.ComponentTrees;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Client.ResourceManagement;
using Robust.Shared.Enums;
using Robust.Shared.Graphics;
using Robust.Shared.Graphics.RSI;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Client._WF.Shuttles.UI;

/// <summary>
/// Plates over every hull while the pilot's external view is up. That view draws no FOV, so without
/// this the inside of each ship in sight would be on show. Guns, thrusters, fire, blasts and hits are
/// drawn back on top.
/// </summary>
public sealed partial class ShuttleHullRoofOverlay : Overlay
{
    [Dependency] private IEntityManager _entManager = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IResourceCache _resCache = default!;
    [Dependency] private ITileDefinitionManager _tileDefs = default!;

    private readonly SharedAppearanceSystem _appearance;
    private readonly SharedMapSystem _map;
    private readonly SpriteTreeSystem _spriteTree;
    private readonly SpriteSystem _sprite;
    private readonly SharedTransformSystem _transform;
    private readonly TurfSystem _turf;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    /// <summary>
    /// Tint laid over the plating texture on built hull.
    /// </summary>
    private static readonly Color SteelTint = Color.FromHex("#D5DCE6");

    /// <summary>
    /// Tint for ground nobody laid, such as asteroid rock.
    /// </summary>
    private static readonly Color RockTint = Color.FromHex("#C4A383");

    /// <summary>
    /// Stands in for the plating texture's own grey if it can't be loaded.
    /// </summary>
    private static readonly Color BarePlating = Color.FromHex("#333337");

    private static readonly Color OutlineColor = Color.FromHex("#0B0C0F");

    private static readonly ProtoId<ContentTileDefinition> PlatingTile = "Plating";
    private static readonly ProtoId<ContentTileDefinition> LatticeTile = "Lattice";
    private static readonly ProtoId<ContentTileDefinition> TrainLatticeTile = "TrainLattice";

    /// <summary>
    /// Most vertices handed over in one draw. The renderer's batch holds 65532 and throws past that.
    /// </summary>
    private const int MaxVertices = 6 * 8192;

    /// <summary>
    /// How far past the view hardware is still drawn, in tiles, so a sprite doesn't pop at the screen's edge.
    /// </summary>
    private const float HardwareMargin = 2f;

    private const int MaxParentDepth = 8;

    private const string FireRsiPath = "/Textures/Effects/fire.rsi";
    private const int FireStates = 3;

    /// <summary>
    /// Seconds a frame of tile fire is held.
    /// </summary>
    private const float FireFrameTime = 0.1f;

    private readonly Dictionary<EntityUid, HullRoof> _roofs = new();
    private readonly Dictionary<Vector2i, int> _roofed = new();
    private readonly HashSet<EntityUid> _drawn = new();
    private List<Entity<MapGridComponent>> _grids = new();
    private readonly List<Entity<SpriteComponent, TransformComponent>> _sprites = new();
    private Texture[]?[]? _fireFrames;

    /// <summary>
    /// How each kind of tile is roofed, by tile id.
    /// </summary>
    private TileRoof[] _tiles = Array.Empty<TileRoof>();

    private bool _tilesBuilt;
    private Texture? _plate;
    private int _plateVariants = 1;

    public ShuttleHullRoofOverlay()
    {
        IoCManager.InjectDependencies(this);

        _appearance = _entManager.System<SharedAppearanceSystem>();
        _map = _entManager.System<SharedMapSystem>();
        _spriteTree = _entManager.System<SpriteTreeSystem>();
        _sprite = _entManager.System<SpriteSystem>();
        _transform = _entManager.System<SharedTransformSystem>();
        _turf = _entManager.System<TurfSystem>();

        // First of the unlit, above-FOV overlays: over status icons, health bars and do-afters, which
        // trust FOV to hide them, and under the effects that work on the finished picture.
        ZIndex = -10;
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        if (!_entManager.TryGetComponent<ShuttleCameraComponent>(_player.LocalEntity, out var camera) ||
            camera.View != ShuttleCameraView.External ||
            !_entManager.TryGetComponent<EyeComponent>(_player.LocalEntity, out var eye))
        {
            return false;
        }

        // The ship previewer draws maps the client made itself, with nothing in them to hide.
        if (!args.MapUid.IsValid() || _entManager.IsClientSide(args.MapUid))
            return false;

        // The levels below and above are drawn without FOV as well, so they're roofed too.
        return IsPilotPass(args.Viewport.Eye, eye.Eye, primaryOnly: false);
    }

    /// <summary>
    /// Whether a pass is drawn through the pilot's eye or a z-level stand-in made from it. Every other
    /// viewport, such as a camera monitor, is drawn through stand-ins of its own eye while the pilot has
    /// altitude, and those keep that eye's position.
    /// </summary>
    public static bool IsPilotPass(IEye? pass, IEye pilot, bool primaryOnly)
    {
        if (pass == pilot)
            return true;

        return pass is ScalingViewport.ZEye stand &&
               (stand.Primary || !primaryOnly) &&
               stand.Position.Position == pilot.Position.Position &&
               stand.Rotation == pilot.Rotation;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var handle = args.WorldHandle;

        _grids.Clear();
        _drawn.Clear();
        _map.FindGridsIntersecting(args.MapId, args.WorldBounds, ref _grids, approx: true, includeMap: false);

        foreach (var grid in _grids)
        {
            var roof = GetRoof(grid);

            if (roof.Count == 0)
                continue;

            _drawn.Add(grid.Owner);
            handle.SetTransform(_transform.GetWorldMatrix(grid.Owner));

            DrawSliced(handle, DrawPrimitiveTopology.TriangleList, _plate ?? Texture.White, roof.Vertices, roof.Count);
            DrawSliced(handle, DrawPrimitiveTopology.LineList, Texture.White, roof.Outline, roof.OutlineCount);
        }

        handle.SetTransform(Matrix3x2.Identity);

        if (_drawn.Count == 0)
            return;

        DrawHardware(args);
        DrawFire(args);
        DrawExplosions(args);
        DrawEffects(args);
        handle.SetTransform(Matrix3x2.Identity);
    }

    /// <summary>
    /// Draws a vertex list a batch at a time. <see cref="MaxVertices"/> divides by both two and three,
    /// so no triangle or line is cut in half.
    /// </summary>
    private static void DrawSliced(
        DrawingHandleWorld handle,
        DrawPrimitiveTopology topology,
        Texture texture,
        DrawVertexUV2DColor[] vertices,
        int count)
    {
        for (var start = 0; start < count; start += MaxVertices)
        {
            var length = Math.Min(MaxVertices, count - start);
            handle.DrawPrimitives(topology, texture, new ReadOnlySpan<DrawVertexUV2DColor>(vertices, start, length));
        }
    }

    /// <summary>
    /// Puts back what belongs on the outside of a hull. The roof has no holes for it, since a hole
    /// would show everything else standing on that tile.
    /// </summary>
    private void DrawHardware(in OverlayDrawArgs args)
    {
        var handle = args.WorldHandle;
        var eyeRotation = args.Viewport.Eye?.Rotation ?? Angle.Zero;
        var bounds = args.WorldAABB.Enlarged(HardwareMargin);

        var guns = _entManager.EntityQueryEnumerator<ShipGunClassComponent, SpriteComponent, TransformComponent>();

        while (guns.MoveNext(out var uid, out _, out var sprite, out var xform))
        {
            if (IsOnDrawnHull(xform, sprite))
                DrawSprite(handle, (uid, sprite), xform, eyeRotation, bounds);
        }

        var thrusters = _entManager.EntityQueryEnumerator<ThrusterComponent, SpriteComponent, TransformComponent>();

        while (thrusters.MoveNext(out var uid, out _, out var sprite, out var xform))
        {
            // Gyroscopes are thrusters too, and sit inside. A real one has its nozzle over open space.
            if (IsOnDrawnHull(xform, sprite) && NozzleExposed(xform))
                DrawSprite(handle, (uid, sprite), xform, eyeRotation, bounds);
        }
    }

    /// <summary>
    /// Burning tiles on the roofed hulls, so a fire aboard shows from outside.
    /// </summary>
    private void DrawFire(in OverlayDrawArgs args)
    {
        var handle = args.WorldHandle;
        var frames = GetFireFrames();
        var frame = (int) (_timing.RealTime.TotalSeconds / FireFrameTime);

        foreach (var gridUid in _drawn)
        {
            if (!_entManager.TryGetComponent<GasTileOverlayComponent>(gridUid, out var gas))
                continue;

            var (_, _, matrix, invMatrix) = _transform.GetWorldPositionRotationMatrixWithInv(gridUid);
            var bounds = invMatrix.TransformBox(args.WorldBounds).Enlarged(1f);
            handle.SetTransform(matrix);

            foreach (var chunk in gas.Chunks.Values)
            {
                var tiles = new GasChunkEnumerator(chunk);

                while (tiles.MoveNext(out var tile))
                {
                    if (tile.FireState == 0)
                        continue;

                    var indices = chunk.Origin + (tiles.X, tiles.Y);

                    if (!bounds.Contains(indices + Vector2Helpers.Half))
                        continue;

                    if (frames[Math.Min((int) tile.FireState, FireStates) - 1] is { Length: > 0 } state)
                        handle.DrawTexture(state[frame % state.Length], indices);
                }
            }
        }
    }

    private Texture[]?[] GetFireFrames()
    {
        if (_fireFrames != null)
            return _fireFrames;

        _fireFrames = new Texture[]?[FireStates];

        if (!_resCache.TryGetResource<RSIResource>(FireRsiPath, out var fire))
            return _fireFrames;

        for (var i = 0; i < FireStates; i++)
        {
            if (fire.RSI.TryGetState((i + 1).ToString(), out var state))
                _fireFrames[i] = state.GetFrames(RsiDirection.South);
        }

        return _fireFrames;
    }

    /// <summary>
    /// Blasts, drawn as the explosion overlay draws them underneath.
    /// </summary>
    private void DrawExplosions(in OverlayDrawArgs args)
    {
        var handle = args.WorldHandle;
        var query = _entManager.EntityQueryEnumerator<ExplosionVisualsComponent, ExplosionVisualsTexturesComponent>();

        while (query.MoveNext(out var uid, out var visuals, out var textures))
        {
            if (visuals.Epicenter.MapId != args.MapId ||
                visuals.Intensity.Count == 0 ||
                textures.FireFrames.Count == 0 ||
                !_appearance.TryGetData(uid, ExplosionAppearanceData.Progress, out int progress))
            {
                continue;
            }

            progress = Math.Min(progress, visuals.Intensity.Count - 1);

            foreach (var (gridUid, tileSets) in visuals.Tiles)
            {
                if (!_drawn.Contains(gridUid) || !_entManager.TryGetComponent<MapGridComponent>(gridUid, out var grid))
                    continue;

                var (_, _, matrix, invMatrix) = _transform.GetWorldPositionRotationMatrixWithInv(gridUid);
                var bounds = invMatrix.TransformBox(args.WorldBounds).Enlarged(grid.TileSize * 2);
                var size = new Vector2(grid.TileSize, grid.TileSize);
                handle.SetTransform(matrix);

                for (var ring = 0; ring <= progress; ring++)
                {
                    if (!tileSets.TryGetValue(ring, out var tiles))
                        continue;

                    var strength = (int) Math.Min(visuals.Intensity[ring] / textures.IntensityPerState, textures.FireFrames.Count - 1);
                    var frames = textures.FireFrames[Math.Max(strength, 0)];

                    foreach (var tile in tiles)
                    {
                        var centre = (tile + Vector2Helpers.Half) * grid.TileSize;

                        if (bounds.Contains(centre))
                            handle.DrawTextureRect(_random.Pick(frames), Box2.CenteredAround(centre, size), textures.FireColor);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Shots and the sparks they leave where they land on a roofed hull.
    /// </summary>
    private void DrawEffects(in OverlayDrawArgs args)
    {
        var handle = args.WorldHandle;
        var eyeRotation = args.Viewport.Eye?.Rotation ?? Angle.Zero;
        var bounds = args.WorldAABB.Enlarged(HardwareMargin);

        _sprites.Clear();
        _spriteTree.QueryAabb(_sprites, args.MapId, bounds);

        foreach (var (uid, sprite, xform) in _sprites)
        {
            if (!sprite.Visible || xform.GridUid is not { } gridUid || !_drawn.Contains(gridUid))
                continue;

            if (sprite.DrawDepth != (int) Content.Shared.DrawDepth.DrawDepth.Effects &&
                !_entManager.HasComponent<ProjectileComponent>(uid))
            {
                continue;
            }

            DrawSprite(handle, (uid, sprite), xform, eyeRotation, bounds);
        }
    }

    private bool IsOnDrawnHull(TransformComponent xform, SpriteComponent sprite)
    {
        return xform.Anchored && sprite.Visible && xform.GridUid is { } grid && _drawn.Contains(grid);
    }

    private void DrawSprite(
        DrawingHandleWorld handle,
        Entity<SpriteComponent> sprite,
        TransformComponent xform,
        Angle eyeRotation,
        Box2 bounds)
    {
        var (position, rotation) = _transform.GetWorldPositionRotation(xform);

        if (bounds.Contains(position))
            _sprite.RenderSprite(sprite, handle, eyeRotation, rotation, position);
    }

    /// <summary>
    /// Whether the tile behind a thruster is open to space, as the server asks before letting it fire.
    /// </summary>
    private bool NozzleExposed(TransformComponent xform)
    {
        if (xform.GridUid is not { } gridUid || !_entManager.TryGetComponent<MapGridComponent>(gridUid, out var grid))
            return false;

        var (x, y) = xform.LocalPosition + xform.LocalRotation.Opposite().ToWorldVec();
        var tile = _map.GetTileRef(gridUid, grid, new Vector2i((int) Math.Floor(x), (int) Math.Floor(y)));

        return _turf.IsSpace(tile);
    }

    /// <summary>
    /// The roof over a grid, built again if its tiles have changed since.
    /// </summary>
    public HullRoof GetRoof(Entity<MapGridComponent> grid)
    {
        EnsureTiles();

        if (!_roofs.TryGetValue(grid.Owner, out var roof))
        {
            roof = new HullRoof();
            _roofs[grid.Owner] = roof;
        }

        // No subscription to tile changes: the grid already keeps the tick of its last one.
        if (roof.Built && roof.Tick == grid.Comp.LastTileModifiedTick)
            return roof;

        Build(grid, roof);
        roof.Built = true;
        roof.Tick = grid.Comp.LastTileModifiedTick;
        return roof;
    }

    /// <summary>
    /// Drops the roof kept for a grid that's gone.
    /// </summary>
    public void ForgetGrid(EntityUid grid)
    {
        _roofs.Remove(grid);
    }

    /// <summary>
    /// Reads the tile prototypes again, after a reload.
    /// </summary>
    public void ReloadTiles()
    {
        _tilesBuilt = false;
    }

    private void EnsureTiles()
    {
        if (_tilesBuilt && _tiles.Length == _tileDefs.Count)
            return;

        _tilesBuilt = true;
        _roofs.Clear();

        // The tile atlas is the engine's own, so the plating's sprite is loaded as a texture by itself.
        var bare = BarePlating;
        _plate = null;
        _plateVariants = 1;

        if (_proto.Resolve(PlatingTile, out var plating) &&
            plating.Sprite is { } path &&
            _resCache.TryGetResource<TextureResource>(path, out var texture))
        {
            bare = Color.White;
            _plate = texture.Texture;
            _plateVariants = Math.Max(1, (int) plating.Variants);
        }

        _tiles = new TileRoof[_tileDefs.Count];

        for (var i = 0; i < _tiles.Length; i++)
        {
            if (_tileDefs[i] is not ContentTileDefinition def)
            {
                _tiles[i].Open = true;
                continue;
            }

            var tint = def.Weather && !def.MapAtmosphere ? RockTint : SteelTint;

            _tiles[i] = new TileRoof
            {
                // Diagonal hull corners are open to space as well, so only the lattices count as grating.
                Open = def.MapAtmosphere && IsLattice(def, 0),
                Color = Color.FromSrgb(tint * bare),
                Shape = def.Vertices,
            };
        }
    }

    private bool IsLattice(ContentTileDefinition def, int depth)
    {
        if (def.ID == LatticeTile.Id || def.ID == TrainLatticeTile.Id)
            return true;

        if (def.Parents == null || depth >= MaxParentDepth)
            return false;

        foreach (var parent in def.Parents)
        {
            if (_proto.TryIndex<ContentTileDefinition>(parent, out var parentDef) && IsLattice(parentDef, depth + 1))
                return true;
        }

        return false;
    }

    private void Build(Entity<MapGridComponent> grid, HullRoof roof)
    {
        _roofed.Clear();

        var tiles = _map.GetAllTiles(grid.Owner, grid.Comp);

        while (tiles.MoveNext(out var tileRef))
        {
            var id = tileRef.Value.Tile.TypeId;

            if (id >= 0 && id < _tiles.Length && !_tiles[id].Open && _tiles[id].Shape.Count >= 3)
                _roofed[tileRef.Value.GridIndices] = id;
        }

        var tileSize = (float) grid.Comp.TileSize;
        var vertices = roof.Vertices;
        var count = 0;
        var outline = roof.Outline;
        var outlineCount = 0;
        var outlineColor = Color.FromSrgb(OutlineColor);

        foreach (var (indices, id) in _roofed)
        {
            var origin = new Vector2(indices.X, indices.Y) * tileSize;
            var shape = _tiles[id].Shape;
            var color = _tiles[id].Color;

            // Shapes are convex, so a fan from the first corner covers them.
            for (var i = 2; i < shape.Count; i++)
            {
                Add(ref vertices, ref count, origin + shape[0] * tileSize, PlateUv(shape[0]), color);
                Add(ref vertices, ref count, origin + shape[i - 1] * tileSize, PlateUv(shape[i - 1]), color);
                Add(ref vertices, ref count, origin + shape[i] * tileSize, PlateUv(shape[i]), color);
            }

            var from = shape[^1];

            foreach (var to in shape)
            {
                AddOutline(ref outline, ref outlineCount, indices, origin, tileSize, from, to, outlineColor);
                from = to;
            }
        }

        roof.Vertices = vertices;
        roof.Count = count;
        roof.Outline = outline;
        roof.OutlineCount = outlineCount;
    }

    /// <summary>
    /// Adds one side of a tile's shape to the hull outline, less whatever of it a roofed neighbour meets.
    /// </summary>
    private void AddOutline(
        ref DrawVertexUV2DColor[] outline,
        ref int count,
        Vector2i indices,
        Vector2 origin,
        float tileSize,
        Vector2 from,
        Vector2 to,
        Color color)
    {
        Vector2i side;

        if (from.X == 0f && to.X == 0f)
            side = Vector2i.Left;
        else if (from.X == 1f && to.X == 1f)
            side = Vector2i.Right;
        else if (from.Y == 0f && to.Y == 0f)
            side = Vector2i.Down;
        else if (from.Y == 1f && to.Y == 1f)
            side = Vector2i.Up;
        else
            side = Vector2i.Zero;

        // A side that crosses its tile has nothing across it, and neither does one on the hull's edge.
        if (side == Vector2i.Zero ||
            !_roofed.TryGetValue(indices + side, out var neighbour) ||
            !TryGetSideSpan(_tiles[neighbour].Shape, -side, out var metLow, out var metHigh))
        {
            AddLine(ref outline, ref count, origin + from * tileSize, origin + to * tileSize, color);
            return;
        }

        var upright = side.X != 0;
        var low = upright ? Math.Min(from.Y, to.Y) : Math.Min(from.X, to.X);
        var high = upright ? Math.Max(from.Y, to.Y) : Math.Max(from.X, to.X);
        var across = upright ? from.X : from.Y;

        if (metLow > low)
            AddSpan(ref outline, ref count, origin, tileSize, upright, across, low, Math.Min(high, metLow), color);

        if (metHigh < high)
            AddSpan(ref outline, ref count, origin, tileSize, upright, across, Math.Max(low, metHigh), high, color);
    }

    /// <summary>
    /// How much of one side of its tile a shape runs along, if it has an edge there at all.
    /// </summary>
    private static bool TryGetSideSpan(List<Vector2> shape, Vector2i side, out float low, out float high)
    {
        low = float.MaxValue;
        high = float.MinValue;

        var upright = side.X != 0;
        var across = side.X > 0 || side.Y > 0 ? 1f : 0f;
        var corners = 0;

        foreach (var corner in shape)
        {
            if ((upright ? corner.X : corner.Y) != across)
                continue;

            var along = upright ? corner.Y : corner.X;
            low = Math.Min(low, along);
            high = Math.Max(high, along);
            corners++;
        }

        return corners >= 2 && high > low;
    }

    private static void AddSpan(
        ref DrawVertexUV2DColor[] outline,
        ref int count,
        Vector2 origin,
        float tileSize,
        bool upright,
        float across,
        float low,
        float high,
        Color color)
    {
        if (high <= low)
            return;

        var from = upright ? new Vector2(across, low) : new Vector2(low, across);
        var to = upright ? new Vector2(across, high) : new Vector2(high, across);
        AddLine(ref outline, ref count, origin + from * tileSize, origin + to * tileSize, color);
    }

    private static void AddLine(ref DrawVertexUV2DColor[] outline, ref int count, Vector2 from, Vector2 to, Color color)
    {
        Add(ref outline, ref count, from, Vector2.Zero, color);
        Add(ref outline, ref count, to, Vector2.Zero, color);
    }

    private static void Add(ref DrawVertexUV2DColor[] vertices, ref int count, Vector2 position, Vector2 uv, Color color)
    {
        if (count == vertices.Length)
            Array.Resize(ref vertices, Math.Max(256, vertices.Length * 2));

        vertices[count++] = new DrawVertexUV2DColor(position, uv, color);
    }

    /// <summary>
    /// Where a corner of a tile's shape falls on the plating texture, whose variants sit side by side.
    /// </summary>
    private Vector2 PlateUv(Vector2 corner)
    {
        return new Vector2(corner.X / _plateVariants, corner.Y);
    }

    private struct TileRoof
    {
        /// <summary>Open grating, which gets no roof.</summary>
        public bool Open;

        /// <summary>Vertex colour, which the renderer wants linear.</summary>
        public Color Color;

        /// <summary>The tile's outline within one tile, a square unless it's a shaped hull piece.</summary>
        public List<Vector2> Shape;
    }

    /// <summary>
    /// A grid's roof in its own coordinates: triangles for the plating and lines for the hull's edge.
    /// </summary>
    public sealed class HullRoof
    {
        internal DrawVertexUV2DColor[] Vertices = Array.Empty<DrawVertexUV2DColor>();
        internal DrawVertexUV2DColor[] Outline = Array.Empty<DrawVertexUV2DColor>();
        internal bool Built;
        internal GameTick Tick;

        /// <summary>How many plating vertices there are, three to a triangle.</summary>
        public int Count { get; internal set; }

        /// <summary>How many outline vertices there are, two to a line.</summary>
        public int OutlineCount { get; internal set; }
    }
}
