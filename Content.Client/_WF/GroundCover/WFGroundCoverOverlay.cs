using System.Numerics;
using System.Runtime.InteropServices;
using Content.Client._WF.WolfWeather;
using Content.Client.Graphics;
using Content.Client.Viewport;
using Content.Shared._WF.GroundCover;
using Content.Shared._WF.WolfWeather;
using Content.Shared.Light.Components;
using Robust.Client.Graphics;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Graphics;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._WF.GroundCover;

/// <summary>
/// Draws wet open ground from the floor under it, puddles that mirror the scene with raindrop ripples, and snow cover,
/// over the floor and under everything else. Tiles under a vessel are left out of the mask, so nothing is drawn on a ship
/// that sits on the ground.
/// </summary>
public sealed partial class WFGroundCoverOverlay : Overlay
{
    private static readonly ProtoId<ShaderPrototype> Shader = "WFGroundCover";
    private static readonly ProtoId<ShaderPrototype> UnshadedShader = "unshaded";
    private static readonly ProtoId<ShaderPrototype> BlurShader = "WFWolfWeatherBlur";

    /// <summary>Mask texels per tile; the linear filter between them softens edges by about a quarter tile.</summary>
    private const float TexelsPerTile = 4f;

    private const int MaxMaskSize = 512;

    /// <summary>Texels per tile of the baked puddle heights, which edge the puddles and want more than the mask.</summary>
    private const float HeightTexelsPerTile = 8f;

    private const int MaxHeightSize = 768;

    /// <summary>How far snow edges are softened, in tiles: the blur that rounds tile corners into soft, organic edges.</summary>
    private const float SnowSoftTiles = 0.6f;

    /// <summary>Tiles drawn past the wet and snowy ones, for the soft edge of snow.</summary>
    private const float ContentMargin = 1f;
    private const float MaskMargin = 4f;
    private const float MaskSnap = 4f;

    /// <summary>World coordinates are wrapped by this before noise, which repeats on it.</summary>
    private const float NoisePeriod = 1024f;

    private const double TimePeriod = 3600.0;

    /// <summary>Wetness below this draws nothing.</summary>
    private const float MinWetness = 0.01f;

    /// <summary>Quality that draws ripples, splashes and reflections.</summary>
    private const int FullQuality = 2;

    private static readonly TimeSpan MaskRefresh = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaskMinAge = TimeSpan.FromSeconds(0.25);

    private static readonly Color ClearColor = new(0f, 0f, 0f, 0f);

    [Dependency] private IClyde _clyde = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IEntityManager _entManager = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IGameTiming _timing = default!;

    private readonly WFGroundCoverSystem _cover;
    private readonly WolfWeatherSystem _wolfWeather;
    private readonly EntityLookupSystem _lookup;
    private readonly SharedMapSystem _map;
    private readonly SharedTransformSystem _xform;
    private readonly EntityQuery<WFGroundWeatherComponent> _groundQuery;
    private readonly EntityQuery<WolfWeatherMapComponent> _weatherQuery;
    private readonly EntityQuery<MapLightComponent> _lightQuery;
    private readonly EntityQuery<RoofComponent> _roofQuery;
    private readonly OverlayResourceCache<MaskTarget> _masks = new();
    private readonly OverlayResourceCache<FloorTarget> _floors = new();
    private readonly List<WorldRect> _rects = new();
    private List<Entity<MapGridComponent>> _grids = new();
    private readonly ShaderInstance _shader;
    private readonly ShaderInstance _reflectShader;
    private readonly ShaderInstance _unshaded;
    private readonly ShaderInstance _bake;
    private readonly ShaderInstance _blurH;
    private readonly ShaderInstance _blurV;
    private readonly Action _drawMask;
    private readonly Action _drawHeights;
    private readonly Action _copyFloor;
    private readonly Action _eraseCover;

    private DrawingHandleWorld _maskHandle = default!;
    private Matrix3x2 _maskMatrix;
    private MapId _maskMapId;
    private Box2 _maskRect;
    private bool _maskWet;
    private bool _anySnow;
    private Box2 _content;
    private bool _hasContent;

    private DrawingHandleWorld _heightHandle = default!;
    private Matrix3x2 _heightMatrix;
    private Box2 _heightRect;

    private DrawingHandleWorld _copyHandle = default!;
    private Texture _copySource = default!;
    private Vector2i _copySize;

    private int _coverStamp;
    private readonly List<Entity<MapGridComponent>> _coverGrids = new();

    private float _wetness;
    private float _puddles;
    private float _rain;
    private Vector3 _tint = new(WFGroundWeatherComponent.Water.R, WFGroundWeatherComponent.Water.G, WFGroundWeatherComponent.Water.B);
    private bool _wet;
    private bool _detail;

    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowEntities;

    /// <summary>The wet ground is drawn from the floor under it, so the floor is copied first, only while wet.</summary>
    public override bool RequestScreenTexture => _wet;

    /// <summary>How many times the ground shader drew, for tests.</summary>
    public int Draws { get; private set; }

    /// <summary>How many times the reflection shader drew, for tests.</summary>
    public int ReflectionDraws { get; private set; }

    public WFGroundCoverOverlay()
    {
        IoCManager.InjectDependencies(this);
        ZIndex = 10;
        _cover = _entManager.System<WFGroundCoverSystem>();
        _wolfWeather = _entManager.System<WolfWeatherSystem>();
        _lookup = _entManager.System<EntityLookupSystem>();
        _map = _entManager.System<SharedMapSystem>();
        _xform = _entManager.System<SharedTransformSystem>();
        _groundQuery = _entManager.GetEntityQuery<WFGroundWeatherComponent>();
        _weatherQuery = _entManager.GetEntityQuery<WolfWeatherMapComponent>();
        _lightQuery = _entManager.GetEntityQuery<MapLightComponent>();
        _roofQuery = _entManager.GetEntityQuery<RoofComponent>();
        _shader = _proto.Index(Shader).InstanceUnique();
        _reflectShader = _proto.Index(Shader).InstanceUnique();
        _unshaded = _proto.Index(UnshadedShader).Instance();
        _bake = _proto.Index(Shader).InstanceUnique();
        _blurH = _proto.Index(BlurShader).InstanceUnique();
        _blurV = _proto.Index(BlurShader).InstanceUnique();
        _drawMask = DrawMask;
        _drawHeights = DrawHeights;
        _copyFloor = CopyFloor;
        _eraseCover = EraseCover;
    }

    protected override void DisposeBehavior()
    {
        _masks.Dispose();
        _floors.Dispose();
        _shader.Dispose();
        _reflectShader.Dispose();
        _bake.Dispose();
        _blurH.Dispose();
        _blurV.Dispose();
        base.DisposeBehavior();
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        GetFloor(args.Viewport).Ready = false;

        // Only the view's own level: z-levels drawn under or over it would stack a second layer of wet ground.
        if (args.MapId == MapId.Nullspace || args.Viewport.Eye is ScalingViewport.ZEye { Primary: false })
        {
            _wet = false;
            return false;
        }

        var wet = UpdateWeather(args.MapId);
        _anySnow = ScanGrids(args);
        if (!wet && !_anySnow)
            return false;

        // A fresh mask with nothing wet or snowy in view needs no pass and no copy of the screen.
        var mask = _masks.GetForViewport(args.Viewport, static _ => new MaskTarget());
        return !MaskFresh(mask, args, _wet) || mask.HasContent;
    }

    /// <summary>Reads a map's wetness, puddles and rain at the current quality; true if wet ground draws.</summary>
    public bool UpdateWeather(MapId mapId)
    {
        var quality = _wolfWeather.Quality;
        var mapUid = _map.GetMapOrInvalid(mapId);
        _wetness = 0f;
        _puddles = 0f;
        _rain = 0f;
        if (quality > 0 && _wolfWeather.IsSurface(mapUid) && _groundQuery.TryComp(mapUid, out var ground))
        {
            _wetness = ground.Wetness;
            _puddles = ground.Puddles;
            _rain = GetRain(mapUid);
            _tint = new Vector3(ground.Tint.R, ground.Tint.G, ground.Tint.B);
        }

        _detail = quality >= FullQuality;
        _wet = _wetness >= MinWetness;
        return _wet;
    }

    /// <summary>Rain read by the last <see cref="UpdateWeather"/>, which drives the ripples and splashes.</summary>
    public float Rain => _rain;

    /// <summary>Whether the last <see cref="UpdateWeather"/> found Full quality, with ripples, splashes and reflections.</summary>
    public bool Detail => _detail;

    /// <summary>The puddle tint read by the last <see cref="UpdateWeather"/>.</summary>
    public Vector3 Tint => _tint;

    /// <summary>Rain falling on a map now, as ground wetness at full strength summed over its weathers.</summary>
    public float GetRain(EntityUid map)
    {
        if (!_weatherQuery.TryComp(map, out var weather))
            return 0f;

        var now = _timing.CurTime;
        var rain = 0f;
        foreach (var entry in weather.Weathers)
        {
            if (_proto.TryIndex(entry.Weather, out var proto))
                rain += SharedWolfWeatherSystem.GetStrength(entry, proto, now) * proto.GroundWetness;
        }

        return Math.Clamp(rain, 0f, 1f);
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (_wet && ScreenTexture == null)
            return;

        var mask = GetMask(args, _wet);
        var maskTexture = mask.Target!.Texture;
        var floor = GetFloor(args.Viewport);

        // Only the part of the view that has wet ground or snow in it is drawn.
        if (!mask.HasContent || !TryGetArea(args, mask, out var area))
            return;

        EnsureHeights(args, mask);

        // Full quality keeps the bare floor, so the reflection pass after entities can find the water nothing covers.
        if (_wet && _detail && _puddles > 0f && CopyFloor(args, floor))
        {
            floor.Ready = true;
            floor.MapId = args.MapId;
        }

        SetCommon(_shader, args, mask, area);
        _shader.SetParameter("pass", 0f);
        _shader.SetParameter("SCREEN_TEXTURE", _wet ? ScreenTexture! : maskTexture);
        _shader.SetParameter("floorTex", maskTexture);

        var handle = args.WorldHandle;
        handle.SetTransform(Matrix3x2.Identity);
        handle.UseShader(_shader);
        handle.DrawRect(area, Color.White);
        handle.UseShader(null);
        Draws++;
    }

    /// <summary>Whether this viewport kept its floor this frame, so puddle reflections can be drawn on it.</summary>
    public bool WantsReflections(in OverlayDrawArgs args)
    {
        var floor = GetFloor(args.Viewport);
        return floor.Ready && floor.MapId == args.MapId;
    }

    /// <summary>
    /// Mirrors what stands above each puddle into it, on water nothing has been drawn over. Runs after entities, with
    /// the whole scene as the screen texture.
    /// </summary>
    public void DrawReflections(in OverlayDrawArgs args, Texture scene)
    {
        var floor = GetFloor(args.Viewport);
        var mask = _masks.GetForViewport(args.Viewport, static _ => new MaskTarget());
        if (!floor.Ready || floor.Target == null || mask.Target == null || !mask.HasContent
            || !TryGetArea(args, mask, out var area))
            return;

        SetCommon(_reflectShader, args, mask, area);
        _reflectShader.SetParameter("pass", 1f);
        _reflectShader.SetParameter("SCREEN_TEXTURE", scene);
        _reflectShader.SetParameter("floorTex", floor.Target.Texture);

        var handle = args.WorldHandle;
        handle.SetTransform(Matrix3x2.Identity);
        handle.UseShader(_reflectShader);
        handle.DrawRect(area, Color.White);
        handle.UseShader(null);
        ReflectionDraws++;
    }

    private FloorTarget GetFloor(IClydeViewport viewport)
    {
        return _floors.GetForViewport(viewport, static _ => new FloorTarget());
    }

    /// <summary>Copies the floor the screen texture holds into the viewport's own target.</summary>
    private bool CopyFloor(in OverlayDrawArgs args, FloorTarget floor)
    {
        var size = args.Viewport.Size;
        if (size.X <= 0 || size.Y <= 0)
            return false;

        if (floor.Target == null || floor.Target.Texture.Size != size)
        {
            floor.Target?.Dispose();
            floor.Target = _clyde.CreateRenderTarget(size,
                new RenderTargetFormatParameters(RenderTargetColorFormat.Rgba8Srgb),
                new TextureSampleParameters { Filter = false },
                "wf-ground-cover-floor");
        }

        _copyHandle = args.WorldHandle;
        _copySource = ScreenTexture!;
        _copySize = size;
        args.WorldHandle.RenderInRenderTarget(floor.Target, _copyFloor, null);
        return true;
    }

    private void CopyFloor()
    {
        // The target's pixel space runs top-down, so the copy is flipped back to line up with the screen texture.
        _copyHandle.UseShader(_unshaded);
        _copyHandle.SetTransform(Matrix3x2.CreateScale(1f, -1f) * Matrix3x2.CreateTranslation(0f, _copySize.Y));
        _copyHandle.DrawTextureRect(_copySource, new Box2(0f, 0f, _copySize.X, _copySize.Y));
        _copyHandle.UseShader(null);
    }

    /// <summary>Parameters both passes share, so the reflection pass can redo the ground pass exactly.</summary>
    private void SetCommon(ShaderInstance shader, in OverlayDrawArgs args, MaskTarget mask, Box2 bounds)
    {
        var corner = bounds.BottomLeft;
        var viewport = args.Viewport;
        var eye = viewport.Eye;
        var eyePos = eye?.Position.Position ?? bounds.Center;
        var scale = EyeManager.PixelsPerMeter * viewport.RenderScale.X * (eye?.Scale.X ?? 1f);
        var down = _wolfWeather.Down;
        var up = MathF.Abs(down.X) > MathF.Abs(down.Y)
            ? new Vector2(-MathF.Sign(down.X), 0f)
            : new Vector2(0f, down.Y > 0f ? -1f : 1f);
        var centre = bounds.Center;
        var uv = ToUv(viewport, centre);

        shader.SetParameter("mask", mask.Target!.Texture);
        shader.SetParameter("heights", (mask.HeightsReady ? mask.Heights : mask.Target)!.Texture);
        shader.SetParameter("useHeights", mask.HeightsReady ? 1f : 0f);
        shader.SetParameter("maskOffset", corner - mask.Rect.BottomLeft);
        shader.SetParameter("maskSize", mask.Rect.Size);
        shader.SetParameter("size", bounds.Size);
        shader.SetParameter("origin", new Vector2(Wrap(corner.X), Wrap(corner.Y)));
        shader.SetParameter("wetness", _wetness);
        shader.SetParameter("puddles", _puddles);
        shader.SetParameter("rain", _rain);
        shader.SetParameter("detail", _detail ? 1f : 0f);
        shader.SetParameter("time", (float) (_timing.RealTime.TotalSeconds % TimePeriod));
        shader.SetParameter("ambient", GetAmbient(args.MapUid));
        shader.SetParameter("eye", eyePos - corner);
        shader.SetParameter("halfView", Vector2.Max(args.WorldAABB.Size * 0.5f, Vector2.One));
        shader.SetParameter("up", up);
        shader.SetParameter("pixel", scale > 0f ? 1f / scale : 1f / EyeManager.PixelsPerMeter);
        shader.SetParameter("uvDx", ToUv(viewport, centre + Vector2.UnitX) - uv);
        shader.SetParameter("uvDy", ToUv(viewport, centre + Vector2.UnitY) - uv);
        shader.SetParameter("puddleOpacity", Math.Clamp(_cfg.GetCVar(WFGroundCoverCVars.PuddleOpacity), 0f, 1f));
        shader.SetParameter("puddleCoverage", MathF.Max(_cfg.GetCVar(WFGroundCoverCVars.PuddleCoverage), 0f));
        shader.SetParameter("reflection", MathF.Max(_cfg.GetCVar(WFGroundCoverCVars.Reflection), 0f));
        shader.SetParameter("sparkle", MathF.Max(_cfg.GetCVar(WFGroundCoverCVars.Sparkle), 0f));
        shader.SetParameter("wetDarkening", MathF.Max(_cfg.GetCVar(WFGroundCoverCVars.WetDarkening), 0f));
        shader.SetParameter("puddleEdge", MathF.Max(_cfg.GetCVar(WFGroundCoverCVars.PuddleEdge), 0.01f));
        shader.SetParameter("tint", _tint);
    }

    /// <summary>A world position's UV on the viewport's screen texture, which is bottom-up.</summary>
    private static Vector2 ToUv(IClydeViewport viewport, Vector2 world)
    {
        var local = viewport.WorldToLocal(world);
        return new Vector2(local.X / viewport.Size.X, 1f - local.Y / viewport.Size.Y);
    }

    private Vector3 GetAmbient(EntityUid map)
    {
        if (!_lightQuery.TryComp(map, out var light))
            return Vector3.One;

        var color = light.AmbientLightColor;
        return new Vector3(color.R, color.G, color.B);
    }

    private static float Wrap(float value)
    {
        return value - MathF.Floor(value / NoisePeriod) * NoisePeriod;
    }

    /// <summary>
    /// Looks over the grids in view: whether any holds snow, and a stamp of where the vessel-like ones are, so the mask is
    /// redrawn when a ship lands, lifts or turns.
    /// </summary>
    private bool ScanGrids(in OverlayDrawArgs args)
    {
        var snow = false;
        var stamp = 17;
        _grids.Clear();
        _map.FindGridsIntersecting(args.MapId, args.WorldAABB, ref _grids);
        foreach (var grid in _grids)
        {
            if (_wolfWeather.IsCoverGrid(grid.Owner))
            {
                var (position, rotation) = _xform.GetWorldPositionRotation(grid.Owner);
                stamp = unchecked(stamp * 31 + HashCode.Combine(grid.Owner.Id, (int) (position.X * 8f), (int) (position.Y * 8f),
                    (int) (rotation.Theta * 64.0)));
                continue;
            }

            snow |= _cover.HasSnow(grid.Owner);
        }

        _coverStamp = stamp;
        return snow;
    }

    /// <summary>Whether the viewport's mask still holds for this view, so it needn't be redrawn.</summary>
    private bool MaskFresh(MaskTarget mask, in OverlayDrawArgs args, bool wet)
    {
        var view = args.WorldAABB.Enlarged(MaskMargin * 0.5f);
        var real = _timing.RealTime;
        var version = MaskVersion();

        return mask.Target != null
               && mask.MapId == args.MapId
               && mask.Wet == wet
               && mask.Rect.Contains(view.BottomLeft)
               && mask.Rect.Contains(view.TopRight)
               && (mask.Version == version || real < mask.BuiltAt + MaskMinAge)
               && real < mask.BuiltAt + MaskRefresh;
    }

    private int MaskVersion()
    {
        return unchecked((_wolfWeather.MaskVersion * 31 + _cover.Version) * 31 + _coverStamp);
    }

    /// <summary>The part of the view with wet ground or snow in it, which is all that draws.</summary>
    private static bool TryGetArea(in OverlayDrawArgs args, MaskTarget mask, out Box2 area)
    {
        area = args.WorldAABB.Intersect(mask.Content);
        return area.Width > 0f && area.Height > 0f;
    }

    /// <summary>The viewport's mask of open ground and snow depth, redrawn when the view leaves it, things change or it ages.</summary>
    private MaskTarget GetMask(in OverlayDrawArgs args, bool wet)
    {
        var mask = _masks.GetForViewport(args.Viewport, static _ => new MaskTarget());
        if (MaskFresh(mask, args, wet))
            return mask;

        var real = _timing.RealTime;
        var outer = args.WorldAABB.Enlarged(MaskMargin);
        var rect = new Box2(
            MathF.Floor(outer.Left / MaskSnap) * MaskSnap,
            MathF.Floor(outer.Bottom / MaskSnap) * MaskSnap,
            MathF.Ceiling(outer.Right / MaskSnap) * MaskSnap,
            MathF.Ceiling(outer.Top / MaskSnap) * MaskSnap);

        var texels = MathF.Min(TexelsPerTile, MaxMaskSize / MathF.Max(rect.Width, rect.Height));
        var size = new Vector2i((int) MathF.Ceiling(rect.Width * texels), (int) MathF.Ceiling(rect.Height * texels));
        EnsureTarget(ref mask.Target, size, "wf-ground-cover-mask");
        if (_anySnow)
        {
            EnsureTarget(ref mask.Raw, size, "wf-ground-cover-mask-raw");
            EnsureTarget(ref mask.Across, size, "wf-ground-cover-mask-across");
        }

        mask.MapId = args.MapId;
        mask.Rect = rect;
        mask.Version = MaskVersion();
        mask.BuiltAt = real;
        mask.Wet = wet;

        _maskHandle = args.WorldHandle;
        _maskMapId = args.MapId;
        _maskRect = rect;
        _maskWet = wet;
        _hasContent = false;
        _coverGrids.Clear();
        _maskMatrix = Matrix3x2.CreateTranslation(-rect.Left, -rect.Top) * Matrix3x2.CreateScale(texels, -texels);

        // Snow is drawn sharp, then blurred so its edges round off; with none in view the mask is drawn as it is.
        args.WorldHandle.RenderInRenderTarget(_anySnow ? mask.Raw! : mask.Target!, _drawMask, ClearColor);
        if (_anySnow)
        {
            var radius = new Vector3(0f, 1f, 1f) * MathF.Max(1f, SnowSoftTiles * texels);
            Blur(args.WorldHandle, mask.Raw!, mask.Across!, _blurH, new Vector2(1f / size.X, 0f), radius);
            Blur(args.WorldHandle, mask.Across!, mask.Target!, _blurV, new Vector2(0f, 1f / size.Y), radius);
        }

        // Last, so the blur can't carry snow onto a ship's edge: a vessel's tiles are blanked, whatever lies below them.
        if (_hasContent && _coverGrids.Count > 0)
            args.WorldHandle.RenderInRenderTarget(mask.Target!, _eraseCover, null);

        mask.HasContent = _hasContent;
        mask.Content = _hasContent ? _content.Enlarged(ContentMargin) : default;
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

    /// <summary>Bakes the puddle height field over the mask's rect, once for each rect, when puddles are drawn.</summary>
    private void EnsureHeights(in OverlayDrawArgs args, MaskTarget mask)
    {
        if (!_wet || _puddles <= 0f)
            return;

        var rect = mask.Rect;
        if (mask.HeightsReady && mask.HeightRect == rect)
            return;

        var texels = MathF.Min(HeightTexelsPerTile, MaxHeightSize / MathF.Max(rect.Width, rect.Height));
        var size = new Vector2i((int) MathF.Ceiling(rect.Width * texels), (int) MathF.Ceiling(rect.Height * texels));
        if (mask.Heights == null || mask.Heights.Texture.Size != size)
        {
            mask.Heights?.Dispose();
            mask.Heights = _clyde.CreateRenderTarget(size,
                new RenderTargetFormatParameters(RenderTargetColorFormat.Rgba16F),
                new TextureSampleParameters { Filter = true },
                "wf-ground-cover-heights");
        }

        _bake.SetParameter("pass", 2f);
        _bake.SetParameter("size", rect.Size);
        _bake.SetParameter("origin", new Vector2(Wrap(rect.Left), Wrap(rect.Bottom)));

        _heightHandle = args.WorldHandle;
        _heightRect = rect;
        _heightMatrix = Matrix3x2.CreateTranslation(-rect.Left, -rect.Top) * Matrix3x2.CreateScale(texels, -texels);
        args.WorldHandle.RenderInRenderTarget(mask.Heights, _drawHeights, ClearColor);
        mask.HeightRect = rect;
        mask.HeightsReady = true;
    }

    private void DrawHeights()
    {
        var handle = _heightHandle;
        handle.SetTransform(_heightMatrix);
        handle.UseShader(_bake);
        handle.DrawRect(_heightRect, Color.White);
        handle.UseShader(null);
    }

    /// <summary>Draws the mask: red where weather reaches the ground, green for snow depth, blue where snow lies.</summary>
    private void DrawMask()
    {
        var handle = _maskHandle;
        handle.UseShader(_unshaded);
        _grids.Clear();
        _map.FindGridsIntersecting(_maskMapId, _maskRect, ref _grids);

        foreach (var grid in _grids)
        {
            if (_wolfWeather.IsCoverGrid(grid.Owner))
            {
                _coverGrids.Add(grid);
                continue;
            }

            var snow = _cover.HasSnow(grid.Owner);
            if (!_maskWet && !snow)
                continue;

            handle.SetTransform(Matrix3x2.Multiply(_xform.GetWorldMatrix(grid.Owner), _maskMatrix));
            _roofQuery.TryComp(grid.Owner, out var roof);
            var roofed = new Entity<MapGridComponent, RoofComponent?>(grid.Owner, grid.Comp, roof);
            _rects.Clear();
            var any = false;
            var local = default(Box2);

            var tiles = _map.GetTilesIntersecting(grid.Owner, grid, _maskRect);
            while (tiles.MoveNext(out var tile))
            {
                var open = 0f;
                if (_maskWet)
                {
                    var flags = _wolfWeather.Classify(roofed, tile);
                    if ((flags & WolfWeatherTile.Exposed) != 0 && (flags & WolfWeatherTile.Blocker) == 0)
                        open = 1f;
                }

                var depth = snow ? _cover.GetDepth(grid.Owner, tile.GridIndices) / (float) WFSnowCover.MaxDepth : 0f;
                if (open <= 0f && depth <= 0f)
                    continue;

                var bounds = _lookup.GetLocalBounds(tile, grid.Comp.TileSize);
                _rects.Add(new WorldRect(bounds, new Color(open, depth, depth > 0f ? 1f : 0f)));
                local = any ? local.Union(bounds) : bounds;
                any = true;
            }

            handle.DrawRectsUnmodulated(CollectionsMarshal.AsSpan(_rects));
            if (any)
                GrowContent(_xform.GetWorldMatrix(grid.Owner).TransformBox(local));
        }

        handle.UseShader(null);
    }

    /// <summary>Blanks the mask under every tile of the vessels the mask pass met, onto the finished mask.</summary>
    private void EraseCover()
    {
        var handle = _maskHandle;
        handle.UseShader(_unshaded);
        var black = new Color(0f, 0f, 0f, 1f);
        foreach (var grid in _coverGrids)
        {
            handle.SetTransform(Matrix3x2.Multiply(_xform.GetWorldMatrix(grid.Owner), _maskMatrix));
            _rects.Clear();
            var tiles = _map.GetTilesIntersecting(grid.Owner, grid, _maskRect);
            while (tiles.MoveNext(out var tile))
            {
                _rects.Add(new WorldRect(_lookup.GetLocalBounds(tile, grid.Comp.TileSize), black));
            }

            handle.DrawRectsUnmodulated(CollectionsMarshal.AsSpan(_rects));
        }

        handle.SetTransform(Matrix3x2.Identity);
        handle.UseShader(null);
    }

    /// <summary>Grows the box of wet and snowy ground, in world space.</summary>
    private void GrowContent(Box2 world)
    {
        _content = _hasContent ? _content.Union(world) : world;
        _hasContent = true;
    }

    /// <summary>One viewport's copy of the bare floor from this frame's ground pass.</summary>
    private sealed class FloorTarget : IDisposable
    {
        public IRenderTexture? Target;
        public MapId MapId;
        public bool Ready;

        public void Dispose()
        {
            Target?.Dispose();
        }
    }

    private sealed class MaskTarget : IDisposable
    {
        public IRenderTexture? Target;
        public IRenderTexture? Raw;
        public IRenderTexture? Across;
        public IRenderTexture? Heights;
        public MapId MapId;
        public Box2 Rect;
        public Box2 HeightRect;
        public bool HeightsReady;
        public Box2 Content;
        public bool HasContent;
        public int Version;
        public TimeSpan BuiltAt;
        public bool Wet;

        public void Dispose()
        {
            Target?.Dispose();
            Raw?.Dispose();
            Across?.Dispose();
            Heights?.Dispose();
        }
    }
}
