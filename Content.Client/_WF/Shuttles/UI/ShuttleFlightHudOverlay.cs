using System.Numerics;
using Content.Client._WF.Cockpit; // WOLFGATE(Cockpit)
using Content.Shared._WF.Shuttles;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Shared.Enums;
using Robust.Shared.Physics.Components;

namespace Content.Client._WF.Shuttles.UI;

/// <summary>
/// Flight readouts in a corner of the pilot's external view: a dial with the ship's velocity as the
/// screen shows it, and its speed, heading, drift and turn rate.
/// </summary>
public sealed partial class ShuttleFlightHudOverlay : Overlay
{
    [Dependency] private IEntityManager _entManager = default!;
    [Dependency] private IEyeManager _eyeManager = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IResourceCache _resCache = default!;
    [Dependency] private IUserInterfaceManager _uiManager = default!;

    private readonly SharedTransformSystem _transform;

    public override OverlaySpace Space => OverlaySpace.ScreenSpace;

    /// <summary>
    /// Clear space left above the dial, which sits in the middle of the view's top edge. In UI pixels,
    /// about an inch, so the bars along the top stay uncovered.
    /// </summary>
    private const float TopPadding = 96f;

    private const float DialRadius = 48f;
    private const float TickLength = 6f;
    private const float ArrowHead = 7f;
    private const float ReadoutGap = 14f;
    private const int FontSize = 10;

    /// <summary>
    /// Speed at which the dial's needle reaches half way out. It never quite reaches the rim.
    /// </summary>
    private const float HalfScaleSpeed = 25f;

    /// <summary>
    /// Below this the ship counts as stopped, and has no direction to speak of.
    /// </summary>
    private const float MinSpeed = 0.1f;

    private static readonly Color DialColor = Color.FromHex("#00ff2a").WithAlpha(0.55f);
    private static readonly Color BackColor = Color.Black.WithAlpha(0.35f);
    private static readonly Color NeedleColor = Color.FromHex("#ffd24a");
    private static readonly Color TextColor = Color.FromHex("#00ff2a");

    private readonly Font _font;
    private readonly Vector2[] _head = new Vector2[3];

    public ShuttleFlightHudOverlay()
    {
        IoCManager.InjectDependencies(this);

        _transform = _entManager.System<SharedTransformSystem>();
        _font = new VectorFont(_resCache.GetResource<FontResource>("/Fonts/NotoSans/NotoSans-Regular.ttf"), FontSize);
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        // WOLFGATE(Cockpit): the cockpit already supplies permanent flight instruments.
        return !_uiManager.GetUIController<WFCockpitUIController>().Active &&
               _entManager.TryGetComponent<ShuttleCameraComponent>(_player.LocalEntity, out var camera) &&
               camera.View == ShuttleCameraView.External &&
               camera.Grid != null &&
               _entManager.TryGetComponent<EyeComponent>(_player.LocalEntity, out var eye) &&
               ShuttleHullRoofOverlay.IsPilotPass(args.Viewport.Eye, eye.Eye, primaryOnly: true);
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (!_entManager.TryGetComponent<ShuttleCameraComponent>(_player.LocalEntity, out var camera) ||
            camera.Grid is not { } gridUid ||
            !_entManager.TryGetComponent<PhysicsComponent>(gridUid, out var body))
        {
            return;
        }

        var handle = args.ScreenHandle;
        var scale = _uiManager.RootControl.UIScale;
        var view = VisibleBounds(args);
        var centre = new Vector2((view.Left + view.Right) / 2f, view.Top + (TopPadding + DialRadius) * scale);
        var radius = DialRadius * scale;

        var velocity = body.LinearVelocity;
        var speed = velocity.Length();
        var (position, rotation) = _transform.GetWorldPositionRotation(gridUid);

        handle.DrawCircle(centre, radius, BackColor);
        handle.DrawCircle(centre, radius, DialColor, false);

        for (var i = 0; i < 8; i++)
        {
            var spoke = new Angle(i * Math.PI / 4).ToVec();
            var length = (i % 2 == 0 ? TickLength : TickLength / 2f) * scale;
            handle.DrawLine(centre + spoke * (radius - length), centre + spoke * radius, DialColor);
        }

        if (speed > MinSpeed)
        {
            // Through the eye, so the needle points the way the ship is seen to slide across the screen.
            var along = _eyeManager.WorldToScreen(position + velocity / speed) - _eyeManager.WorldToScreen(position);

            if (along.LengthSquared() > 0.0001f)
            {
                along = Vector2.Normalize(along);
                var tip = centre + along * radius * (speed / (speed + HalfScaleSpeed));
                var side = new Vector2(-along.Y, along.X) * ArrowHead * scale / 2f;

                handle.DrawLine(centre, tip, NeedleColor);
                _head[0] = tip + along * ArrowHead * scale;
                _head[1] = tip + side;
                _head[2] = tip - side;
                handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, _head, NeedleColor);
            }
        }

        // Bow is the grid's north. Compass bearings run clockwise, angles the other way.
        var heading = Wrap(-rotation.Degrees);
        var local = (-rotation).RotateVec(velocity);
        var drift = speed > MinSpeed ? MathHelper.RadiansToDegrees(MathF.Atan2(local.X, local.Y)) : 0f;
        var turn = -MathHelper.RadiansToDegrees(body.AngularVelocity);

        // In a row with the dial: two readouts run out to its left and two to its right.
        var gap = ReadoutGap * scale;
        var left = centre.X - radius - gap;
        left = DrawReadout(handle, Loc.GetString("shuttle-camera-hud-heading", ("heading", $"{heading:000}")), left, centre.Y, scale, true) - gap;
        DrawReadout(handle, Loc.GetString("shuttle-camera-hud-speed", ("speed", $"{speed:0.0}")), left, centre.Y, scale, true);

        var right = centre.X + radius + gap;
        right = DrawReadout(handle, Loc.GetString("shuttle-camera-hud-drift", ("angle", $"{drift:+0;-0;0}")), right, centre.Y, scale, false) + gap;
        DrawReadout(handle, Loc.GetString("shuttle-camera-hud-turn", ("rate", $"{turn:+0.0;-0.0;0.0}")), right, centre.Y, scale, false);
    }

    /// <summary>
    /// Writes a readout level with the dial's centre, ending or starting at an edge. Returns its far edge.
    /// </summary>
    private float DrawReadout(DrawingHandleScreen handle, string text, float edge, float middle, float scale, bool endsAtEdge)
    {
        var size = handle.GetDimensions(_font, text, scale);
        var x = endsAtEdge ? edge - size.X : edge;

        handle.DrawString(_font, new Vector2(x, middle - size.Y / 2f).Rounded(), text, scale, TextColor);
        return endsAtEdge ? x : x + size.X;
    }

    /// <summary>
    /// The part of the viewport's picture that is on the screen. The picture itself can run past the
    /// edges of the control it's shown in.
    /// </summary>
    public static UIBox2 VisibleBounds(in OverlayDrawArgs args)
    {
        UIBox2 bounds = args.ViewportBounds;

        if (args.ViewportControl is not Control control)
            return bounds;

        UIBox2 shown = control.GlobalPixelRect;

        return new UIBox2(
            Math.Max(bounds.Left, shown.Left),
            Math.Max(bounds.Top, shown.Top),
            Math.Min(bounds.Right, shown.Right),
            Math.Min(bounds.Bottom, shown.Bottom));
    }

    private static double Wrap(double degrees)
    {
        degrees %= 360d;
        return degrees < 0d ? degrees + 360d : degrees;
    }
}
