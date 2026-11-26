using System.Numerics;
using Content.Client._WF.Shuttles.UI;
using Content.Client.CombatMode;
using Content.Client.ContextMenu.UI;
using Content.Client.Eye;
using Content.Client.Movement.Systems;
using Content.Client.UserInterface.Controls;
using Content.Client.Verbs;
using Content.Client.Viewport;
using Content.Shared._WF.Shuttles;
using Content.Shared.Camera;
using Content.Shared.Maps;
using Content.Shared.Polymorph.Components;
using Content.Shared.Shuttles.Components;
using Content.Shared.Verbs;
using Robust.Client.ComponentTrees;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.Player;
using Robust.Client.UserInterface;
using Robust.Shared.Graphics;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._WF.Shuttles.Systems;

/// <summary>
/// Runs the local pilot's external view. The eye rides an anchor the server keeps near the look point
/// for PVS, and this offsets it onto the exact point each frame, pans that point with a right-mouse
/// drag, zooms with the wheel and stops the eye drawing FOV. Hulls are roofed over by
/// <see cref="ShuttleHullRoofOverlay"/> instead, and mobs aren't drawn at all.
/// </summary>
public sealed partial class ShuttleExternalCameraSystem : EntitySystem
{
    [Dependency] private IClientNetManager _net = default!;
    [Dependency] private IClyde _clyde = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IInputManager _input = default!;
    [Dependency] private IOverlayManager _overlay = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IUserInterfaceManager _uiManager = default!;
    [Dependency] private CombatModeSystem _combatMode = default!;
    [Dependency] private ContentEyeSystem _contentEye = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedUserInterfaceSystem _ui = default!;
    [Dependency] private VerbSystem _verbs = default!;

    /// <summary>
    /// How far a drag carries the look point before the server is told, in tiles.
    /// </summary>
    private const float PanSendDistance = 1f;

    /// <summary>
    /// How close the anchor is brought to the look point once a drag is over, in tiles.
    /// </summary>
    private const float PanSettleDistance = 0.05f;

    /// <summary>
    /// How far the pointer can stray from a right press, in screen pixels, and still be a click.
    /// </summary>
    private const float PanClickSlop = 4f;

    private static readonly TimeSpan PanInterval = TimeSpan.FromSeconds(0.2);

    /// <summary>
    /// How long a pan gets to show up in the anchor's position before it's sent again.
    /// </summary>
    private static readonly TimeSpan PanSettleTime = TimeSpan.FromSeconds(1);

    private static readonly TimeSpan ZoomInterval = TimeSpan.FromSeconds(0.1);

    /// <summary>
    /// How long a wheel zoom is held onto while the server hasn't answered it.
    /// </summary>
    private static readonly TimeSpan ZoomHold = TimeSpan.FromSeconds(0.5);

    private ShuttleHullRoofOverlay _roof = default!;

    /// <summary>
    /// The entity whose eye this is driving, or null while the view isn't up.
    /// </summary>
    private EntityUid? _pilot;

    private EntityUid _grid;

    /// <summary>
    /// The point being looked at, on the flown grid.
    /// </summary>
    private Vector2 _look;

    /// <summary>
    /// World offset that carries the eye from the anchor to the look point this frame.
    /// </summary>
    private Vector2 _offset;

    private bool _dragging;
    private bool _panButtonDown;
    private ScalingViewport? _dragViewport;
    private Vector2? _lastMouse;

    /// <summary>
    /// Where the right press landed, until the pointer strays far enough for it to be a drag.
    /// </summary>
    private Vector2? _panStart;

    private Vector2 _sentLook;
    private TimeSpan _sentAt;

    /// <summary>
    /// Wheel travel that hasn't added up to a notch yet. A touchpad sends a notch in pieces.
    /// </summary>
    private float _wheel;

    private float? _pendingZoom;
    private float? _sentZoom;
    private TimeSpan _zoomAt;
    private TimeSpan _nextZoom;

    /// <summary>
    /// Whether the local player is looking through the external view.
    /// </summary>
    public bool Active => _pilot != null;

    /// <summary>
    /// Raised when a right click that wasn't a pan asks for the entity menu, before the world is searched.
    /// </summary>
    public event Action<ScreenCoordinates>? WfEntityMenuRequested;

    /// <summary>
    /// The point being looked at, in the flown grid's coordinates.
    /// </summary>
    public Vector2 LookPoint => _look;

    /// <summary>
    /// The zoom the wheel has asked for and the server hasn't answered yet.
    /// </summary>
    public float? PendingZoom => _pendingZoom;

    /// <summary>
    /// How far the pilot's view sits from their eye's own position, for whatever draws by that position.
    /// </summary>
    public Vector2 GetViewOffset(IEye? pass)
    {
        if (_pilot == null || !TryComp<EyeComponent>(_pilot, out var eye))
            return Vector2.Zero;

        return ShuttleHullRoofOverlay.IsPilotPass(pass, eye.Eye, primaryOnly: false) ? _offset : Vector2.Zero;
    }

    public override void Initialize()
    {
        base.Initialize();
        _input.FirstChanceOnKeyEvent += OnPanKey;

        SubscribeLocalEvent<ShuttleCameraComponent, GetEyeOffsetEvent>(OnGetEyeOffset);
        SubscribeLocalEvent<ShuttleCameraComponent, MenuVisibilityEvent>(OnMenuVisibility);
        SubscribeLocalEvent<ChameleonDisguisedComponent, ComponentInit>(OnDisguiseInit);
        SubscribeLocalEvent<GridRemovalEvent>(OnGridRemoval);
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(OnPrototypesReloaded);

        // The offset is measured from where the eye ends up this frame, so everything that moves it goes first.
        UpdatesAfter.Add(typeof(TransformSystem));
        UpdatesAfter.Add(typeof(Robust.Client.Physics.PhysicsSystem));
        UpdatesAfter.Add(typeof(EyeLerpingSystem));
        UpdatesAfter.Add(typeof(EyeSystem));
        UpdatesAfter.Add(typeof(ContentEyeSystem));

        // A sprite hidden once its tree has been brought up to date is drawn for one more frame.
        UpdatesBefore.Add(typeof(SpriteTreeSystem));

        _roof = new ShuttleHullRoofOverlay();
        _overlay.AddOverlay(_roof);
        _overlay.AddOverlay(new ShuttlePoiPointerOverlay());
        _overlay.AddOverlay(new ShuttleFlightHudOverlay());
    }

    public override void Shutdown()
    {
        _input.FirstChanceOnKeyEvent -= OnPanKey;
        base.Shutdown();

        Deactivate();
        _overlay.RemoveOverlay<ShuttleHullRoofOverlay>();
        _overlay.RemoveOverlay<ShuttlePoiPointerOverlay>();
        _overlay.RemoveOverlay<ShuttleFlightHudOverlay>();
    }

    private void OnGetEyeOffset(Entity<ShuttleCameraComponent> ent, ref GetEyeOffsetEvent args)
    {
        if (ent.Owner == _pilot)
            args.Offset += _offset;
    }

    /// <summary>
    /// With FOV off the right-click menu would list everything under the cursor, roofed over or not.
    /// </summary>
    private void OnMenuVisibility(Entity<ShuttleCameraComponent> ent, ref MenuVisibilityEvent args)
    {
        if (ent.Comp.View == ShuttleCameraView.External)
            args.Visibility &= ~MenuVisibility.NoFov;
    }

    private void OnGridRemoval(GridRemovalEvent args)
    {
        _roof.ForgetGrid(args.EntityUid);
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        if (args.WasModified<ContentTileDefinition>())
            _roof.ReloadTiles();
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        if (_player.LocalEntity is not { } player ||
            !TryComp<ShuttleCameraComponent>(player, out var camera) ||
            camera.View != ShuttleCameraView.External ||
            camera.Grid is not { } gridUid ||
            !TryComp<MapGridComponent>(gridUid, out var grid) ||
            !TryComp<EyeComponent>(player, out var eye) ||
            OnPlanetGround(gridUid))
        {
            Deactivate();
            return;
        }

        if (_pilot != player)
            Deactivate();

        if (_pilot == null || _grid != gridUid)
        {
            _pilot = player;
            _grid = gridUid;
            _look = grid.LocalAABB.Center;
            _sentLook = _look;
            _sentAt = _timing.CurTime;
            _pendingZoom = null;
            _sentZoom = null;
            _wheel = 0f;
            EndDrag();

        }

        // Traversal isn't networked. Left on, the anchor's own lerp hands it to the map over open space.
        if (TryComp(camera.Camera, out TransformComponent? anchor) && anchor.GridTraversal)
            anchor.GridTraversal = false;

        // A replay has a pilot and a console but no server to pan for. Where its anchor went is the look point.
        var live = _net.IsConnected;

        if (live)
            UpdateDrag(gridUid);
        else if (anchor != null && anchor.ParentUid == gridUid)
            _look = anchor.LocalPosition;

        _look = Clamp(grid, _look);

        var gridXform = Transform(gridUid);
        var view = eye.Eye;
        var onMap = view.Position.MapId == gridXform.MapID;

        // Taken from where the eye really is, so the view lands on the look point however far the
        // anchor trails behind it.
        _offset = onMap
            ? Vector2.Transform(_look, _transform.GetWorldMatrix(gridXform)) - view.Position.Position
            : Vector2.Zero;

        // Only the drawn eye loses FOV. The component keeps it, so the server, examine and speech bubbles
        // carry on as before. A server state puts it back and this takes it off again before the frame.
        // An eye that isn't over the flown grid's map keeps it, whatever map that is.
        view.DrawFov = !onMap && eye.DrawFov;
        _contentEye.UpdateEyeOffset((player, eye));

        if (onMap)
            HideMobs(player);
        else
            ShowHidden();

        if (!live || !TryComp<PilotComponent>(player, out var pilot) || pilot.Console is not { } console)
            return;

        SendPan(console, gridUid, anchor);
        SendZoom(console, camera);
    }

    /// <summary>
    /// The server's rule against the view on a planet's ground, where buildings stand on terrain that
    /// nothing roofs. It only takes the view away on its next look at it, so the client keeps to it too.
    /// </summary>
    private bool OnPlanetGround(EntityUid gridUid)
    {
        return HasComp<MapComponent>(gridUid) || HasComp<MapGridComponent>(Transform(gridUid).MapUid);
    }

    private void OnPanKey(KeyEventArgs args, KeyEventType type) =>
        OnPanKey(args, type, _input.MouseScreenPosition);

    /// <summary>
    /// Owns unmodified right-button presses only over the active external world view. A press that
    /// is released without being dragged opens the entity menu there instead.
    /// </summary>
    private void OnPanKey(KeyEventArgs args, KeyEventType type, ScreenCoordinates mouse)
    {
        if (args.Key != Keyboard.Key.MouseRight)
            return;
        if (_panButtonDown)
        {
            args.Handle();
            if (type == KeyEventType.Up)
            {
                var held = _dragViewport;
                var click = IsPanClick(mouse);
                EndDrag();

                if (click && held != null)
                    OpenEntityMenu(held, mouse);
            }
            return;
        }
        if (args.Handled || type != KeyEventType.Down || args.IsRepeat || !Active || !_net.IsConnected ||
            !_clyde.IsFocused || !mouse.IsValid || args.Shift || args.Control || args.Alt ||
            _uiManager.MouseGetControl(mouse) is not ScalingViewport { Parent: MainViewport } viewport)
            return;
        args.Handle();
        _panButtonDown = _dragging = true;
        _dragViewport = viewport;
        _lastMouse = mouse.Position;
        _panStart = mouse.Position;
    }

    /// <summary>
    /// Whether the pointer is still where the right press landed, on the viewport it landed on.
    /// </summary>
    private bool IsPanClick(ScreenCoordinates mouse)
    {
        return _panStart is { } start &&
               _dragViewport is { } viewport &&
               mouse.IsValid &&
               mouse.Window == viewport.Window?.Id &&
               (mouse.Position - start).LengthSquared() <= PanClickSlop * PanClickSlop;
    }

    /// <summary>
    /// Opens the entity menu at the pointer, as the right press would have if it hadn't been taken for a pan.
    /// </summary>
    private void OpenEntityMenu(ScalingViewport viewport, ScreenCoordinates mouse)
    {
        if (_combatMode.IsInCombatMode())
            return;

        WfEntityMenuRequested?.Invoke(mouse);

        // The point a plain right-click would pick, so a lens such as a singularity's bends it the same way.
        var coordinates = viewport.PixelToMap(mouse.Position);

        if (coordinates.MapId != MapId.Nullspace && _verbs.TryGetEntityMenuEntities(coordinates, out var entities))
            _uiManager.GetUIController<EntityMenuUIController>().OpenRootMenu(entities);
    }

    /// <summary>Keeps hold of the grabbed world point through viewport rotation, zoom and pointer movement.</summary>
    private void UpdateDrag(EntityUid gridUid) => UpdateDrag(gridUid, _input.MouseScreenPosition, _clyde.IsFocused);

    private void UpdateDrag(EntityUid gridUid, ScreenCoordinates mouse, bool focused)
    {
        if (!_dragging)
            return;

        if (!_panButtonDown || !focused || _dragViewport is not { IsInsideTree: true, VisibleInTree: true } dragged)
        {
            EndDrag();
            return;
        }

        // Off the window there's no position to measure from, so pick it up afresh once it's back.
        if (!mouse.IsValid || mouse.Window != dragged.Window?.Id)
        {
            _lastMouse = null;
            return;
        }

        // A press that hasn't strayed yet may still be a click, so the look point stays put.
        if (_panStart is { } start)
        {
            if ((mouse.Position - start).LengthSquared() <= PanClickSlop * PanClickSlop)
                return;

            _panStart = null;
        }

        if (_lastMouse is { } last)
        {
            // Both through the one viewport in the one frame, so only its zoom and turn are in the difference.
            var from = dragged.ScreenToMap(last);
            var to = dragged.ScreenToMap(mouse.Position);

            if (from.MapId != MapId.Nullspace && from.MapId == to.MapId)
                _look += (-_transform.GetWorldRotation(gridUid)).RotateVec(from.Position - to.Position);
        }

        _lastMouse = mouse.Position;
    }

    private void EndDrag()
    {
        _panButtonDown = false;
        _dragging = false;
        _dragViewport = null;
        _lastMouse = null;
        _panStart = null;
    }

    /// <summary>
    /// Tells the server where the pilot is looking, so its anchor, and PVS with it, keeps up.
    /// </summary>
    private void SendPan(EntityUid console, EntityUid gridUid, TransformComponent? anchor)
    {
        var now = _timing.CurTime;

        if (now < _sentAt + PanInterval)
            return;

        var reach = _dragging ? PanSendDistance : PanSettleDistance;
        var behind = (_look - _sentLook).LengthSquared() > reach * reach;

        // The server drops a pan that lands too soon after the last, and re-centres the anchor by itself
        // when a hull splits. Once a pan has had time to arrive, where the anchor sits has the last word.
        if (!behind && !_dragging && now >= _sentAt + PanSettleTime && anchor != null && anchor.ParentUid == gridUid)
            behind =(_look - anchor.LocalPosition).LengthSquared() > PanSettleDistance * PanSettleDistance;

        if (!behind)
            return;

        WfSendOrderedCameraInput(console, new ShuttleCameraPanMessage(_look)); // WOLFGATE(Cockpit): order pans with camera session changes.
        _sentLook = _look;
        _sentAt = now;
    }

    /// <summary>
    /// Passes wheel zoom on a few times a second rather than once a notch.
    /// </summary>
    private void SendZoom(EntityUid console, ShuttleCameraComponent camera)
    {
        if (_pendingZoom is not { } pending)
            return;

        var now = _timing.CurTime;

        // A zoom that matches doesn't settle it while a different one is still on its way to the server.
        var settled = MathHelper.CloseTo(camera.Zoom, pending) && (_sentZoom is null || _sentZoom == pending);

        if (settled || now >= _zoomAt + ZoomHold)
        {
            _pendingZoom = null;
            _sentZoom = null;
            return;
        }

        if (now < _nextZoom || _sentZoom == pending)
            return;

        WfSendOrderedCameraInput(console, new ShuttleCameraZoomMessage(pending)); // WOLFGATE(Cockpit): order wheel zoom with camera session changes.
        _sentZoom = pending;
        _nextZoom = now + ZoomInterval;
    }

    /// <summary>
    /// Hands the eye back as the view goes: FOV as its component has it, and no offset from here.
    /// </summary>
    private void Deactivate()
    {
        if (_pilot is not { } pilot)
            return;

        _pilot = null;
        _grid = default;
        _offset = Vector2.Zero;
        _pendingZoom = null;
        _sentZoom = null;
        _wheel = 0f;
        EndDrag();
        ShowHidden();

        if (TerminatingOrDeleted(pilot) || !TryComp<EyeComponent>(pilot, out var eye))
            return;

        var view = eye.Eye;
        view.DrawFov = eye.DrawFov;
        _contentEye.UpdateEyeOffset((pilot, eye));
    }

    /// <summary>
    /// Moves the look point, kept within reach of the hull. Does nothing while the view isn't up.
    /// </summary>
    public void SetLookPoint(Vector2 gridLocal)
    {
        if (_pilot == null || !TryComp<MapGridComponent>(_grid, out var grid))
            return;

        _look = Clamp(grid, gridLocal);
    }

    /// <summary>
    /// Takes mouse wheel travel as zoom, a step for each whole notch. False if the view isn't up to take it.
    /// </summary>
    public bool TryWheelZoom(float deltaY)
    {
        // A replay has nobody to send a zoom to.
        if (deltaY == 0f ||
            !float.IsFinite(deltaY) ||
            !_net.IsConnected ||
            !TryComp<ShuttleCameraComponent>(_pilot, out var camera))
        {
            return false;
        }

        // What's left of a scroll one way doesn't count against a scroll back.
        if (MathF.Sign(deltaY) != MathF.Sign(_wheel))
            _wheel = 0f;

        _wheel += deltaY;
        var notches = MathF.Truncate(_wheel);

        // Taken all the same, it just hasn't added up to anything yet.
        if (notches == 0f)
            return true;

        _wheel -= notches;

        // Notches stack on the last one asked for, since the server's answer to it is still on its way.
        var zoom = (_pendingZoom ?? camera.Zoom) - notches * ShuttleCameraComponent.ZoomStep;
        zoom = MathF.Round(zoom / ShuttleCameraComponent.ZoomStep) * ShuttleCameraComponent.ZoomStep;

        _pendingZoom = Math.Clamp(zoom, ShuttleCameraComponent.MinZoom, ShuttleCameraComponent.MaxZoom);
        _zoomAt = _timing.CurTime;
        return true;
    }

    private static Vector2 Clamp(MapGridComponent grid, Vector2 point)
    {
        return grid.LocalAABB.Enlarged(ShuttleCameraComponent.PanMargin).ClosestPoint(point);
    }
}
