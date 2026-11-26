using System.Numerics;
using System.Runtime.InteropServices;
using Content.Client.Graphics;
using Content.Client.Viewport;
using Content.Client.Parallax;
using Content.Shared._WF.WolfWeather;
using Content.Shared.Light.Components;
using Content.Shared.Tag;
using Content.Shared.Weather;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Graphics;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._WF.WolfWeather;

/// <summary>Draws weather layers over exposed tiles, masked off roofed and indoor tiles.</summary>
public sealed partial class WolfWeatherOverlay : Overlay
{
    private static readonly ProtoId<ShaderPrototype> WeatherShader = "WFWolfWeather";
    private static readonly ProtoId<ShaderPrototype> BlurShader = "WFWolfWeatherBlur";
    private static readonly ProtoId<ShaderPrototype> UnshadedShader = "unshaded";
    private static readonly Vector2i[] Cardinals = { new(0, 1), new(1, 0), new(0, -1), new(-1, 0) };

    private const float PixelsPerTile = 32f;
    private const double CellPeriod = 1024.0;
    private const double SplashPeriod = 3600.0;

    /// <summary>How far weather fades out before cover, in tiles.</summary>
    private const float Feather = 0.45f;

    /// <summary>How far wall edges fade, in tiles.</summary>
    private const float HardFeather = Feather * 0.2f;

    /// <summary>Mask texels per tile, lowered when zoomed far out so the mask stays under MaxMaskSize.</summary>
    private const float MaskTexelsPerTile = 8f;

    private const int MaxMaskSize = 768;

    /// <summary>Tiles drawn into the mask past the view's edge, snapped so small camera moves keep the mask.</summary>
    private const float MaskMargin = 4f;

    private const float MaskSnap = 4f;

    /// <summary>How often the mask is redrawn anyway, to catch walls and roofs built or removed.</summary>
    private static readonly TimeSpan MaskRefresh = TimeSpan.FromSeconds(1);

    /// <summary>Soonest the mask is redrawn for changed tiles, as terrain streaming in changes tiles every tick.</summary>
    private static readonly TimeSpan MaskMinAge = TimeSpan.FromSeconds(0.25);

    private static readonly Color Soft = new(1f, 0f, 0f);
    private static readonly Color Hard = new(0f, 1f, 0f);
    private static readonly Color Open = new(0f, 0f, 1f);
    private static readonly Color ClearColor = new(0f, 0f, 0f, 0f);

    [Dependency] private IClyde _clyde = default!;
    [Dependency] private IEntityManager _entManager = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IGameTiming _timing = default!;

    private readonly WolfWeatherSystem _wolfWeather;
    private readonly EntityLookupSystem _lookup;
    private readonly SharedMapSystem _map;
    private readonly SharedTransformSystem _xform;
    private readonly TagSystem _tag;
    private readonly EntityQuery<BlockWeatherComponent> _blockQuery;
    private readonly EntityQuery<RoofComponent> _roofQuery;
    private readonly EntityQuery<TransformComponent> _xformQuery;
    private readonly EntityQuery<WolfWeatherMapComponent> _weatherQuery;
    private readonly OverlayResourceCache<MaskTargets> _masks = new();
    private readonly List<Vector2i> _diagonalTiles = new();
    private readonly List<Vector2> _wallHalves = new();
    private readonly List<WorldRect> _rects = new();
    private List<Entity<MapGridComponent>> _grids = new();
    private readonly Dictionary<(ProtoId<WolfWeatherPrototype>, TimeSpan, int), ShaderInstance> _shaders = new();
    private readonly HashSet<(ProtoId<WolfWeatherPrototype>, TimeSpan, int)> _usedShaders = new();
    private readonly List<(ProtoId<WolfWeatherPrototype>, TimeSpan, int)> _unusedShaders = new();
    private readonly ShaderInstance _blurH;
    private readonly ShaderInstance _blurV;
    private readonly ShaderInstance _unshaded;
    private readonly Action _drawMask;

    private DrawingHandleWorld _maskHandle = default!;
    private Matrix3x2 _maskMatrix;
    private MapId _maskMapId;
    private Box2 _maskRect;

    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowFOV;

    public WolfWeatherOverlay()
    {
        IoCManager.InjectDependencies(this);
        ZIndex = ParallaxSystem.ParallaxZIndex + 2;
        _wolfWeather = _entManager.System<WolfWeatherSystem>();
        _lookup = _entManager.System<EntityLookupSystem>();
        _map = _entManager.System<SharedMapSystem>();
        _xform = _entManager.System<SharedTransformSystem>();
        _tag = _entManager.System<TagSystem>();
        _blockQuery = _entManager.GetEntityQuery<BlockWeatherComponent>();
        _roofQuery = _entManager.GetEntityQuery<RoofComponent>();
        _xformQuery = _entManager.GetEntityQuery<TransformComponent>();
        _weatherQuery = _entManager.GetEntityQuery<WolfWeatherMapComponent>();
        _blurH = _proto.Index(BlurShader).InstanceUnique();
        _blurV = _proto.Index(BlurShader).InstanceUnique();
        _unshaded = _proto.Index(UnshadedShader).Instance();
        _drawMask = DrawMask;
    }

    protected override void DisposeBehavior()
    {
        _masks.Dispose();
        foreach (var shader in _shaders.Values)
        {
            shader.Dispose();
        }

        _shaders.Clear();
        _blurH.Dispose();
        _blurV.Dispose();
        base.DisposeBehavior();
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        // Only the view's own level: the ground seen from a cavern below it is drawn in a pass of its own, with no rain.
        if (_wolfWeather.Quality <= 0 || args.MapId == MapId.Nullspace || args.Viewport.Eye is ScalingViewport.ZEye { Primary: false })
            return;

        var mapUid = _map.GetMapOrInvalid(args.MapId);
        if (!_wolfWeather.IsSurface(mapUid)
            || !_weatherQuery.TryComp(mapUid, out var weather)
            || weather.Weathers.Count == 0
            || !_wolfWeather.TryGetState(mapUid, out var state))
            return;

        var mask = GetMask(args);
        var handle = args.WorldHandle;
        handle.SetTransform(Matrix3x2.Identity);

        var now = _timing.CurTime;
        var time = (float) (_timing.RealTime.TotalSeconds % SplashPeriod);
        _usedShaders.Clear();

        DrawWeathers(args, weather, state, mask, now, time, false);
        if (_wolfWeather.Quality >= 2)
            DrawWeathers(args, weather, state, mask, now, time, true);

        handle.UseShader(null);
        handle.SetTransform(Matrix3x2.Identity);
        DropUnusedShaders();
    }

    private void DrawWeathers(in OverlayDrawArgs args, WolfWeatherMapComponent weather, WolfWeatherMapState state, MaskTargets mask, TimeSpan now, float time, bool splashes)
    {
        foreach (var entry in weather.Weathers)
        {
            if (!_proto.TryIndex(entry.Weather, out var proto)
                || !state.Offsets.TryGetValue((entry.Weather, entry.Start), out var offsets))
                continue;

            var strength = SharedWolfWeatherSystem.GetStrength(entry, proto, now);
            if (strength <= 0f)
                continue;

            var layers = proto.FinalLayers;
            for (var i = 0; i < layers.Count && i < offsets.X.Length; i++)
            {
                var layer = layers[i];
                if (layer.MinQuality > _wolfWeather.Quality || (layer.Kind == WolfWeatherLayerKind.Splash) != splashes)
                    continue;

                var shader = GetShader((entry.Weather, entry.Start, i), layer, entry.Seed + i * 31);
                SetMovingParameters(shader, args.WorldAABB, layer, state.Wind, _wolfWeather.Down, offsets.X[i], offsets.Y[i], strength, time);
                shader.SetParameter("mask", mask.Blurred!.Texture);
                shader.SetParameter("maskOffset", args.WorldAABB.BottomLeft - mask.Rect.BottomLeft);
                shader.SetParameter("maskSize", mask.Rect.Size);
                args.WorldHandle.UseShader(shader);
                args.WorldHandle.DrawRect(args.WorldAABB, Color.White);
            }
        }
    }

    /// <summary>A layer's shader, made with its fixed parameters the first time the layer draws.</summary>
    private ShaderInstance GetShader((ProtoId<WolfWeatherPrototype>, TimeSpan, int) key, WolfWeatherLayer layer, int seed)
    {
        _usedShaders.Add(key);
        if (_shaders.TryGetValue(key, out var shader))
            return shader;

        shader = _proto.Index(WeatherShader).InstanceUnique();
        var spacing = Spacing(layer);
        shader.SetParameter("spacing", spacing);
        shader.SetParameter("along", Along(layer, spacing));
        shader.SetParameter("streak", layer.Length);
        shader.SetParameter("thick", layer.Width);
        shader.SetParameter("wobble", layer.Wobble);
        shader.SetParameter("rate", MathF.Max(layer.Rate, 0.01f));
        shader.SetParameter("soft", layer.Softness);
        shader.SetParameter("mode", (float) layer.Kind);
        shader.SetParameter("seed", (float) seed);
        shader.SetParameter("invert", layer.Kind == WolfWeatherLayerKind.Splash ? 1f : 0f);
        _shaders[key] = shader;
        return shader;
    }

    private void DropUnusedShaders()
    {
        if (_shaders.Count == _usedShaders.Count)
            return;

        _unusedShaders.Clear();
        foreach (var key in _shaders.Keys)
        {
            if (!_usedShaders.Contains(key))
                _unusedShaders.Add(key);
        }

        foreach (var key in _unusedShaders)
        {
            _shaders.Remove(key, out var shader);
            shader?.Dispose();
        }
    }

    private static void SetMovingParameters(ShaderInstance shader, Box2 bounds, WolfWeatherLayer layer, Vector2 wind, Vector2 down, double offsetX, double offsetY, float strength, float time)
    {
        var velocity = WolfWeatherSystem.GetVelocity(layer, wind, down);
        var axisA = layer.Kind == WolfWeatherLayerKind.Fog ? new Vector2(0f, 1f) : down;
        var axisC = new Vector2(-axisA.Y, axisA.X);
        var heading = velocity.LengthSquared() < 0.0001f ? axisA : Vector2.Normalize(velocity);
        var spacing = Spacing(layer);
        var along = Along(layer, spacing);

        var originX = Math.Floor(bounds.Left * PixelsPerTile) / PixelsPerTile;
        var originY = Math.Floor(bounds.Bottom * PixelsPerTile) / PixelsPerTile;
        var qx = originX - offsetX;
        var qy = originY - offsetY;
        var u0 = Wrap((qx * axisC.X + qy * axisC.Y) / spacing);
        var v0 = Wrap((qx * axisA.X + qy * axisA.Y) / along);

        var density = layer.Kind is WolfWeatherLayerKind.Splash or WolfWeatherLayerKind.Fog ? layer.Density : layer.Density * strength;

        shader.SetParameter("size", bounds.Size);
        shader.SetParameter("fracOrigin", new Vector2((float) (bounds.Left - originX), (float) (bounds.Bottom - originY)));
        shader.SetParameter("axisA", axisA);
        shader.SetParameter("axisC", axisC);
        shader.SetParameter("cell0", new Vector2((float) u0, (float) v0));
        shader.SetParameter("density", density);
        shader.SetParameter("time", time);
        shader.SetParameter("tint", layer.Color.WithAlpha(layer.Color.A * strength));
        shader.SetParameter("dir", new Vector2(Vector2.Dot(heading, axisC), Vector2.Dot(heading, axisA)));
    }

    private static float Spacing(WolfWeatherLayer layer)
    {
        return MathF.Max(layer.Spacing, 1f / PixelsPerTile);
    }

    private static float Along(WolfWeatherLayer layer, float spacing)
    {
        return layer.Kind == WolfWeatherLayerKind.Rain ? MathF.Max(spacing, layer.Length / PixelsPerTile * 2f) : spacing;
    }

    private static double Wrap(double value)
    {
        var r = value % CellPeriod;
        return r < 0 ? r + CellPeriod : r;
    }

    /// <summary>The viewport's world-space cover mask, redrawn only when the view leaves it, tiles change or it ages.</summary>
    private MaskTargets GetMask(in OverlayDrawArgs args)
    {
        var mask = _masks.GetForViewport(args.Viewport, static _ => new MaskTargets());
        var view = args.WorldAABB.Enlarged(MaskMargin * 0.5f);
        var real = _timing.RealTime;

        if (mask.Blurred != null
            && mask.MapId == args.MapId
            && mask.Rect.Contains(view.BottomLeft)
            && mask.Rect.Contains(view.TopRight)
            && (mask.Version == _wolfWeather.MaskVersion || real < mask.BuiltAt + MaskMinAge)
            && real < mask.BuiltAt + MaskRefresh)
            return mask;

        var outer = args.WorldAABB.Enlarged(MaskMargin);
        var rect = new Box2(
            MathF.Floor(outer.Left / MaskSnap) * MaskSnap,
            MathF.Floor(outer.Bottom / MaskSnap) * MaskSnap,
            MathF.Ceiling(outer.Right / MaskSnap) * MaskSnap,
            MathF.Ceiling(outer.Top / MaskSnap) * MaskSnap);

        var texels = MathF.Min(MaskTexelsPerTile, MaxMaskSize / MathF.Max(rect.Width, rect.Height));
        var size = new Vector2i((int) MathF.Ceiling(rect.Width * texels), (int) MathF.Ceiling(rect.Height * texels));

        EnsureTarget(ref mask.Raw, size, "wolfweather-mask-raw");
        EnsureTarget(ref mask.Across, size, "wolfweather-mask-across");
        EnsureTarget(ref mask.Blurred, size, "wolfweather-mask");

        mask.MapId = args.MapId;
        mask.Rect = rect;
        mask.Version = _wolfWeather.MaskVersion;
        mask.BuiltAt = real;

        var handle = args.WorldHandle;
        _maskHandle = handle;
        _maskMapId = args.MapId;
        _maskRect = rect;
        _maskMatrix = Matrix3x2.CreateTranslation(-rect.Left, -rect.Top) * Matrix3x2.CreateScale(texels, -texels);
        handle.RenderInRenderTarget(mask.Raw!, _drawMask, ClearColor);

        var radius = new Vector3(Feather, HardFeather, Feather) * texels;
        Blur(handle, mask.Raw!, mask.Across!, _blurH, new Vector2(1f / size.X, 0f), radius);
        Blur(handle, mask.Across!, mask.Blurred!, _blurV, new Vector2(0f, 1f / size.Y), radius);
        return mask;
    }

    private void EnsureTarget(ref IRenderTexture? target, Vector2i size, string name)
    {
        if (target != null && target.Texture.Size == size)
            return;

        target?.Dispose();
        target = _clyde.CreateRenderTarget(size,
            new RenderTargetFormatParameters(RenderTargetColorFormat.Rgba8Srgb),
            new TextureSampleParameters { Filter = true },
            name);
    }

    private static void Blur(DrawingHandleWorld handle, IRenderTexture from, IRenderTexture to, ShaderInstance shader, Vector2 step, Vector3 radius)
    {
        shader.SetParameter("texelStep", step);
        shader.SetParameter("radius", radius);
        handle.RenderInRenderTarget(to,
            () =>
            {
                handle.SetTransform(Matrix3x2.Identity);
                handle.UseShader(shader);
                handle.DrawTextureRect(from.Texture, Box2.FromDimensions(Vector2.Zero, (Vector2) to.Size));
                handle.UseShader(null);
            },
            ClearColor);
    }

    /// <summary>Draws the mask. Red is cover that fades softly, green is walls that cut hard, blue is open ground.</summary>
    private void DrawMask()
    {
        var handle = _maskHandle;
        handle.UseShader(_unshaded);
        _grids.Clear();
        _map.FindGridsIntersecting(_maskMapId, _maskRect, ref _grids);

        // Grids that are always under cover, such as a landed vessel, go last so their tiles win over the ground beneath.
        for (var pass = 0; pass < 2; pass++)
        {
            foreach (var grid in _grids)
            {
                if (_wolfWeather.IsCoverGrid(grid.Owner) == (pass == 0))
                    continue;

                DrawMaskGrid(handle, grid);
            }
        }
    }

    private void DrawMaskGrid(DrawingHandleWorld handle, Entity<MapGridComponent> grid)
    {
        handle.SetTransform(Matrix3x2.Multiply(_xform.GetWorldMatrix(grid.Owner), _maskMatrix));
        _roofQuery.TryComp(grid.Owner, out var roof);
        var roofed = new Entity<MapGridComponent, RoofComponent?>(grid.Owner, grid.Comp, roof);
        _rects.Clear();
        _diagonalTiles.Clear();

        var tiles = _map.GetTilesIntersecting(grid.Owner, grid, _maskRect);
        while (tiles.MoveNext(out var tile))
        {
            if (MaskColor(roofed, tile) is { } color)
                _rects.Add(new WorldRect(_lookup.GetLocalBounds(tile, grid.Comp.TileSize), color));
        }

        handle.DrawRectsUnmodulated(CollectionsMarshal.AsSpan(_rects));
        MaskDiagonals(handle, grid);
    }

    /// <summary>Open ground in blue, walls in green, other cover in red. Diagonal edge tiles are left for MaskDiagonals.</summary>
    private Color? MaskColor(Entity<MapGridComponent, RoofComponent?> grid, TileRef tile)
    {
        if (tile.Tile.IsEmpty)
            return null;

        var flags = _wolfWeather.Classify(grid, tile);
        if ((flags & WolfWeatherTile.Exposed) != 0)
            return Open;

        if ((flags & WolfWeatherTile.ExposedPastDiagonals) != 0 && BordersExposed(grid, tile))
        {
            _diagonalTiles.Add(tile.GridIndices);
            return null;
        }

        return (flags & WolfWeatherTile.Blocker) != 0 ? Hard : Soft;
    }

    private bool BordersExposed(Entity<MapGridComponent, RoofComponent?> grid, TileRef tile)
    {
        foreach (var dir in Cardinals)
        {
            if (_wolfWeather.IsExposed(grid, _map.GetTileRef(grid.Owner, grid.Comp1, tile.GridIndices + dir)))
                return true;
        }

        return false;
    }

    /// <summary>Adds the wall half of each diagonal blocker on the grid's collected edge tiles to the mask.</summary>
    private void MaskDiagonals(DrawingHandleWorld handle, Entity<MapGridComponent> grid)
    {
        foreach (var indices in _diagonalTiles)
        {
            var size = grid.Comp.TileSize;
            var sw = (Vector2) indices * size;
            var se = sw + new Vector2(size, 0);
            var ne = sw + new Vector2(size, size);
            var nw = sw + new Vector2(0, size);

            var anchored = _map.GetAnchoredEntities(grid.Owner, grid.Comp, indices);
            while (anchored.MoveNext(out var ent))
            {
                if (!_blockQuery.HasComp(ent.Value) || !_tag.HasTag(ent.Value, SharedWolfWeatherSystem.DiagonalTag))
                    continue;

                switch (_xformQuery.GetComponent(ent.Value).LocalRotation.GetCardinalDir())
                {
                    case Direction.South: _wallHalves.Add(sw); _wallHalves.Add(se); _wallHalves.Add(ne); break;
                    case Direction.East: _wallHalves.Add(se); _wallHalves.Add(ne); _wallHalves.Add(nw); break;
                    case Direction.North: _wallHalves.Add(ne); _wallHalves.Add(nw); _wallHalves.Add(sw); break;
                    case Direction.West: _wallHalves.Add(nw); _wallHalves.Add(sw); _wallHalves.Add(se); break;
                }
            }
        }

        if (_wallHalves.Count > 0)
            handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, _wallHalves, Hard);

        _wallHalves.Clear();
        _diagonalTiles.Clear();
    }

    private sealed class MaskTargets : IDisposable
    {
        public IRenderTexture? Raw;
        public IRenderTexture? Across;
        public IRenderTexture? Blurred;
        public MapId MapId;
        public Box2 Rect;
        public int Version;
        public TimeSpan BuiltAt;

        public void Dispose()
        {
            Raw?.Dispose();
            Across?.Dispose();
            Blurred?.Dispose();
        }
    }
}
