using Content.Client.Shuttles.UI;
using Content.Client.UserInterface.Controls;
using Content.Client.Viewport;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.Player;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Shared.Input;
using Robust.Shared.Map;
using Robust.Shared.Timing;
using Robust.Shared.Utility;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Content.Client._WF.Cockpit;

/// <summary>Routes cockpit aiming and held fire from the world view and navigation plot.</summary>
public sealed class WFCockpitFireInput : Control
{
    [Dependency] private IInputManager _input = default!;
    [Dependency] private IClyde _clyde = default!;
    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private IPlayerManager _player = default!;

    private const float UpdateInterval = 0.1f;

    /// <summary>A loose image: packaged clients pack every RSI into one file, so a state's PNG cannot be read by path.</summary>
    public static readonly ResPath ReticlePath = new("/Textures/_WF/Cockpit/gun_sight.png");

    private readonly ScalingViewport _world;
    private readonly ShuttleNavControl _navigation;
    private readonly Func<bool> _enabled;
    private readonly Func<bool> _armed;
    private readonly ICursor? _reticle;
    private readonly Action<EntityCoordinates, bool> _aim;
    private readonly (CursorShape Shape, ICursor? Custom) _worldCursor;
    private readonly (CursorShape Shape, ICursor? Custom) _navigationCursor;
    private Control? _heldSurface;
    private Control? _capturedSurface;
    private float _elapsed;
    private bool _disposed;
    private bool _leftPressEvent;
    private EntityCoordinates? _lastAim;
    private Control? _lastAimSurface;

    /// <summary>Where armed weapons point this frame, or null while the pointer is not aiming.</summary>
    public EntityCoordinates? AimTarget { get; private set; }

    /// <summary>Consumes primary clicks while linked and removes every input hook when its lease ends.</summary>
    public WFCockpitFireInput(MainViewport world, ShuttleNavControl navigation, Func<bool> enabled, Func<bool> armed,
        Action<EntityCoordinates, bool> aim, WFCockpitLease lease)
    {
        IoCManager.InjectDependencies(this);
        MouseFilter = MouseFilterMode.Ignore;
        _world = world.Viewport;
        _navigation = navigation;
        _enabled = enabled;
        _armed = armed;
        _reticle = LoadReticle();
        _aim = aim;
        _worldCursor = (_world.DefaultCursorShape, _world.CustomCursorShape);
        _navigationCursor = (_navigation.DefaultCursorShape, _navigation.CustomCursorShape);
        _world.OnKeyBindDown += WorldDown;
        _world.OnKeyBindUp += WorldUp;
        _world.OnMouseExited += MouseLeft;
        _world.OnVisibilityChanged += SurfaceVisibilityChanged;
        _navigation.OnKeyBindDown += NavigationDown;
        _navigation.OnKeyBindUp += NavigationUp;
        _navigation.OnMouseExited += MouseLeft;
        _navigation.OnVisibilityChanged += SurfaceVisibilityChanged;
        _input.Contexts.ContextChanged += ContextChanged;
        _input.FirstChanceOnKeyEvent += PhysicalKey;
        lease.Remember(Dispose);
    }

    /// <summary>Builds the aiming cursor; a missing image falls back to the system crosshair instead of blocking the cockpit.</summary>
    private ICursor? LoadReticle()
    {
        try
        {
            using var stream = IoCManager.Resolve<IResourceCache>().ContentFileRead(ReticlePath);
            using var image = Image.Load<Rgba32>(stream);
            using var sized = image.Clone(context => context.Resize(48, 48));
            return _clyde.CreateCursor(sized, new Vector2i(24, 24));
        }
        catch (Exception e)
        {
            Logger.GetSawmill("wf.cockpit").Error($"Cockpit reticle failed to load, using the system crosshair: {e}");
            return null;
        }
    }

    /// <summary>Requires a fresh press after the active gun link changes without releasing click capture.</summary>
    public void Cancel()
    {
        _heldSurface = null;
        _lastAim = null;
        _lastAimSurface = null;
    }

    private void PhysicalKey(KeyEventArgs args, KeyEventType type) =>
        _leftPressEvent = args.Key == Keyboard.Key.MouseLeft && type == KeyEventType.Down;

    private void WorldDown(GUIBoundKeyEventArgs args) => Press(_world, args);
    private void WorldUp(GUIBoundKeyEventArgs args) => Release(_world, args);
    private void NavigationDown(GUIBoundKeyEventArgs args) => Press(_navigation, args);
    private void NavigationUp(GUIBoundKeyEventArgs args) => Release(_navigation, args);
    private void ContextChanged(object? sender, ContextChangedEventArgs args) => Cancel();
    private void MouseLeft(GUIMouseHoverEventArgs args) => Cancel();
    private void SurfaceVisibilityChanged(Control control) => Cancel();

    private void Press(Control surface, GUIBoundKeyEventArgs args)
    {
        if (_disposed || args.Handled || !Primary(args.Function) || !_enabled() || !_armed() ||
            !_clyde.IsFocused || Modified() || UserInterfaceManager.KeyboardFocused != null ||
            !_leftPressEvent || !_input.IsKeyDown(Keyboard.Key.MouseLeft) || !IsHovered(surface, args.PointerLocation) ||
            !TryGetTarget(surface, args.PointerLocation, out _))
            return;

        args.Handle();
        if (_capturedSurface != null)
            return;
        _capturedSurface = surface;
        _heldSurface = surface;
        _elapsed = 0;
        SendAim(surface, args.PointerLocation, true);
    }

    private void Release(Control surface, GUIBoundKeyEventArgs args)
    {
        if (surface != _capturedSurface || !Primary(args.Function))
            return;
        args.Handle();
        _capturedSurface = null;
        _heldSurface = null;
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        UpdateInput(args.DeltaSeconds, _input.MouseScreenPosition, _input.IsKeyDown(Keyboard.Key.MouseLeft), _clyde.IsFocused);
    }

    private void UpdateInput(float delta, ScreenCoordinates pointer, bool leftDown, bool focused)
    {
        if (_disposed)
            return;
        var available = _enabled();
        var surface = IsHovered(_world, pointer) ? (Control) _world : IsHovered(_navigation, pointer) ? _navigation : null;
        var canAim = available && _armed() && focused && !Modified() && UserInterfaceManager.KeyboardFocused == null;
        EntityCoordinates target = default;
        var showReticle = canAim && surface != null && TryGetTarget(surface, pointer, out target);
        AimTarget = showReticle ? target : null;
        SetCursor(_world, showReticle && surface == _world, _worldCursor);
        SetCursor(_navigation, showReticle && surface == _navigation, _navigationCursor);
        if (!leftDown)
            _capturedSurface = null;
        if (!canAim)
        {
            Cancel();
            return;
        }

        if (!leftDown || surface != _heldSurface)
            _heldSurface = null;
        if (surface == null)
        {
            Cancel();
            return;
        }

        _elapsed += delta;
        if (_elapsed < UpdateInterval)
            return;
        _elapsed = 0;
        SendAim(surface, pointer, surface == _heldSurface);
    }

    private void SetCursor(Control surface, bool aiming, (CursorShape Shape, ICursor? Custom) original)
    {
        var custom = aiming ? _reticle : original.Custom;
        if (custom != null)
        {
            if (surface.CustomCursorShape != custom)
                surface.CustomCursorShape = custom;
            return;
        }
        var shape = aiming ? CursorShape.Crosshair : original.Shape;
        if (surface.DefaultCursorShape != shape)
            surface.DefaultCursorShape = shape;
    }

    private bool IsHovered(Control surface, ScreenCoordinates pointer) => pointer.IsValid && surface.VisibleInTree &&
        pointer.Window == surface.Window?.Id && UserInterfaceManager.MouseGetControl(pointer) == surface;

    private bool Modified() => _input.IsKeyDown(Keyboard.Key.Shift) || _input.IsKeyDown(Keyboard.Key.Control) ||
        _input.IsKeyDown(Keyboard.Key.Alt);

    private static bool Primary(BoundKeyFunction function) => function == EngineKeyFunctions.UIClick || function == EngineKeyFunctions.Use;

    private void SendAim(Control surface, ScreenCoordinates pointer, bool fire)
    {
        if (!TryGetTarget(surface, pointer, out var target) ||
            !fire && _lastAimSurface == surface && _lastAim == target)
            return;
        _lastAim = target;
        _lastAimSurface = surface;
        _aim(target, fire);
    }

    /// <summary>Expresses a world aim point relative to the pilot's ship, so it stays under the pointer while the ship moves.</summary>
    public static EntityCoordinates ShipRelative(IEntityManager entities, EntityUid? pilot, MapCoordinates point)
    {
        var transforms = entities.System<SharedTransformSystem>();
        if (pilot is { } uid && entities.TryGetComponent(uid, out TransformComponent? xform) && xform.GridUid is { } ship &&
            xform.MapID == point.MapId)
            return transforms.ToCoordinates(ship, point);
        return transforms.ToCoordinates(point);
    }

    private bool TryGetTarget(Control surface, ScreenCoordinates pointer, out EntityCoordinates target)
    {
        target = default;
        if (surface == _navigation)
            target = _navigation.WfCockpitAimCoordinates(pointer.Position / _navigation.UIScale - _navigation.GlobalPosition);
        else
        {
            var map = _world.PixelToMap(pointer.Position);
            if (map.MapId == MapId.Nullspace)
                return false;
            target = ShipRelative(_entities, _player.LocalEntity, map);
        }
        return target.IsValid(_entities) && float.IsFinite(target.X) && float.IsFinite(target.Y);
    }

    protected override void ExitedTree()
    {
        _heldSurface = null;
        base.ExitedTree();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            _heldSurface = _capturedSurface = null;
            AimTarget = null;
            _world.OnKeyBindDown -= WorldDown;
            _world.OnKeyBindUp -= WorldUp;
            _world.OnMouseExited -= MouseLeft;
            _world.OnVisibilityChanged -= SurfaceVisibilityChanged;
            _navigation.OnKeyBindDown -= NavigationDown;
            _navigation.OnKeyBindUp -= NavigationUp;
            _navigation.OnMouseExited -= MouseLeft;
            _navigation.OnVisibilityChanged -= SurfaceVisibilityChanged;
            _input.Contexts.ContextChanged -= ContextChanged;
            _input.FirstChanceOnKeyEvent -= PhysicalKey;
            SetCursor(_world, false, _worldCursor);
            SetCursor(_navigation, false, _navigationCursor);
            _reticle?.Dispose();
        }
        base.Dispose(disposing);
    }
}
