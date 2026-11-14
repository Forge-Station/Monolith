using System.Numerics;
using Content.Shared._Mono.Detection;
using Content.Shared._WF.Shuttles;
using Content.Shared.Shuttles.Components;
using Content.Shared.Shuttles.Systems;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;

namespace Content.Client._WF.Shuttles.UI;

/// <summary>
/// Arrows around the edge of the pilot's external view, pointing at whatever the radar would label
/// that is off the screen. It keeps to the radar's rules on what is hidden and what is only a signature.
/// </summary>
public sealed partial class ShuttlePoiPointerOverlay : Overlay
{
    [Dependency] private IEntityManager _entManager = default!;
    [Dependency] private IEyeManager _eyeManager = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IResourceCache _resCache = default!;
    [Dependency] private IUserInterfaceManager _uiManager = default!;

    private readonly DetectionSystem _detection;
    private readonly SharedMapSystem _map;
    private readonly SharedShuttleSystem _shuttles;
    private readonly SharedTransformSystem _transform;

    public override OverlaySpace Space => OverlaySpace.ScreenSpace;

    /// <summary>
    /// How far out something is still pointed at, the radar's own default for labels.
    /// </summary>
    private const float MaxDistance = 3000f;

    /// <summary>
    /// How far in from the view's edge the arrows sit, in UI pixels.
    /// </summary>
    private const float EdgeInset = 40f;

    private const float ArrowLength = 12f;
    private const float ArrowHalfWidth = 6f;
    private const float LabelGap = 6f;
    private const int FontSize = 10;

    private readonly Font _font;
    private readonly Vector2[] _arrow = new Vector2[3];
    private List<Entity<MapGridComponent>> _grids = new();

    public ShuttlePoiPointerOverlay()
    {
        IoCManager.InjectDependencies(this);

        _detection = _entManager.System<DetectionSystem>();
        _map = _entManager.System<SharedMapSystem>();
        _shuttles = _entManager.System<SharedShuttleSystem>();
        _transform = _entManager.System<SharedTransformSystem>();
        _font = new VectorFont(_resCache.GetResource<FontResource>("/Fonts/NotoSans/NotoSans-Regular.ttf"), FontSize);
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        // Once, on the pilot's own picture, not on every level drawn under it or on other viewports.
        return _entManager.TryGetComponent<ShuttleCameraComponent>(_player.LocalEntity, out var camera) &&
               camera.View == ShuttleCameraView.External &&
               camera.Grid != null &&
               _entManager.TryGetComponent<EyeComponent>(_player.LocalEntity, out var eye) &&
               ShuttleHullRoofOverlay.IsPilotPass(args.Viewport.Eye, eye.Eye, primaryOnly: true);
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (_player.LocalEntity is not { } player ||
            !_entManager.TryGetComponent<ShuttleCameraComponent>(player, out var camera) ||
            camera.Grid is not { } ownUid ||
            !_entManager.TryGetComponent<MapGridComponent>(ownUid, out var ownGrid))
        {
            return;
        }

        var handle = args.ScreenHandle;
        var scale = _uiManager.RootControl.UIScale;
        var own = Vector2.Transform(ownGrid.LocalAABB.Center, _transform.GetWorldMatrix(ownUid));

        // What a console can make out is asked of the console, as the radar does.
        EntityUid? console = _entManager.TryGetComponent<PilotComponent>(player, out var pilot) ? pilot.Console : null;

        var view = ShuttleFlightHudOverlay.VisibleBounds(args);
        var inset = EdgeInset * scale;
        var inner = new Box2(view.Left + inset, view.Top + inset, view.Right - inset, view.Bottom - inset);

        if (inner.Width <= 0f || inner.Height <= 0f)
            return;

        var centre = inner.Center;
        var half = inner.Size / 2f;

        _grids.Clear();
        _map.FindGridsIntersecting(args.MapId,
            Box2.CenteredAround(own, new Vector2(MaxDistance * 2f)),
            ref _grids,
            approx: true,
            includeMap: false);

        foreach (var grid in _grids)
        {
            if (grid.Owner == ownUid ||
                !_entManager.HasComponent<FixturesComponent>(grid.Owner) ||
                !_entManager.TryGetComponent<PhysicsComponent>(grid.Owner, out var body))
            {
                continue;
            }

            _entManager.TryGetComponent<IFFComponent>(grid.Owner, out var iff);

            if (!_shuttles.CanDraw(grid.Owner, body, iff) || GetLabel(grid, iff, console) is not { } name)
                continue;

            var world = Vector2.Transform(body.LocalCenter, _transform.GetWorldMatrix(grid.Owner));
            var distance = (world - own).Length();

            if (distance > MaxDistance)
                continue;

            var screen = _eyeManager.WorldToScreen(world);

            if (inner.Contains(screen))
                continue;

            var direction = screen - centre;

            if (direction.LengthSquared() < 0.001f)
                continue;

            direction = Vector2.Normalize(direction);

            // Out along the bearing as far as the nearer pair of edges allows.
            var reach = Math.Min(
                direction.X == 0f ? float.MaxValue : half.X / Math.Abs(direction.X),
                direction.Y == 0f ? float.MaxValue : half.Y / Math.Abs(direction.Y));

            var tip = centre + direction * reach;
            var hideLabel = iff != null && (iff.Flags & IFFFlags.HideLabel) != 0x0;
            var hideColor = hideLabel && (iff!.Flags & IFFFlags.AlwaysShowColor) == 0x0;
            var color = hideColor ? Color.Orange : _shuttles.GetIFFColor(grid.Owner, self: false, iff);

            DrawPointer(handle, tip, direction, scale, color, Loc.GetString("shuttle-console-iff-label",
                ("name", name),
                ("distance", distance < 1000f ? $"{distance:0}" : $"{distance / 1000f:0.0}k")));
        }
    }

    /// <summary>
    /// What the radar would call a grid, or null if it wouldn't label it at all.
    /// </summary>
    private string? GetLabel(Entity<MapGridComponent> grid, IFFComponent? iff, EntityUid? console)
    {
        if (iff != null && (iff.Flags & IFFFlags.HideLabelAlways) != 0x0)
            return null;

        if (iff == null || (iff.Flags & IFFFlags.HideLabel) == 0x0)
            return _shuttles.GetIFFLabel(grid.Owner, self: false, component: iff);

        var level = console is { } uid && _entManager.EntityExists(uid)
            ? _detection.IsGridDetected((grid.Owner, grid.Comp), uid)
            : DetectionLevel.Undetected;

        return level switch
        {
            DetectionLevel.Undetected => null,
            DetectionLevel.PartialDetected => Loc.GetString("shuttle-console-signature-infrared"),
            _ => _detection.HandleUnknownMassLabel((grid.Owner, grid.Comp)),
        };
    }

    private void DrawPointer(
        DrawingHandleScreen handle,
        Vector2 tip,
        Vector2 direction,
        float scale,
        Color color,
        string label)
    {
        var back = tip - direction * ArrowLength * scale;
        var side = new Vector2(-direction.Y, direction.X) * ArrowHalfWidth * scale;

        _arrow[0] = tip;
        _arrow[1] = back + side;
        _arrow[2] = back - side;
        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, _arrow, color);

        // The label sits behind the arrow, pushed in by half its own size along the bearing.
        var size = handle.GetDimensions(_font, label, scale);
        var middle = back - direction * LabelGap * scale - new Vector2(direction.X * size.X, direction.Y * size.Y) / 2f;
        handle.DrawString(_font, (middle - size / 2f).Rounded(), label, scale, color);
    }
}
