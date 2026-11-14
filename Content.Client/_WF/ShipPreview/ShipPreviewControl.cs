using System.Numerics;
using Content.Client.Viewport;
using Content.Shared._NF.Shipyard.Prototypes;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Graphics;
using Robust.Shared.Input;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client._WF.ShipPreview;

/// <summary>
/// Draws a ship grid from <see cref="ShipPreviewSystem"/> inside the control, full-bright, with drag to pan and
/// wheel to zoom. Purely client-side: nothing here touches the player's own eye or the server.
/// </summary>
public sealed partial class ShipPreviewControl : Control
{
    [Dependency] private IClyde _clyde = default!;
    [Dependency] private IEntityManager _entMan = default!;
    [Dependency] private IPrototypeManager _protoMan = default!;

    /// <summary>Fraction of the control the fitted grid leaves free on each axis.</summary>
    private const float FitMargin = 1.1f;

    private const float ZoomStep = 1.25f;
    private const float AbsoluteMinZoom = 0.02f;
    private const float AbsoluteMaxZoom = 64f;

    /// <summary>Frames rendered after a load, so late sprite work lands in the cached texture.</summary>
    private const int LoadRenderFrames = 3;

    private static readonly Color Background = new(0.05f, 0.05f, 0.07f);

    private readonly Label _status;

    // A ZEye keeps the fork's skybox, vignette and z-blur overlays out of this viewport; they all bail on one that
    // is not the player's own. Depth 0 with parallax off is "flat, plain background".
    private readonly ScalingViewport.ZEye _eye = new(0f, 0f, 0f)
    {
        DrawLight = false,
        DrawFov = false,
        DrawParallax = false,
        Rotation = Angle.Zero,
    };

    private IClydeViewport? _viewport;

    /// <summary>
    /// Something to show: a vessel, or grid YAML under a key.
    /// </summary>
    private sealed record PreviewSource(VesselPrototype? Vessel, string? Key, string? Yaml, string? Name);

    private PreviewSource? _shown;
    private PreviewSource? _pending;
    private bool _loadQueued;
    private int _loadDelay;

    private EntityUid _grid;
    private Box2 _bounds;
    private bool _hasGrid;
    private ShipPreviewHandle? _handle;

    private float _zoom = 1f;
    private float _minZoom = AbsoluteMinZoom;
    private float _maxZoom = AbsoluteMaxZoom;

    private bool _fitQueued;
    private bool _dragging;
    private Vector2 _dragAnchor;
    private int _renderFrames;

    /// <summary>Tiles on the previewed grid. Counted once per load.</summary>
    public int TileCount { get; private set; }

    /// <summary>Bounding box of the previewed grid, in tiles.</summary>
    public Vector2i GridSize { get; private set; }

    /// <summary>Estimated strongest planet gravity, in g, the grid lifts off in; see <see cref="ShipPreviewLift"/>.</summary>
    public float MaxLiftGravity { get; private set; }

    /// <summary>Whether a grid is currently loaded and drawn.</summary>
    public bool HasPreview => _hasGrid;

    /// <summary>Raised after a load finishes or fails, so the host window can refresh its labels.</summary>
    public event Action? PreviewUpdated;

    public ShipPreviewControl()
    {
        IoCManager.InjectDependencies(this);

        RectClipContent = true;
        MouseFilter = MouseFilterMode.Stop;

        _status = new Label
        {
            HorizontalAlignment = HAlignment.Center,
            VerticalAlignment = VAlignment.Center,
            Align = Label.AlignMode.Center,
            Text = Loc.GetString("wf-ship-preview-no-vessel"),
        };

        AddChild(_status);
    }

    /// <summary>
    /// Shows a vessel, or clears the preview when given null. The load itself happens on the next frame so the
    /// loading label gets a chance to draw first.
    /// </summary>
    public void SetVessel(VesselPrototype? vessel)
    {
        if (vessel == null)
        {
            ShowMessage("wf-ship-preview-no-vessel");
            return;
        }

        Queue(new PreviewSource(vessel, null, null, null));
    }

    /// <summary>
    /// Shows a grid from YAML text. <paramref name="key"/> identifies it, so the same key isn't loaded twice.
    /// </summary>
    public void SetGridText(string key, string yaml, string? name)
    {
        Queue(new PreviewSource(null, key, yaml, name));
    }

    /// <summary>
    /// Clears the preview and shows a message in its place.
    /// </summary>
    public void ShowMessage(string locId)
    {
        _pending = null;
        _loadQueued = false;
        ClearPreview(locId);
        PreviewUpdated?.Invoke();
    }

    private void Queue(PreviewSource source)
    {
        // Already showing it, or already queued to show it.
        if (_loadQueued && Same(_pending, source))
            return;
        if (!_loadQueued && _hasGrid && Same(_shown, source))
            return;

        _pending = source;
        _loadQueued = true;
        // Skip one frame update so the loading label gets drawn before the load stalls the frame.
        _loadDelay = 1;
        SetStatus("wf-ship-preview-loading");
    }

    /// <summary>
    /// Centres the view on the grid and picks the zoom that fits it with a small margin.
    /// </summary>
    public void FitToGrid()
    {
        if (!_hasGrid)
            return;

        var size = PixelSize;
        if (size.X <= 0 || size.Y <= 0)
        {
            // Nothing to fit into yet; try again once the control has a size.
            _fitQueued = true;
            return;
        }

        var extents = Vector2.Max(_bounds.Size, Vector2.One) * FitMargin;
        var fit = MathF.Max(
            extents.X * EyeManager.PixelsPerMeter / size.X,
            extents.Y * EyeManager.PixelsPerMeter / size.Y);

        _minZoom = Math.Clamp(fit / 16f, AbsoluteMinZoom, AbsoluteMaxZoom);
        _maxZoom = Math.Clamp(fit * 4f, _minZoom, AbsoluteMaxZoom);

        SetZoom(fit);
        _eye.Position = new MapCoordinates(_bounds.Center, _eye.Position.MapId);

        _fitQueued = false;
        MarkDirty();
    }

    /// <summary>
    /// Drops this control's claim on the preview map. Call it when the host window closes.
    /// </summary>
    public void Release()
    {
        ClearPreview("wf-ship-preview-no-vessel");

        if (_handle == null)
            return;

        _entMan.System<ShipPreviewSystem>().Release(_handle);
        _handle = null;
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        // The grid can go away under us on disconnect or round restart.
        if (_hasGrid && !_entMan.EntityExists(_grid))
            ClearPreview("wf-ship-preview-no-vessel");

        if (!_loadQueued)
            return;

        if (_loadDelay > 0)
        {
            _loadDelay--;
            return;
        }

        _loadQueued = false;
        if (_pending is { } source)
            Load(source);
    }

    private static bool Same(PreviewSource? a, PreviewSource? b)
    {
        return a != null && b != null && a.Vessel == b.Vessel && a.Key == b.Key;
    }

    private void Load(PreviewSource source)
    {
        var system = _entMan.System<ShipPreviewSystem>();

        _handle ??= system.Acquire();

        var loaded = source.Vessel is { } vessel
            ? system.TryLoad(_handle, vessel, out var preview)
            : system.TryLoadText(_handle, new ResPath(source.Key!), source.Yaml!, source.Name, out preview);

        if (!loaded)
        {
            ClearPreview("wf-ship-preview-failed");
            PreviewUpdated?.Invoke();
            return;
        }

        _shown = source;
        _grid = preview.Grid.Owner;
        _bounds = preview.LocalBounds;
        _hasGrid = true;
        TileCount = preview.TileCount;
        GridSize = preview.SizeInTiles;
        MaxLiftGravity = ShipPreviewLift.MaxLiftGravity(_entMan, _protoMan, _grid, TileCount);

        _status.Visible = false;
        _eye.Position = new MapCoordinates(_bounds.Center, _handle.PreviewMap);

        FitToGrid();
        MarkDirty(LoadRenderFrames);
        PreviewUpdated?.Invoke();
    }

    private void ClearPreview(string status)
    {
        // The handle is ours alone, so this can never touch another previewer's grid.
        if (_hasGrid && _handle != null)
            _entMan.System<ShipPreviewSystem>().Clear(_handle);

        _shown = null;
        _grid = EntityUid.Invalid;
        _hasGrid = false;
        _dragging = false;
        TileCount = 0;
        GridSize = Vector2i.Zero;
        MaxLiftGravity = 0f;
        SetStatus(status);
    }

    private void SetStatus(string locId)
    {
        _status.Text = Loc.GetString(locId);
        _status.Visible = true;
    }

    protected override void Draw(IRenderHandle handle)
    {
        base.Draw(handle);

        if (!_hasGrid)
            return;

        var size = PixelSize;
        if (size.X <= 0 || size.Y <= 0)
            return;

        // Follows the control's pixel size, which already includes UI scale. Only rebuilt when it really changes.
        if (_viewport == null || _viewport.Size != size)
        {
            _viewport?.Dispose();
            _viewport = _clyde.CreateViewport(size, new TextureSampleParameters { Filter = false }, "wf-ship-preview");
            _viewport.Eye = _eye;
            _viewport.ClearColor = Background;
            MarkDirty(LoadRenderFrames);
        }

        if (_fitQueued)
            FitToGrid();

        // The map is paused and nothing on it moves, so the render target is only refreshed after a change.
        if (_renderFrames > 0)
        {
            _renderFrames--;
            _viewport.Render();
        }

        handle.DrawingHandleScreen.DrawTextureRect(
            _viewport.RenderTarget.Texture,
            UIBox2.FromDimensions(Vector2.Zero, size));
    }

    protected override void ExitedTree()
    {
        base.ExitedTree();

        // Also fires while the window is being moved into a popped-out OS window; the viewport is rebuilt on the
        // next draw, so there is nothing to restore here.
        _viewport?.Dispose();
        _viewport = null;
        _dragging = false;
    }

    protected override void EnteredTree()
    {
        base.EnteredTree();

        MarkDirty(LoadRenderFrames);
    }

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        base.KeyBindDown(args);

        if (!_hasGrid || _viewport == null)
            return;

        if (args.Function != EngineKeyFunctions.UIClick && args.Function != EngineKeyFunctions.UIRightClick)
            return;

        _dragging = true;
        _dragAnchor = _viewport.LocalToWorld(args.RelativePixelPosition).Position;
        args.Handle();
    }

    protected override void KeyBindUp(GUIBoundKeyEventArgs args)
    {
        base.KeyBindUp(args);

        if (args.Function != EngineKeyFunctions.UIClick && args.Function != EngineKeyFunctions.UIRightClick)
            return;

        _dragging = false;
        args.Handle();
    }

    protected override void MouseMove(GUIMouseMoveEventArgs args)
    {
        base.MouseMove(args);

        if (!_dragging || !_hasGrid || _viewport == null)
            return;

        // Move the eye so the world point grabbed on mouse-down stays under the cursor. Reading it back through the
        // viewport covers zoom and eye rotation without repeating the maths.
        var delta = _dragAnchor - _viewport.LocalToWorld(args.RelativePixelPosition).Position;
        if (delta == Vector2.Zero)
            return;

        _eye.Position = new MapCoordinates(_eye.Position.Position + delta, _eye.Position.MapId);
        MarkDirty();
    }

    protected override void MouseWheel(GUIMouseWheelEventArgs args)
    {
        base.MouseWheel(args);

        if (!_hasGrid || _viewport == null || args.Delta.Y == 0f)
            return;

        // Handled either way, so a surrounding scroll container does not scroll while zooming.
        args.Handle();

        var local = args.RelativePixelPosition;
        var before = _viewport.LocalToWorld(local).Position;
        SetZoom(_zoom * MathF.Pow(ZoomStep, -args.Delta.Y));
        var after = _viewport.LocalToWorld(local).Position;

        _eye.Position = new MapCoordinates(_eye.Position.Position + (before - after), _eye.Position.MapId);
        MarkDirty();
    }

    private void SetZoom(float zoom)
    {
        _zoom = Math.Clamp(zoom, _minZoom, _maxZoom);
        _eye.Zoom = new Vector2(_zoom, _zoom);
    }

    private void MarkDirty(int frames = 1)
    {
        _renderFrames = Math.Max(_renderFrames, frames);
    }
}
