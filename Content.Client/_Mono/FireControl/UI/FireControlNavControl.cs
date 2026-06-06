using System.Linq;
using System.Numerics;
using Content.Client.Shuttles.UI;
using Content.Shared._Mono.FireControl;
using Content.Shared.Physics;
using Content.Shared.Shuttles.BUIStates;
using Content.Shared.Shuttles.Components;
using Content.Shared.Shuttles.Systems;
using Content.Client._Mono.Radar;
using Content.Shared._Mono.Radar;
using Content.Shared._Crescent.ShipShields;
using Robust.Shared.Physics.Collision.Shapes;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared.Input;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;

namespace Content.Client._Mono.FireControl.UI;

public sealed class FireControlNavControl : ShuttleNavControl
{
    private readonly SharedTransformSystem _transform;
    private readonly SharedPhysicsSystem _physics;
    private readonly RadarBlipsSystem _blips;

    private EntityUid? _activeConsole;
    private FireControllableEntry[]? _controllables;
    private HashSet<NetEntity> _selectedWeapons = new();

    private readonly Dictionary<NetEntity, Color> _blipColors = new();

    // Add a limit to how often we update the cursor position to prevent network spam
    private float _lastCursorUpdateTime = 0f;
    private const float CursorUpdateInterval = 0.1f; // 10 updates per second

    private bool _clickedOnWeapon;

    private const float WeaponClickRadiusView = 14f;

    /// <summary>
    /// Raised when the user clicks a controllable weapon on the own-ship grid.
    /// </summary>
    public Action<NetEntity>? OnWeaponGridClick;

    public FireControlNavControl() : base(64f, 512f, 512f)
    {
        IoCManager.InjectDependencies(this);
        _blips = EntManager.System<RadarBlipsSystem>();
        _physics = EntManager.System<SharedPhysicsSystem>();
        _transform = EntManager.System<SharedTransformSystem>();
    }

    protected override void MouseMove(GUIMouseMoveEventArgs args)
    {
        base.MouseMove(args);
        if (_isMouseInside && !_clickedOnWeapon)
            TryUpdateCursorPosition(_lastMousePos);
    }

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        if (args.Function != EngineKeyFunctions.UIClick)
        {
            base.KeyBindDown(args);
            return;
        }

        if (TryGetWeaponAtPosition(args.RelativePosition) != null)
        {
            _isMouseDown = true;
            _lastMousePos = args.RelativePosition;
            _clickedOnWeapon = true;
            return;
        }

        _clickedOnWeapon = false;
        base.KeyBindDown(args);
    }

    protected override void KeyBindUp(GUIBoundKeyEventArgs args)
    {
        if (args.Function != EngineKeyFunctions.UIClick)
        {
            base.KeyBindUp(args);
            return;
        }

        if (_clickedOnWeapon && TryGetWeaponAtPosition(args.RelativePosition) is { } weapon)
        {
            _isMouseDown = false;
            _clickedOnWeapon = false;
            OnWeaponGridClick?.Invoke(weapon);
            return;
        }

        _clickedOnWeapon = false;
        base.KeyBindUp(args);
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        if (!_clickedOnWeapon)
            base.FrameUpdate(args);
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        if (_coordinates == null || _rotation == null)
            return;

        var xformQuery = EntManager.GetEntityQuery<TransformComponent>();
        if (!xformQuery.TryGetComponent(_coordinates.Value.EntityId, out var xform)
            || xform.MapID == MapId.Nullspace)
        {
            return;
        }

        base.Draw(handle);

        var coordEntRot = _transform.GetWorldRotation(_coordinates.Value.EntityId);

        var worldRot = _rotation.Value;

        var mapPos = _transform.ToMapCoordinates(_coordinates.Value).Offset(_rotation.Value.RotateVec(Offset));
        var mapCoord = _transform.ToCoordinates(mapPos);
        var worldToShuttle = Matrix3Helpers.CreateTranslation(-mapCoord.Position) * Matrix3Helpers.CreateRotation(-worldRot);
        Matrix3x2.Invert(worldToShuttle, out var shuttleToWorld);
        var shuttleToView = Matrix3x2.CreateScale(new Vector2(MinimapScale, -MinimapScale)) * Matrix3x2.CreateTranslation(MidPointVector);
        var worldToView = worldToShuttle * shuttleToView;
        Matrix3x2.Invert(worldToView, out var viewToWorld);

        var blips = _blips.GetCurrentBlips();
        _blipColors.Clear();
        foreach (var blip in blips)
            _blipColors[blip.NetUid] = blip.Config.Color;

        if (_controllables != null)
        {
            foreach (var controllable in _controllables)
            {
                var coords = EntManager.GetCoordinates(controllable.Coordinates);
                var worldPos = _transform.ToMapCoordinates(coords).Position;
                var viewPos = Vector2.Transform(worldPos, worldToView);
                var isSelected = _selectedWeapons.Contains(controllable.NetEntity);

                var color = _blipColors.TryGetValue(controllable.NetEntity, out var blipColor)
                    ? blipColor
                    : Color.Orange;

                if (isSelected)
                {
                    handle.DrawCircle(viewPos, 10f, Color.LimeGreen.WithAlpha(0.35f), filled: true);
                    handle.DrawCircle(viewPos, 10f, Color.LimeGreen, filled: false);
                    handle.DrawCircle(viewPos, 5f, color.WithAlpha(0.95f), filled: true);
                }
                else
                {
                    handle.DrawCircle(viewPos, 5f, color.WithAlpha(0.45f), filled: true);
                    handle.DrawCircle(viewPos, 5f, color.WithAlpha(0.85f), filled: false);
                }

                if (!isSelected)
                    continue;

                var cursorViewPos = InverseScalePosition(_lastMousePos);
                cursorViewPos = ScalePosition(cursorViewPos);

                var cursorWorldPos = Vector2.Transform(cursorViewPos, viewToWorld);

                var direction = cursorWorldPos - worldPos;
                var ray = new CollisionRay(worldPos, direction.Normalized(), (int)CollisionGroup.Impassable);

                var results = _physics.IntersectRay(xform.MapID, ray, direction.Length(), ignoredEnt: _coordinates?.EntityId);

                if (!results.Any())
                    handle.DrawLine(viewPos, cursorViewPos, color.WithAlpha(0.3f));
            }
        }
    }

    public void UpdateControllables(EntityUid console, FireControllableEntry[] controllables)
    {
        _activeConsole = console;
        _controllables = controllables;
    }

    public void UpdateSelectedWeapons(HashSet<NetEntity> selectedWeapons)
    {
        _selectedWeapons = selectedWeapons;
    }

    private NetEntity? TryGetWeaponAtPosition(Vector2 relativePosition)
    {
        if (_controllables == null || _coordinates == null || _rotation == null)
            return null;

        var xformQuery = EntManager.GetEntityQuery<TransformComponent>();
        if (!xformQuery.TryGetComponent(_coordinates.Value.EntityId, out var xform)
            || xform.MapID == MapId.Nullspace)
        {
            return null;
        }

        var worldRot = _rotation.Value;
        var mapPos = _transform.ToMapCoordinates(_coordinates.Value).Offset(_rotation.Value.RotateVec(Offset));
        var mapCoord = _transform.ToCoordinates(mapPos);
        var worldToShuttle = Matrix3Helpers.CreateTranslation(-mapCoord.Position) * Matrix3Helpers.CreateRotation(-worldRot);
        var shuttleToView = Matrix3x2.CreateScale(new Vector2(MinimapScale, -MinimapScale)) * Matrix3x2.CreateTranslation(MidPointVector);
        var worldToView = worldToShuttle * shuttleToView;

        var clickViewPos = relativePosition * UIScale;
        NetEntity? closest = null;
        var closestDist = float.MaxValue;
        var clickRadius = WeaponClickRadiusView * UIScale;

        foreach (var controllable in _controllables)
        {
            var coords = EntManager.GetCoordinates(controllable.Coordinates);
            var worldPos = _transform.ToMapCoordinates(coords).Position;
            var viewPos = Vector2.Transform(worldPos, worldToView);

            var dist = Vector2.Distance(clickViewPos, viewPos);
            if (dist >= clickRadius || dist >= closestDist)
                continue;

            closestDist = dist;
            closest = controllable.NetEntity;
        }

        return closest;
    }

    private void TryUpdateCursorPosition(Vector2 relativePosition)
    {
        var currentTime = IoCManager.Resolve<IGameTiming>().CurTime.TotalSeconds;
        if (currentTime - _lastCursorUpdateTime < CursorUpdateInterval)
            return;

        _lastCursorUpdateTime = (float)currentTime;

        var coords = GetMouseEntityCoordinates(relativePosition);
        OnRadarClick?.Invoke(coords);
    }

    /// <summary>
    /// Returns true if the mouse button is currently pressed down
    /// </summary>
    public bool IsMouseDown() => _isMouseDown;
}
