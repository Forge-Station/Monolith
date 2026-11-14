#nullable enable annotations

using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using Content.Client._WF.Shuttles.Systems;
using Content.Client._WF.Shuttles.UI;
using Content.Client.ContextMenu.UI;
using Content.Client.Popups;
using Content.Client.UserInterface.Controls;
using Content.Client.Viewport;
using Content.IntegrationTests.Tests.Interaction;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared._WF.Shuttles;
using Content.Shared.Movement.Components;
using Content.Shared.Polymorph.Components;
using Content.Shared.Shuttles.BUIStates;
using Content.Shared.Shuttles.Components;
using Content.Shared.Verbs;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.Shuttles;

/// <summary>
/// A pilot's external view: where its anchor sits, how panning and zooming move it, that it keeps its
/// place as the hull changes, when the view is refused, what the pilot's client makes of it, and that
/// it draws nobody.
/// </summary>
public sealed class ShuttleExternalCameraTest : InteractionTest
{
    // The stock test mob has no zoom of its own to set, and nothing for the view to stop drawing.
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: InteractionTestMob
  id: ShuttleExternalCameraTestMob
  components:
  - type: ContentEye
  - type: MobState
  - type: Sprite
";

    protected override string PlayerPrototype => "ShuttleExternalCameraTestMob";

    private const float Margin = ShuttleCameraComponent.PanMargin;

    /// <summary>
    /// Middle of the test hull, whose bounds run from (0, 0) to (5, 5).
    /// </summary>
    private static readonly Vector2 HullCentre = new(2.5f, 2.5f);

    private static readonly Vector2 FrontCamera = new(2.5f, 5.5f);

    private ShuttleConsoleSystem _consoleSystem = default!;
    private Entity<MapGridComponent> _grid;
    private EntityUid _console;

    [Test]
    public async Task AnchorSitsOverTheHull()
    {
        await TakeHelm();
        await SetCamera(ShuttleCameraView.External, 3f);
        await RunTicks(10);

        var anchor = await GetAnchor();

        await Server.WaitAssertion(() =>
        {
            var camera = SEntMan.GetComponent<ShuttleCameraComponent>(SPlayer);
            Assert.That(camera.View, Is.EqualTo(ShuttleCameraView.External));
            Assert.That(camera.Grid, Is.EqualTo(_grid.Owner), "The client pans on the grid it's told is flown.");
            AssertAnchorAt(HullCentre, "The view should start over the middle of the hull.");

            var eye = SEntMan.GetComponent<EyeComponent>(SPlayer);
            Assert.That(eye.Target, Is.EqualTo(anchor));
            Assert.That(eye.DrawFov, Is.True, "Only the client stops drawing FOV, the eye itself keeps it.");
            Assert.That(eye.PvsScale, Is.EqualTo(1f));
            Assert.That(ServerSession.ViewSubscriptions, Does.Contain(anchor), "PVS should be sent around the anchor.");
            Assert.That(SEntMan.GetComponent<EyeComponent>(anchor).PvsScale, Is.EqualTo(2f),
                "PVS range should grow around the anchor with the zoom.");
            Assert.That(SEntMan.GetComponent<ContentEyeComponent>(SPlayer).TargetZoom, Is.EqualTo(new Vector2(3f)));
        });

        await Client.WaitAssertion(() =>
        {
            var clientAnchor = ToClient(anchor);
            Assert.That(CEntMan.EntityExists(clientAnchor), "The anchor should reach the pilot's client.");
            Assert.That(CEntMan.GetComponent<EyeComponent>(CPlayer).Target, Is.EqualTo(clientAnchor),
                "The client's eye should be riding the anchor.");
            Assert.That(CEntMan.GetComponent<ShuttleCameraComponent>(CPlayer).Grid, Is.EqualTo(ToClient(_grid.Owner)));

            AssertClientView(true, "The client should be showing the view it was sent.");
            Assert.That(CEntMan.System<ShuttleExternalCameraSystem>().LookPoint, Is.EqualTo(HullCentre));
            AssertEyeOn(HullCentre, "The view should open on the middle of the hull.");
        });

        await SetCamera(ShuttleCameraView.External, 1.5f);
        await RunTicks(5);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<ShuttleCameraComponent>(SPlayer).Camera, Is.EqualTo(anchor));
            Assert.That(SEntMan.GetComponent<EyeComponent>(anchor).PvsScale, Is.EqualTo(1f),
                "The anchor's PVS range should come back in with the zoom.");
        });

        await SetCamera(ShuttleCameraView.Front, 3f);
        await RunTicks(5);

        await Server.WaitAssertion(() =>
        {
            var camera = SEntMan.GetComponent<ShuttleCameraComponent>(SPlayer);
            Assert.That(camera.Camera, Is.EqualTo(anchor), "Hull views and the external view share one camera.");
            Assert.That(camera.Grid, Is.Null, "Only the external view names a grid to pan on.");
            AssertAnchorAt(FrontCamera, "The front view should take the camera to the bow.");
        });

        await Client.WaitAssertion(() => AssertClientView(false, "A hull camera is looked through with FOV."));

        await SetCamera(ShuttleCameraView.External, 3f);
        await RunTicks(5);

        await Server.WaitAssertion(() =>
        {
            var camera = SEntMan.GetComponent<ShuttleCameraComponent>(SPlayer);
            Assert.That(camera.View, Is.EqualTo(ShuttleCameraView.External));
            Assert.That(camera.Camera, Is.EqualTo(anchor));
            Assert.That(camera.Grid, Is.EqualTo(_grid.Owner));
            AssertAnchorAt(HullCentre, "Coming from a hull view should start over the middle again.");

            Assert.That(SEntMan.GetComponent<EyeComponent>(SPlayer).Target, Is.EqualTo(anchor));
            Assert.That(ServerSession.ViewSubscriptions, Does.Contain(anchor),
                "The anchor should still be a PVS viewer after coming from a hull view.");
        });

        await Client.WaitAssertion(() =>
        {
            AssertClientView(true, "The client should take the view up again.");
            AssertEyeOn(HullCentre, "Coming from a hull view should look at the middle again.");
        });

        await LeaveHelm();
    }

    [Test]
    public async Task LeavingClearsTheAnchor()
    {
        await TakeHelm();
        await SetCamera(ShuttleCameraView.External, 3f);
        await RunTicks(10);

        var anchor = await GetAnchor();

        // Looked at off-centre, so there's an offset on the eye for leaving to clear.
        await Client.WaitPost(() => CEntMan.System<ShuttleExternalCameraSystem>().SetLookPoint(new Vector2(4f, 1f)));
        await RunTicks(5);
        await Client.WaitAssertion(() =>
        {
            AssertClientView(true, "The client should be showing the view.");
            AssertEyeOn(new Vector2(4f, 1f), "The eye should be carried off the anchor to where it was pointed.");
        });

        await SetCamera(ShuttleCameraView.Helm, 3f);
        await RunTicks(10);

        await Server.WaitAssertion(() =>
        {
            var camera = SEntMan.GetComponent<ShuttleCameraComponent>(SPlayer);
            Assert.That(camera.View, Is.EqualTo(ShuttleCameraView.Helm));
            Assert.That(camera.Camera, Is.Null);
            Assert.That(camera.Grid, Is.Null);
            Assert.That(SEntMan.EntityExists(anchor), Is.False, "The anchor should go with the view.");
            Assert.That(ServerSession.ViewSubscriptions, Does.Not.Contain(anchor));

            var eye = SEntMan.GetComponent<EyeComponent>(SPlayer);
            Assert.That(eye.Target, Is.Null);
            Assert.That(eye.DrawFov, Is.True);
            Assert.That(eye.PvsScale, Is.EqualTo(2f), "At the helm the wider range sits on the pilot.");
        });

        await Client.WaitAssertion(() =>
        {
            Assert.That(CEntMan.GetComponent<EyeComponent>(CPlayer).Target, Is.Null);
            Assert.That(CEntMan.GetComponent<ShuttleCameraComponent>(CPlayer).Grid, Is.Null);
            AssertClientView(false, "Going back to the helm should hand the eye back.");
        });

        await SetCamera(ShuttleCameraView.External, 3f);
        await RunTicks(10);

        anchor = await GetAnchor();

        await Client.WaitPost(() => CEntMan.System<ShuttleExternalCameraSystem>().SetLookPoint(new Vector2(4f, 1f)));
        await RunTicks(5);
        await Client.WaitAssertion(() =>
        {
            AssertClientView(true, "The client should be showing the view again.");
            AssertEyeOn(new Vector2(4f, 1f), "The eye should be carried off the anchor to where it was pointed.");
        });

        await Server.WaitPost(() => _consoleSystem.RemovePilot(SPlayer));
        await RunTicks(10);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<ShuttleCameraComponent>(SPlayer), Is.False);
            Assert.That(SEntMan.EntityExists(anchor), Is.False, "The anchor should go with the pilot.");
            Assert.That(ServerSession.ViewSubscriptions, Does.Not.Contain(anchor));

            var eye = SEntMan.GetComponent<EyeComponent>(SPlayer);
            Assert.That(eye.Target, Is.Null);
            Assert.That(eye.DrawFov, Is.True);
            Assert.That(eye.PvsScale, Is.EqualTo(1f));
            Assert.That(SEntMan.GetComponent<ContentEyeComponent>(SPlayer).TargetZoom, Is.EqualTo(Vector2.One));
        });

        await Client.WaitAssertion(() =>
        {
            Assert.That(CEntMan.HasComponent<ShuttleCameraComponent>(CPlayer), Is.False);
            Assert.That(CEntMan.GetComponent<EyeComponent>(CPlayer).Target, Is.Null);
            AssertClientView(false, "Getting up should hand the eye back.");
        });
    }

    [Test]
    public async Task PanMovesTheAnchorWithinReach()
    {
        await TakeHelm();
        await SetCamera(ShuttleCameraView.External, 2f);
        await RunTicks(5);

        await Pan(new Vector2(4f, 1f));
        await AnchorShouldBeAt(new Vector2(4f, 1f), "A pan over the hull should be taken as sent.");

        await Pan(new Vector2(100f, -100f));
        await AnchorShouldBeAt(new Vector2(5f + Margin, -Margin), "A pan past the margin should stop at it.");

        await Pan(new Vector2(float.NaN, 1f));
        await Pan(new Vector2(1f, float.PositiveInfinity));
        await Pan(new Vector2(float.NegativeInfinity, float.NaN));
        await AnchorShouldBeAt(new Vector2(5f + Margin, -Margin), "A pan that isn't a number should be dropped.");

        await PassPanThrottle();

        await Server.WaitPost(() =>
        {
            Raise(new ShuttleCameraPanMessage(new Vector2(1f, 1f)));
            Raise(new ShuttleCameraPanMessage(new Vector2(3f, 3f)));
        });

        await AnchorShouldBeAt(new Vector2(1f, 1f), "A second pan straight after the first should be dropped.");

        await Pan(new Vector2(3f, 3f));
        await AnchorShouldBeAt(new Vector2(3f, 3f), "Pans should be taken again once the throttle has passed.");

        await PassPanThrottle();

        await Server.WaitPost(() =>
        {
            var otherCoords = new EntityCoordinates(_grid.Owner, new Vector2(3.5f, 1.5f));
            var other = SEntMan.SpawnEntity("ComputerShuttle", otherCoords);
            Raise(new ShuttleCameraPanMessage(new Vector2(1f, 1f)), other);
        });

        await AnchorShouldBeAt(new Vector2(3f, 3f), "A console the player isn't flying shouldn't move their view.");

        await SetCamera(ShuttleCameraView.Front, 2f);
        await RunTicks(5);

        await Pan(new Vector2(1f, 1f));
        await AnchorShouldBeAt(FrontCamera, "Only the external view pans.");

        await LeaveHelm();
    }

    [Test]
    public async Task ZoomAloneKeepsTheViewAndPan()
    {
        await TakeHelm();
        await SetCamera(ShuttleCameraView.External, 3f, true);
        await RunTicks(5);

        var anchor = await GetAnchor();
        var panned = new Vector2(4f, 1f);

        await Pan(panned);
        await Zoom(2f);

        await Server.WaitAssertion(() =>
        {
            var camera = SEntMan.GetComponent<ShuttleCameraComponent>(SPlayer);
            Assert.That(camera.View, Is.EqualTo(ShuttleCameraView.External));
            Assert.That(camera.Zoom, Is.EqualTo(2f));
            Assert.That(camera.LowLight, Is.True);
            Assert.That(camera.Camera, Is.EqualTo(anchor));
            AssertAnchorAt(panned, "Zooming shouldn't move a panned view.");

            Assert.That(SEntMan.GetComponent<EyeComponent>(anchor).PvsScale, Is.EqualTo(2f / 1.5f).Within(0.001f));
            Assert.That(SEntMan.GetComponent<ContentEyeComponent>(SPlayer).TargetZoom, Is.EqualTo(new Vector2(2f)));

            var settings = SEntMan.GetComponent<ShuttleCameraSettingsComponent>(_console);
            Assert.That(settings.View, Is.EqualTo(ShuttleCameraView.External));
            Assert.That(settings.Zoom, Is.EqualTo(2f), "The console should remember the zoom for the next pilot.");
            Assert.That(settings.LowLight, Is.True);
        });

        await Zoom(50f);

        await Server.WaitAssertion(() =>
        {
            var camera = SEntMan.GetComponent<ShuttleCameraComponent>(SPlayer);
            Assert.That(camera.Zoom, Is.EqualTo(ShuttleCameraComponent.MaxZoom),
                "Out of range zooms are clamped rather than trusted.");
        });

        await Zoom(float.NaN);

        await Server.WaitAssertion(() =>
        {
            var camera = SEntMan.GetComponent<ShuttleCameraComponent>(SPlayer);
            Assert.That(camera.Zoom, Is.EqualTo(ShuttleCameraComponent.MaxZoom),
                "A zoom that isn't a number should be dropped.");
            AssertAnchorAt(panned, "Zooming shouldn't move a panned view.");
        });

        await SetCamera(ShuttleCameraView.Front, 3f, true);
        await RunTicks(5);
        await Zoom(1f);

        await Server.WaitAssertion(() =>
        {
            var camera = SEntMan.GetComponent<ShuttleCameraComponent>(SPlayer);
            Assert.That(camera.View, Is.EqualTo(ShuttleCameraView.Front), "The zoom works on whatever view is up.");
            Assert.That(camera.Zoom, Is.EqualTo(1f));
            Assert.That(camera.LowLight, Is.True);
            AssertAnchorAt(FrontCamera, "Zooming shouldn't move a hull camera.");
        });

        await LeaveHelm();
    }

    [Test]
    public async Task HullChangesKeepThePan()
    {
        await TakeHelm();
        await SetCamera(ShuttleCameraView.External, 2f);
        await RunTicks(5);

        var panned = new Vector2(4f, 1f);
        await Pan(panned);

        // Losing the tip of the bow brings the bounds in to (0, 0) to (5, 4).
        await Server.WaitPost(() => MapSystem.SetTile(_grid.Owner, _grid.Comp, new Vector2i(2, 4), Tile.Empty));

        // The hull is checked on an interval, so give it more than one.
        await RunSeconds(1.5f);

        await Server.WaitAssertion(() =>
        {
            var camera = SEntMan.GetComponent<ShuttleCameraComponent>(SPlayer);
            Assert.That(camera.View, Is.EqualTo(ShuttleCameraView.External));
            AssertAnchorAt(panned, "Losing hull shouldn't take the view back to the middle.");
        });

        await Pan(new Vector2(2.5f, 100f));
        await AnchorShouldBeAt(new Vector2(2.5f, 4f + Margin), "The pan should reach the margin past what's left.");

        // With the rest of the bow gone the anchor is further out than the margin allows.
        await Server.WaitPost(() => MapSystem.SetTile(_grid.Owner, _grid.Comp, new Vector2i(2, 3), Tile.Empty));
        await RunSeconds(1.5f);

        await Server.WaitAssertion(() =>
        {
            var camera = SEntMan.GetComponent<ShuttleCameraComponent>(SPlayer);
            Assert.That(camera.View, Is.EqualTo(ShuttleCameraView.External));
            Assert.That(camera.Grid, Is.EqualTo(_grid.Owner));
            AssertAnchorAt(new Vector2(2.5f, 3f + Margin), "A shrinking hull should pull the anchor in to its margin.");
        });

        await LeaveHelm();
    }

    [Test]
    public async Task LostAnchorIsPutBack()
    {
        await TakeHelm();
        await SetCamera(ShuttleCameraView.External, 2f);
        await RunTicks(5);

        var anchor = await GetAnchor();
        await Pan(new Vector2(4f, 1f));

        // A split hands whatever sat over the lost half to another grid. The map stands in for that grid here.
        await Server.WaitPost(() =>
        {
            Transform.SetCoordinates(anchor, new EntityCoordinates(MapData.MapUid, new Vector2(50f, 50f)));
        });

        await RunSeconds(1.5f);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<ShuttleCameraComponent>(SPlayer).Camera, Is.EqualTo(anchor));
            AssertAnchorAt(HullCentre, "An anchor that left the flown grid should come back over its middle.");
        });

        await Server.WaitPost(() => SEntMan.DeleteEntity(anchor));
        await RunSeconds(1.5f);

        await Server.WaitAssertion(() =>
        {
            var camera = SEntMan.GetComponent<ShuttleCameraComponent>(SPlayer);
            Assert.That(camera.View, Is.EqualTo(ShuttleCameraView.External));
            Assert.That(camera.Camera, Is.Not.Null.And.Not.EqualTo(anchor), "A deleted anchor should be replaced.");
            Assert.That(camera.Grid, Is.EqualTo(_grid.Owner));
            AssertAnchorAt(HullCentre, "A new anchor should start over the middle of the hull.");

            var replacement = camera.Camera!.Value;
            Assert.That(SEntMan.GetComponent<EyeComponent>(SPlayer).Target, Is.EqualTo(replacement));
            Assert.That(ServerSession.ViewSubscriptions, Does.Contain(replacement));
        });

        await LeaveHelm();
    }

    [Test]
    public async Task RefusedViewFallsBackToHelm()
    {
        await TakeHelm();
        await SetCamera(ShuttleCameraView.External, 2f);
        await RunTicks(5);

        var anchor = await GetAnchor();

        // The console now remembers the external view, which crew with no player behind them can't use.
        EntityUid crew = default;

        await Server.WaitPost(() =>
        {
            crew = SEntMan.SpawnEntity(PlayerPrototype, new EntityCoordinates(_grid.Owner, new Vector2(0.5f, 1.5f)));
            SEntMan.EnsureComponent<PilotComponent>(crew);
            _consoleSystem.AddPilot(_console, crew, SEntMan.GetComponent<ShuttleConsoleComponent>(_console));
        });

        await RunTicks(5);

        await Server.WaitAssertion(() =>
        {
            var camera = SEntMan.GetComponent<ShuttleCameraComponent>(crew);
            Assert.That(camera.View, Is.EqualTo(ShuttleCameraView.Helm), "Crew with no player should stay at the helm.");
            Assert.That(camera.Camera, Is.Null);
            Assert.That(camera.Grid, Is.Null);

            _consoleSystem.RemovePilot(crew);
        });

        await Client.WaitAssertion(() => AssertClientView(true, "The client should be showing the view."));

        // The server only looks at the view now and then. Starting from one look leaves room before the next.
        await PassHullCheck(anchor);

        // Turning the map into terrain puts the ship on a planet's ground layer.
        await Server.WaitPost(() => SEntMan.EnsureComponent<MapGridComponent>(MapData.MapUid));
        await RunTicks(5);

        await Client.WaitAssertion(() =>
        {
            Assert.That(CEntMan.HasComponent<MapGridComponent>(ToClient(MapData.MapUid)),
                "The client should have heard of the ground.");
            Assert.That(CEntMan.GetComponent<ShuttleCameraComponent>(CPlayer).View, Is.EqualTo(ShuttleCameraView.External),
                "The server shouldn't have looked at the view again yet.");
            AssertClientView(false, "The client shouldn't wait for the server before drawing FOV over a planet's ground.");
        });

        await RunSeconds(1.5f);

        await Server.WaitAssertion(() =>
        {
            var camera = SEntMan.GetComponent<ShuttleCameraComponent>(SPlayer);
            Assert.That(camera.View, Is.EqualTo(ShuttleCameraView.Helm), "A grounded ship should lose the external view.");
            Assert.That(camera.Camera, Is.Null);
            Assert.That(camera.Grid, Is.Null);
            Assert.That(SEntMan.EntityExists(anchor), Is.False);
            Assert.That(SEntMan.GetComponent<EyeComponent>(SPlayer).Target, Is.Null);
        });

        await Client.WaitAssertion(() =>
            Assert.That(RefusalsShown(), Is.EqualTo(1), "A pilot who loses the view should be told why."));

        await SetCamera(ShuttleCameraView.External, 2f);
        await RunTicks(5);

        await Server.WaitAssertion(() =>
        {
            var camera = SEntMan.GetComponent<ShuttleCameraComponent>(SPlayer);
            Assert.That(camera.View, Is.EqualTo(ShuttleCameraView.Helm), "A grounded ship can't ask for the external view.");
            Assert.That(camera.Camera, Is.Null);
        });

        await Client.WaitAssertion(() =>
            Assert.That(RefusalsShown(), Is.EqualTo(2), "A pilot who asks for a view they can't have should be told why."));

        await LeaveHelm();
    }

    /// <summary>
    /// The pilot's client carries the eye from the anchor to the point being looked at, draws it
    /// without FOV, and keeps the right-click menu to what the pilot could see anyway.
    /// </summary>
    [Test]
    public async Task EyeLandsOnTheLookPoint()
    {
        await TakeHelm();

        // Turned and moved off the origin, so a look point taken for a world position would show.
        await Server.WaitPost(() =>
        {
            Transform.SetWorldPosition(_grid.Owner, new Vector2(30f, -12f));
            Transform.SetWorldRotation(_grid.Owner, Angle.FromDegrees(40));
        });

        await RunTicks(10);

        await Client.WaitAssertion(() =>
        {
            var system = CEntMan.System<ShuttleExternalCameraSystem>();
            AssertClientView(false, "At the helm the client leaves the eye alone.");
            Assert.That(system.TryWheelZoom(1f), Is.False, "The wheel is only taken while the view is up.");
            Assert.That(MenuVisibilityFor(MenuVisibility.NoFov), Is.EqualTo(MenuVisibility.NoFov),
                "Outside the view the right-click menu is none of its business.");
        });

        await SetCamera(ShuttleCameraView.External, 2f);
        await RunTicks(10);

        var look = new Vector2(4f, 1f);

        await Client.WaitPost(() => CEntMan.System<ShuttleExternalCameraSystem>().SetLookPoint(look));
        await RunTicks(5);

        await Client.WaitAssertion(() =>
        {
            var system = CEntMan.System<ShuttleExternalCameraSystem>();
            AssertClientView(true, "The client should be showing the view.");
            Assert.That(system.LookPoint, Is.EqualTo(look));
            AssertEyeOn(look, "The eye should sit on the point it was sent to.");

            Assert.That(MenuVisibilityFor(MenuVisibility.NoFov | MenuVisibility.InContainer),
                Is.EqualTo(MenuVisibility.InContainer),
                "With FOV off the right-click menu shouldn't start listing what's under a roof.");
            Assert.That(system.TryWheelZoom(1f), Is.True, "The wheel zooms the view while it's up.");
        });

        // The anchor only ever trails the look point, and the server moves it on its own account too.
        await Pan(new Vector2(1f, 2f));
        await RunTicks(5);
        await AnchorShouldBeAt(new Vector2(1f, 2f), "The anchor should have moved under the view.");
        await Client.WaitAssertion(() => AssertEyeOn(look, "Moving the anchor shouldn't move what the pilot sees."));

        await Client.WaitPost(() =>
            CEntMan.System<ShuttleExternalCameraSystem>().SetLookPoint(new Vector2(100f, -100f)));
        await RunTicks(5);

        await Client.WaitAssertion(() =>
        {
            var reach = new Vector2(5f + Margin, -Margin);
            Assert.That(CEntMan.System<ShuttleExternalCameraSystem>().LookPoint, Is.EqualTo(reach),
                "The look point should stop at the margin the server keeps the anchor to.");
            AssertEyeOn(reach, "The eye should stop at the margin with it.");
        });

        await LeaveHelm();
        await Client.WaitAssertion(() => AssertClientView(false, "Getting up should hand the eye back."));
    }

    [Test]
    public async Task RightDragOwnsOnlyExternalWorldInputAndCancelsWithoutResuming()
    {
        await TakeHelm();
        await SetCamera(ShuttleCameraView.External, 2f);
        await RunTicks(5);
        await Client.WaitAssertion(() =>
        {
            var system = CEntMan.System<ShuttleExternalCameraSystem>();
            Assert.That(system.Active, Is.True);
            var ui = Client.ResolveDependency<IUserInterfaceManager>();
            var input = Client.ResolveDependency<IInputManager>();
            using var host = new Control { MouseFilter = Control.MouseFilterMode.Ignore };
            using var world = new MainViewport();
            using var other = new Button();
            world.Viewport.ViewportSize = new Vector2i(400, 300);
            world.Viewport.HorizontalExpand = world.Viewport.VerticalExpand = true;
            world.Viewport.Eye = CEntMan.GetComponent<EyeComponent>(CPlayer).Eye;
            host.AddChild(world);
            host.AddChild(other);
            ui.RootControl.AddChild(host);
            host.Measure(new Vector2(850, 340));
            host.Arrange(UIBox2.FromDimensions(Vector2.Zero, new Vector2(850, 340)));
            world.Arrange(UIBox2.FromDimensions(Vector2.Zero, new Vector2(400, 300)));
            other.Arrange(UIBox2.FromDimensions(new Vector2(420, 0), new Vector2(400, 300)));
            Assert.That(world.Viewport.Width, Is.GreaterThan(300f), "The test viewport must fill its MainViewport box for hit testing.");
            var pointer = new ScreenCoordinates(world.Viewport.GlobalPixelPosition + new Vector2(200, 150) * world.UIScale, world.Window!.Id);
            var outside = new ScreenCoordinates(other.GlobalPixelPosition + new Vector2(40, 40) * other.UIScale, other.Window!.Id);
            Assert.That(ui.MouseGetControl(pointer), Is.SameAs(world.Viewport));
            Assert.That(ui.MouseGetControl(outside), Is.SameAs(other));
            var handlers = (KeyEventAction?) input.GetType().GetField("FirstChanceOnKeyEvent", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(input);
            Assert.That(handlers?.GetInvocationList(), Has.Some.Matches<Delegate>(handler => ReferenceEquals(handler.Target, system)),
                "The physical pan hook must run before engine context-menu bindings.");

            Assert.That(Key(Keyboard.Key.MouseMiddle, KeyEventType.Down, pointer).Handled, Is.False);
            Assert.That(Key(Keyboard.Key.MouseLeft, KeyEventType.Down, pointer).Handled, Is.False);
            Assert.That(Key(Keyboard.Key.MouseRight, KeyEventType.Down, outside).Handled, Is.False,
                "A right click on another HUD control must retain its normal action.");
            foreach (var modifier in new[] { 1, 2, 3 })
                Assert.That(Key(Keyboard.Key.MouseRight, KeyEventType.Down, pointer, modifier: modifier).Handled, Is.False);
            Assert.That(Key(Keyboard.Key.MouseRight, KeyEventType.Repeat, pointer).Handled, Is.False);
            Assert.That(Dragging(), Is.False);

            // A press released where it landed is a click: it leaves the view alone and goes to the entity menu.
            var menuAsks = new List<ScreenCoordinates>();
            system.WfEntityMenuRequested += menuAsks.Add;
            var resting = system.LookPoint;
            var near = new ScreenCoordinates(pointer.Position + new Vector2(2, 0), pointer.Window);
            Assert.That(Key(Keyboard.Key.MouseRight, KeyEventType.Down, pointer).Handled, Is.True);
            Tick(near);
            Assert.That(system.LookPoint, Is.EqualTo(resting), "A press that hasn't strayed far must not nudge the view.");
            Assert.That(PanStart(), Is.EqualTo(pointer.Position), "A press within the click distance is still undecided.");
            Assert.That(menuAsks, Is.Empty, "The menu waits for the release.");
            Assert.That(Key(Keyboard.Key.MouseRight, KeyEventType.Up, near).Handled, Is.True);
            Assert.That(Dragging(), Is.False, "Releasing in place ends the press like any other release.");
            Assert.That(menuAsks, Is.EqualTo(new[] { near }), "A click asks for the entity menu once, where it was released.");
            CloseEntityMenu();

            Assert.That(Key(Keyboard.Key.MouseRight, KeyEventType.Down, pointer).Handled, Is.True,
                "The fresh world press must be consumed before either native right-click context binding runs.");
            Tick(outside);
            // The headless viewport cannot map screen points to the world, so the pan itself is not measurable here.
            Assert.That(PanStart(), Is.Null, "A press dragged past the click distance commits to a pan.");
            Assert.That(Dragging(), Is.True, "A captured pan continues across the viewport edge.");
            Assert.That(LastMouse(), Is.EqualTo(outside.Position));
            Tick(default);
            Assert.That(LastMouse(), Is.Null, "Leaving the window drops only the old position, preventing a return jump.");
            Tick(pointer);
            Assert.That(Dragging(), Is.True);
            Assert.That(LastMouse(), Is.EqualTo(pointer.Position));
            Assert.That(Key(Keyboard.Key.MouseRight, KeyEventType.Up, outside).Handled, Is.True);
            Assert.That(Dragging(), Is.False);
            Assert.That(menuAsks, Has.Count.EqualTo(1), "A press that became a pan must not open the menu when it ends.");
            system.WfEntityMenuRequested -= menuAsks.Add;
            Tick(pointer);
            Assert.That(Dragging(), Is.False, "A released drag cannot resume on pointer return.");

            Key(Keyboard.Key.MouseRight, KeyEventType.Down, pointer);
            Tick(pointer, false);
            Assert.That(Dragging(), Is.False, "Focus loss cancels capture.");
            Tick(pointer);
            Assert.That(Key(Keyboard.Key.MouseRight, KeyEventType.Repeat, pointer).Handled, Is.False);
            Assert.That(Dragging(), Is.False, "Refocusing while the button is held requires a fresh press.");
            Key(Keyboard.Key.MouseRight, KeyEventType.Down, pointer);
            world.Visible = false;
            Tick(pointer);
            Assert.That(Dragging(), Is.False, "A hidden viewport cannot keep panning.");
            world.Visible = true;
            Key(Keyboard.Key.MouseRight, KeyEventType.Down, pointer);
            host.RemoveChild(world);
            Tick(pointer);
            Assert.That(Dragging(), Is.False, "Removing a borrowed viewport cancels its capture.");
            host.AddChild(world);
            world.Arrange(UIBox2.FromDimensions(Vector2.Zero, new Vector2(400, 300)));
            Key(Keyboard.Key.MouseRight, KeyEventType.Down, pointer);
            system.WfEndCockpitInput();
            Assert.That(Dragging(), Is.False, "Leaving cockpit cancels a world pan even if external view remains selected.");
            Tick(pointer);
            Assert.That(Dragging(), Is.False);
            typeof(ShuttleExternalCameraSystem).GetMethod("Deactivate", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(system, null);
            Assert.That(Key(Keyboard.Key.MouseRight, KeyEventType.Down, pointer).Handled, Is.False,
                "The normal world view keeps its right-click menu when external view ends.");
            return;

            KeyEventArgs Key(Keyboard.Key key, KeyEventType type, ScreenCoordinates mouse, int modifier = 0)
            {
                var args = new KeyEventArgs(key, type == KeyEventType.Repeat, modifier == 1, modifier == 2, modifier == 3, false, 0);
                typeof(ShuttleExternalCameraSystem).GetMethod("OnPanKey", BindingFlags.Instance | BindingFlags.NonPublic,
                    null, new[] { typeof(KeyEventArgs), typeof(KeyEventType), typeof(ScreenCoordinates) }, null)!
                    .Invoke(system, new object[] { args, type, mouse });
                return args;
            }
            void Tick(ScreenCoordinates mouse, bool focused = true) =>
                typeof(ShuttleExternalCameraSystem).GetMethod("UpdateDrag", BindingFlags.Instance | BindingFlags.NonPublic,
                    null, new[] { typeof(EntityUid), typeof(ScreenCoordinates), typeof(bool) }, null)!
                    .Invoke(system, new object[] { ToClient(_grid.Owner), mouse, focused });
            bool Dragging() => (bool) typeof(ShuttleExternalCameraSystem)
                .GetField("_dragging", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(system)!;
            Vector2? LastMouse() => (Vector2?) typeof(ShuttleExternalCameraSystem)
                .GetField("_lastMouse", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(system);
            Vector2? PanStart() => (Vector2?) typeof(ShuttleExternalCameraSystem)
                .GetField("_panStart", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(system);
            void CloseEntityMenu()
            {
                var menus = ui.GetUIController<ContextMenuUIController>();
                if (menus.RootMenu?.Visible == true)
                    menus.Close();
            }
        });
        await LeaveHelm();
    }

    /// <summary>
    /// The roof the client draws over a hull follows its tiles: shaped hull pieces are covered, open
    /// lattice isn't, and the outline runs only where there's no roofed tile across it.
    /// </summary>
    [Test]
    public async Task HullsAreRoofedOver()
    {
        await TakeHelm();
        await SetCamera(ShuttleCameraView.External, 2f);

        var (plating, outline) = await CountRoof();

        Assert.Multiple(() =>
        {
            Assert.That(plating, Is.EqualTo(17 * 6), "Each of the hull's 17 squares is roofed with two triangles.");
            Assert.That(outline, Is.EqualTo(20 * 2), "The outline is one line for each of the 20 tile sides round the hull.");
        });

        // On top of the block's corner, sharing one side with it.
        await SetHullTile(new Vector2i(4, 3), Plating);
        var (withPlating, withPlatingOutline) = await CountRoof();

        Assert.Multiple(() =>
        {
            Assert.That(withPlating, Is.EqualTo(plating + 6), "A square of plating is roofed with two triangles.");
            Assert.That(withPlatingOutline, Is.EqualTo(outline + 4),
                "A new tile on the edge trades the side it covers for its other three.");
        });

        await SetHullTile(new Vector2i(0, 3), Lattice);
        var (withLattice, withLatticeOutline) = await CountRoof();

        Assert.Multiple(() =>
        {
            Assert.That(withLattice, Is.EqualTo(withPlating), "Open lattice should be left unroofed.");
            Assert.That(withLatticeOutline, Is.EqualTo(withPlatingOutline), "Lattice isn't part of the hull's outline.");
        });

        // Open to space like lattice is, but hull all the same.
        await SetHullTile(new Vector2i(0, 4), "PlatingCornerNE");
        var (withCorner, withCornerOutline) = await CountRoof();

        Assert.Multiple(() =>
        {
            Assert.That(withCorner, Is.EqualTo(withLattice + 3), "A diagonal hull corner is roofed with one triangle.");
            Assert.That(withCornerOutline, Is.EqualTo(withLatticeOutline + 6),
                "A corner standing by itself is outlined on all three sides.");
        });

        // With any altitude every viewport is drawn through stand-ins for its own eye, a camera monitor's included.
        await Client.WaitAssertion(() =>
        {
            var pilot = CEntMan.GetComponent<EyeComponent>(CPlayer).Eye;
            var here = pilot.Position;
            var turn = pilot.Rotation;

            var level = new ScalingViewport.ZEye(-1f, -0.5f, 0f) { Position = here, Rotation = turn, Primary = true };
            var below = new ScalingViewport.ZEye(-1f, -1f, 0f) { Position = here, Rotation = turn };
            var monitor = new ScalingViewport.ZEye(-1f, -0.5f, 0f)
            {
                Position = here.Offset(new Vector2(6f, 0f)),
                Rotation = turn,
                Primary = true,
            };

            Assert.Multiple(() =>
            {
                Assert.That(ShuttleHullRoofOverlay.IsPilotPass(pilot, pilot, true), "The pilot's own eye is theirs.");
                Assert.That(ShuttleHullRoofOverlay.IsPilotPass(level, pilot, true),
                    "So is the stand-in for it on their own level.");
                Assert.That(ShuttleHullRoofOverlay.IsPilotPass(below, pilot, false), "The level below is roofed as well.");
                Assert.That(ShuttleHullRoofOverlay.IsPilotPass(below, pilot, true), Is.False,
                    "Low-light is for the pilot's own level alone.");
                Assert.That(ShuttleHullRoofOverlay.IsPilotPass(monitor, pilot, false), Is.False,
                    "Another window's view isn't the pilot's to roof.");
                Assert.That(ShuttleHullRoofOverlay.IsPilotPass(monitor, pilot, true), Is.False,
                    "Nor to feed through low-light.");
                Assert.That(ShuttleHullRoofOverlay.IsPilotPass(null, pilot, false), Is.False);
            });
        });

        await LeaveHelm();
    }

    /// <summary>
    /// With the console's window open the client's own messages get through, so its pan and its wheel
    /// zoom are followed all the way to the server.
    /// </summary>
    [Test]
    public async Task PanAndZoomReachTheServer()
    {
        await BuildHull();
        await SpawnTarget("ComputerShuttle");
        _console = ToServer(Target!.Value);

        // The console runs off its own battery, which the power pass only finds every half second.
        await RunSeconds(1f);

        await Activate();
        Assert.That(IsUiOpen(ShuttleConsoleUiKey.Key), Is.True, "Using the console should open it and seat the player.");

        await SendBui(ShuttleConsoleUiKey.Key, new ShuttleCameraSetMessage(ShuttleCameraView.External, 2f, false));
        await AnchorShouldBeAt(HullCentre, "The view should start over the middle of the hull.");

        var look = new Vector2(4f, 1f);

        await Client.WaitPost(() => CEntMan.System<ShuttleExternalCameraSystem>().SetLookPoint(look));
        await RunSeconds(1f);

        await AnchorShouldBeAt(look, "The server should bring its anchor to where the client is looking.");
        await Client.WaitAssertion(() => AssertEyeOn(look, "The eye should stay on the look point as the anchor arrives."));

        await Client.WaitPost(() =>
            CEntMan.System<ShuttleExternalCameraSystem>().SetLookPoint(new Vector2(-100f, 100f)));
        await RunSeconds(1f);

        await AnchorShouldBeAt(new Vector2(-Margin, 5f + Margin), "Client and server should stop at the same margin.");

        // Three notches in, quicker than the server answers one.
        await Client.WaitAssertion(() =>
        {
            var system = CEntMan.System<ShuttleExternalCameraSystem>();

            for (var i = 0; i < 3; i++)
            {
                Assert.That(system.TryWheelZoom(1f), Is.True);
            }
        });

        await RunSeconds(1f);

        await Server.WaitAssertion(() =>
        {
            var camera = SEntMan.GetComponent<ShuttleCameraComponent>(SPlayer);
            Assert.That(camera.View, Is.EqualTo(ShuttleCameraView.External), "Zooming shouldn't change the view.");
            Assert.That(camera.Zoom, Is.EqualTo(1.25f), "Every notch should count, not only the first.");
            AssertAnchorAt(new Vector2(-Margin, 5f + Margin), "Zooming shouldn't move a panned view.");
        });

        await Client.WaitAssertion(() =>
        {
            Assert.That(CEntMan.GetComponent<ShuttleCameraComponent>(CPlayer).Zoom, Is.EqualTo(1.25f));
            AssertClientView(true, "The view should still be up.");
        });

        // A hard flick sends two notches as one event, and a touchpad sends one notch in pieces.
        await Client.WaitAssertion(() =>
        {
            var system = CEntMan.System<ShuttleExternalCameraSystem>();
            Assert.That(system.TryWheelZoom(-2f), Is.True);

            for (var i = 0; i < 4; i++)
            {
                Assert.That(system.TryWheelZoom(-0.25f), Is.True, "A piece of a notch is taken all the same.");
            }
        });

        await RunSeconds(1f);
        await ZoomShouldBe(2f, "Wheel travel should count by the notch, however many events it comes in.");

        // In and straight back out, before the server has answered the first notch.
        await Client.WaitPost(() => CEntMan.System<ShuttleExternalCameraSystem>().TryWheelZoom(1f));
        await RunTicks(1);
        await Client.WaitPost(() => CEntMan.System<ShuttleExternalCameraSystem>().TryWheelZoom(-1f));
        await RunSeconds(1f);
        await ZoomShouldBe(2f, "Two notches that cancel should leave the zoom where it was.");

        await LeaveHelm();
    }

    [Test]
    public async Task MobsAreHiddenFromTheView()
    {
        await TakeHelm();

        // Off the hull's side, with no roof to be under.
        var outside = new EntityCoordinates(_grid.Owner, new Vector2(-1.5f, 1.5f));
        EntityUid crew = default;

        await Server.WaitPost(() => crew = SEntMan.SpawnEntity(PlayerPrototype, outside));
        await RunTicks(5);

        await Client.WaitAssertion(() =>
            Assert.That(IsDrawn(ToClient(crew)), "Someone outside is drawn while the pilot looks from the helm."));

        await SetCamera(ShuttleCameraView.External, 2f);
        await RunTicks(5);

        // Jetpacks leave their trails on the client alone.
        EntityUid trail = default;

        await Client.WaitPost(() =>
            trail = CEntMan.SpawnEntity("JetpackEffect", CEntMan.GetCoordinates(SEntMan.GetNetCoordinates(outside))));
        await RunTicks(2);

        await Client.WaitAssertion(() =>
        {
            var system = CEntMan.System<ShuttleExternalCameraSystem>();
            var clientCrew = ToClient(crew);

            AssertClientView(true, "The client should be showing the view.");
            Assert.That(IsDrawn(clientCrew), Is.False, "Nobody outside the hull should be drawn in the view.");
            Assert.That(system.IsHidden(clientCrew), "Whatever draws by a mob's position should hear that it's hidden.");
            Assert.That(IsDrawn(trail), Is.False, "A jetpack's trail would give away whoever is wearing it.");
            Assert.That(IsDrawn(CPlayer), "The pilot's own sprite is left alone.");
            Assert.That(system.IsHidden(CPlayer), Is.False);

            // A disguise notes how its user looked as it goes on, and puts that back as it comes off.
            var disguise = CEntMan.AddComponent<ChameleonDisguisedComponent>(clientCrew);
            Assert.That(disguise.WasVisible, "A disguise shouldn't take the view's hiding for how its user looks.");
            Assert.That(IsDrawn(clientCrew), Is.False);

            CEntMan.RemoveComponent<ChameleonDisguisedComponent>(clientCrew);
            Assert.That(IsDrawn(clientCrew), "A disguise coming off should leave its user as it found them.");
        });

        await RunTicks(2);

        await Client.WaitAssertion(() =>
            Assert.That(IsDrawn(ToClient(crew)), Is.False, "The view should hide someone again once they're showing."));

        await SetCamera(ShuttleCameraView.Front, 2f);
        await RunTicks(5);

        await Client.WaitAssertion(() =>
        {
            var system = CEntMan.System<ShuttleExternalCameraSystem>();

            AssertClientView(false, "A hull camera is looked through with FOV.");
            Assert.That(IsDrawn(ToClient(crew)), "Leaving the view should put back everyone it hid.");
            Assert.That(system.IsHidden(ToClient(crew)), Is.False);
            Assert.That(CEntMan.EntityExists(trail), "The trail should outlast the view to be put back.");
            Assert.That(IsDrawn(trail), "Leaving the view should put jetpack trails back too.");
        });

        await LeaveHelm();
    }

    /// <summary>
    /// Whether an entity's sprite is set to be drawn. Client thread only.
    /// </summary>
    private bool IsDrawn(EntityUid uid)
    {
        return CEntMan.GetComponent<Robust.Client.GameObjects.SpriteComponent>(uid).Visible;
    }

    /// <summary>
    /// Builds the hull, bolts a console to it and seats the player there.
    /// </summary>
    private async Task TakeHelm()
    {
        await BuildHull();

        await Server.WaitPost(() =>
        {
            var consoleCoords = new EntityCoordinates(_grid.Owner, new Vector2(1.5f, 1.5f));
            _console = SEntMan.SpawnEntity("ComputerShuttle", consoleCoords);

            SEntMan.EnsureComponent<PilotComponent>(SPlayer);
            _consoleSystem.AddPilot(_console, SPlayer, SEntMan.GetComponent<ShuttleConsoleComponent>(_console));
        });

        await RunTicks(5);
    }

    /// <summary>
    /// Lays the test hull: a 5x3 block with a two tile bow sticking out of the middle.
    /// </summary>
    private async Task BuildHull()
    {
        _consoleSystem = SEntMan.System<ShuttleConsoleSystem>();
        _grid = MapData.Grid;

        await Server.WaitPost(() =>
        {
            var plating = new Tile(TileMan[Plating].TileId);
            var tiles = new List<(Vector2i GridIndices, Tile Tile)>();
            for (var x = 0; x < 5; x++)
            {
                for (var y = 0; y < 3; y++)
                {
                    tiles.Add((new Vector2i(x, y), plating));
                }
            }

            tiles.Add((new Vector2i(2, 3), plating));
            tiles.Add((new Vector2i(2, 4), plating));
            MapSystem.SetTiles(_grid.Owner, _grid.Comp, tiles);
        });
    }

    private async Task SetHullTile(Vector2i indices, string tile)
    {
        await Server.WaitPost(() =>
            MapSystem.SetTile(_grid.Owner, _grid.Comp, indices, new Tile(TileMan[tile].TileId)));
    }

    /// <summary>
    /// Has the client roof the hull over, and counts the vertices in its plating and in its outline.
    /// </summary>
    private async Task<(int Plating, int Outline)> CountRoof()
    {
        var counts = (0, 0);

        await RunTicks(5);

        await Client.WaitAssertion(() =>
        {
            var overlays = Client.ResolveDependency<IOverlayManager>();
            Assert.That(overlays.TryGetOverlay<ShuttleHullRoofOverlay>(out var overlay), "The roof overlay should be up.");

            var grid = ToClient(_grid.Owner);
            var roof = overlay!.GetRoof((grid, CEntMan.GetComponent<MapGridComponent>(grid)));
            counts = (roof.Count, roof.OutlineCount);
        });

        return counts;
    }

    /// <summary>
    /// What the right-click menu is allowed to list for the player, starting from the given flags.
    /// Client thread only.
    /// </summary>
    private MenuVisibility MenuVisibilityFor(MenuVisibility visibility)
    {
        var ev = new MenuVisibilityEvent { Visibility = visibility };
        CEntMan.EventBus.RaiseLocalEvent(CPlayer, ref ev);
        return ev.Visibility;
    }

    /// <summary>
    /// Checks whether the client has the view up, and that its eye draws FOV exactly when it hasn't.
    /// Client thread only.
    /// </summary>
    private void AssertClientView(bool external, string message)
    {
        var eye = CEntMan.GetComponent<EyeComponent>(CPlayer);
        var view = eye.Eye;

        Assert.That(CEntMan.System<ShuttleExternalCameraSystem>().Active, Is.EqualTo(external), message);
        Assert.That(view.DrawFov, Is.EqualTo(!external), "FOV should be drawn exactly when the view isn't up.");
        Assert.That(eye.DrawFov, Is.True, "Only the drawn eye loses FOV, its component keeps it.");

        if (!external)
            Assert.That(view.Offset, Is.EqualTo(Vector2.Zero), "Nothing should be left of the view's offset.");
    }

    /// <summary>
    /// Checks the client's eye is centred on a point of the flown grid. Client thread only.
    /// </summary>
    private void AssertEyeOn(Vector2 look, string message)
    {
        var matrix = CEntMan.System<SharedTransformSystem>().GetWorldMatrix(ToClient(_grid.Owner));
        var expected = Vector2.Transform(look, matrix);
        var view = CEntMan.GetComponent<EyeComponent>(CPlayer).Eye;
        var centre = view.Position.Position + view.Offset;

        Assert.That((centre - expected).Length(), Is.LessThan(0.01f), $"{message} It's on {centre}, not {expected}.");
    }

    private async Task LeaveHelm()
    {
        await Server.WaitPost(() => _consoleSystem.RemovePilot(SPlayer));
        await RunTicks(5);
    }

    private async Task SetCamera(ShuttleCameraView view, float zoom, bool lowLight = false)
    {
        await Server.WaitPost(() => Raise(new ShuttleCameraSetMessage(view, zoom, lowLight)));
    }

    private async Task Zoom(float zoom)
    {
        await Server.WaitPost(() => Raise(new ShuttleCameraZoomMessage(zoom)));
        await RunTicks(5);
    }

    /// <summary>
    /// Sends a pan once the server is ready to take another.
    /// </summary>
    private async Task Pan(Vector2 position)
    {
        await PassPanThrottle();
        await Server.WaitPost(() => Raise(new ShuttleCameraPanMessage(position)));
    }

    private async Task PassPanThrottle()
    {
        await RunSeconds(0.25f);
    }

    private async Task ZoomShouldBe(float expected, string message)
    {
        await Server.WaitAssertion(() =>
            Assert.That(SEntMan.GetComponent<ShuttleCameraComponent>(SPlayer).Zoom, Is.EqualTo(expected), message));
    }

    /// <summary>
    /// Runs up to the server's next look at the view, which leaves a whole interval before the one after.
    /// The look shows as an anchor that was put out of reach being pulled back in.
    /// </summary>
    private async Task PassHullCheck(EntityUid anchor)
    {
        var far = new Vector2(100f, 100f);

        await Server.WaitPost(() => Transform.SetCoordinates(anchor, new EntityCoordinates(_grid.Owner, far)));

        for (var i = 0; i < 30; i++)
        {
            await RunTicks(1);

            var pulledIn = false;
            await Server.WaitPost(() => pulledIn = SEntMan.GetComponent<TransformComponent>(anchor).LocalPosition != far);

            if (pulledIn)
                return;
        }

        Assert.Fail("The server never looked at the view.");
    }

    /// <summary>
    /// How many times the player has been told the view can't be had, going by the popups still up.
    /// Client thread only.
    /// </summary>
    private int RefusalsShown()
    {
        var loc = Client.ResolveDependency<ILocalizationManager>();
        Assert.That(loc.TryGetString("shuttle-console-camera-external-unavailable", out var text),
            "The reason should have text of its own.");

        var shown = 0;

        foreach (var label in CEntMan.System<PopupSystem>().WorldLabels)
        {
            if (label.Text.Contains(text!))
                shown += label.Repeats;
        }

        return shown;
    }

    /// <summary>
    /// Hands a console a message as if the player's open UI had sent it. Server thread only.
    /// </summary>
    private void Raise<T>(T message, EntityUid? console = null) where T : BoundUserInterfaceMessage
    {
        var target = console ?? _console;

        message.Actor = SPlayer;
        message.UiKey = ShuttleConsoleUiKey.Key;
        message.Entity = SEntMan.GetNetEntity(target);

        SEntMan.EventBus.RaiseLocalEvent(target, message);
    }

    private async Task<EntityUid> GetAnchor()
    {
        EntityUid anchor = default;

        await Server.WaitAssertion(() =>
        {
            var camera = SEntMan.GetComponent<ShuttleCameraComponent>(SPlayer);
            Assert.That(camera.Camera, Is.Not.Null, "The view should have put a camera on the hull.");
            anchor = camera.Camera!.Value;
        });

        return anchor;
    }

    private async Task AnchorShouldBeAt(Vector2 expected, string message)
    {
        await Server.WaitAssertion(() => AssertAnchorAt(expected, message));
    }

    /// <summary>
    /// Checks where the player's camera sits on the flown grid. Server thread only.
    /// </summary>
    private void AssertAnchorAt(Vector2 expected, string message)
    {
        var camera = SEntMan.GetComponent<ShuttleCameraComponent>(SPlayer);
        Assert.That(camera.Camera, Is.Not.Null, message);

        var xform = SEntMan.GetComponent<TransformComponent>(camera.Camera!.Value);
        Assert.That(xform.ParentUid, Is.EqualTo(_grid.Owner), "The camera should ride the flown grid.");
        Assert.That((xform.LocalPosition - expected).Length(), Is.LessThan(0.001f),
            $"{message} It's at {xform.LocalPosition}, not {expected}.");
    }
}
