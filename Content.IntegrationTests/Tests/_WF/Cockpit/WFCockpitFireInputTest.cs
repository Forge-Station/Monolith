#nullable enable annotations

using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Content.Client._Mono.Radar;
using Content.Client._WF.Cockpit;
using Content.Client._WF.CombatConsole;
using Content.Client.Shuttles.UI;
using Content.Client.UserInterface.Controls;
using Content.Client.Viewport;
using Content.Shared._Mono.FireControl;
using Content.Shared._Mono.Radar;
using Content.Shared.Input;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.ContentPack;
using Robust.Shared.GameObjects;
using Robust.Shared.Input;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._WF.Cockpit;

/// <summary>Checks native click interception, held-fire cadence and cancellation of stale presses.</summary>
public sealed class WFCockpitFireInputTest
{
    [Test]
    public async Task CockpitAimingConsumesClicksAndRequiresFreshPressAfterInterruption()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        await pair.Client.WaitAssertion(() =>
        {
            var ui = pair.Client.ResolveDependency<IUserInterfaceManager>();
            var maps = pair.Client.ResolveDependency<IMapManager>();
            var input = pair.Client.ResolveDependency<IInputManager>();
            var map = maps.CreateMap();
            var mapEntity = maps.GetMapEntityId(map);
            using var host = new Control { MouseFilter = Control.MouseFilterMode.Ignore };
            using var world = new MainViewport();
            using var nav = new ShuttleNavControl { WfFitInstrument = true, SetSize = new Vector2(float.NaN) };
            world.Viewport.ViewportSize = new Vector2i(400, 300);
            nav.SetMatrix(new EntityCoordinates(mapEntity, new Vector2(10, 20)), Angle.Zero);
            var lease = new WFCockpitLease();
            nav.WfCockpitInteraction(lease);
            var enabled = true;
            var aims = new List<(EntityCoordinates Target, bool Fire)>();
            using var fire = new WFCockpitFireInput(world, nav, () => enabled, () => true, (target, firing) => aims.Add((target, firing)), lease);
            host.AddChild(world);
            host.AddChild(nav);
            host.AddChild(fire);
            ui.RootControl.AddChild(host);
            ui.ReleaseKeyboardFocus();
            host.Measure(new Vector2(850, 340));
            host.Arrange(UIBox2.FromDimensions(Vector2.Zero, new Vector2(850, 340)));
            world.Arrange(UIBox2.FromDimensions(Vector2.Zero, new Vector2(400, 300)));
            nav.Arrange(UIBox2.FromDimensions(new Vector2(420, 0), new Vector2(400, 300)));
            var cursor = Pointer(nav, nav.Size / 2);
            var worldCursor = Pointer(world.Viewport, world.Viewport.Size / 2);
            Assert.That(ui.MouseGetControl(cursor), Is.SameAs(nav));
            Assert.That(ui.MouseGetControl(worldCursor), Is.SameAs(world.Viewport));
            var ordinaryRadarClicks = 0;
            nav.OnRadarClick += _ => ordinaryRadarClicks++;
            enabled = false;
            Assert.That(Key(world.Viewport, EngineKeyFunctions.UIClick, BoundKeyState.Down, worldCursor).Handled, Is.False,
                "Without gun control, ordinary world interaction must pass through even if weapons are selected.");
            Key(world.Viewport, EngineKeyFunctions.UIClick, BoundKeyState.Up, worldCursor);
            Assert.That(Key(nav, EngineKeyFunctions.UIClick, BoundKeyState.Down, cursor).Handled, Is.False,
                "Without gun control, ordinary navigation input must pass through.");
            Key(nav, EngineKeyFunctions.UIClick, BoundKeyState.Up, cursor);
            Tick(fire, 0.5f, cursor, false);
            Assert.That(aims, Is.Empty, "Without gun control, no passive targeting updates may be sent.");
            ordinaryRadarClicks = 0;
            enabled = true;
            void PressNav()
            {
                var before = aims.Count;
                Assert.That(Key(nav, EngineKeyFunctions.UIClick, BoundKeyState.Down, cursor).Handled, Is.True);
                Assert.That(aims, Has.Count.EqualTo(before + 1), "Every fresh press must fire immediately.");
                Assert.That(aims[^1].Fire, Is.True);
            }
            void ReleaseNav()
            {
                Assert.That(Key(nav, EngineKeyFunctions.UIClick, BoundKeyState.Up, cursor).Handled, Is.True,
                    "The inherited map control must forward a captured trigger release.");
            }

            var worldDown = Key(world.Viewport, EngineKeyFunctions.UIClick, BoundKeyState.Down, worldCursor);
            Assert.That(worldDown.Handled, Is.False, "A world viewport without a valid map target must not capture a trigger.");
            Assert.That(Key(world.Viewport, EngineKeyFunctions.UIClick, BoundKeyState.Up, worldCursor).Handled, Is.False);
            Assert.That(aims, Is.Empty, "A headless viewport has no rendered map target and must not send invalid coordinates.");
            Assert.That(Key(nav, EngineKeyFunctions.UseSecondary, BoundKeyState.Down, cursor).Handled, Is.False,
                "Fire input must leave the shared right-mouse pan gesture alone.");

            PressNav();
            Assert.That(aims, Has.Count.EqualTo(1), "The first shot is immediate.");
            Assert.That(aims[0].Fire, Is.True);
            Assert.That(Key(nav, EngineKeyFunctions.Use, BoundKeyState.Down, cursor, newPhysicalEvent: false).Handled, Is.True);
            Assert.That(aims, Has.Count.EqualTo(1), "Duplicate functions for one physical button must not double-fire.");
            Assert.That(ordinaryRadarClicks, Is.Zero, "Consumed cockpit input must bypass the inherited radar firing loop.");
            Tick(fire, 0.05f, cursor, true);
            Assert.That(aims, Has.Count.EqualTo(1));
            Tick(fire, 0.06f, cursor, true);
            Assert.That(aims, Has.Count.EqualTo(2));
            Assert.That(aims[^1].Fire, Is.True);
            ReleaseNav();
            var afterRelease = aims.Count;
            Tick(fire, 0.11f, cursor, false);
            Tick(fire, 0.11f, cursor, false);
            Assert.That(aims, Has.Count.EqualTo(afterRelease), "A stationary released pointer must not repeatedly send the same target.");
            Tick(fire, 0.11f, Pointer(nav, nav.Size / 2 + new Vector2(2, 0)), false);
            Assert.That(aims, Has.Count.EqualTo(afterRelease + 1));
            Assert.That(aims[^1].Fire, Is.False, "Moving after release continues missile guidance without firing.");

            PressNav();
            enabled = false;
            var beforeLoss = aims.Count;
            Tick(fire, 0.11f, cursor, true);
            Assert.That(aims, Has.Count.EqualTo(beforeLoss));
            enabled = true;
            Tick(fire, 0.11f, cursor, true);
            Assert.That(aims[^1].Fire, Is.False, "Restoring the server link must not resume a held trigger.");
            ReleaseNav();

            PressNav();
            beforeLoss = aims.Count;
            fire.Cancel();
            Assert.That(Key(nav, EngineKeyFunctions.Use, BoundKeyState.Down, cursor, newPhysicalEvent: false).Handled, Is.True);
            Assert.That(aims, Has.Count.EqualTo(beforeLoss), "A replacement link must not rearm from a duplicate bound function.");
            Tick(fire, 0.11f, cursor, true);
            Assert.That(aims[^1].Fire, Is.False, "Changing the console cancels the held trigger even while link availability stays true.");
            ReleaseNav();

            PressNav();
            Invoke(nav, "MouseExited");
            Tick(fire, 0.11f, cursor, true);
            Assert.That(aims[^1].Fire, Is.False, "Re-entering the map while still held must require a new press.");
            ReleaseNav();

            PressNav();
            Tick(fire, 0.11f, cursor, true, false);
            Tick(fire, 0.11f, cursor, true);
            Assert.That(aims[^1].Fire, Is.False, "Restoring window focus must not resume firing.");
            ReleaseNav();

            PressNav();
            nav.Visible = false;
            nav.Visible = true;
            Tick(fire, 0.11f, cursor, true);
            Assert.That(aims[^1].Fire, Is.False, "Changing MFD pages releases the trigger.");
            ReleaseNav();

            PressNav();
            world.Visible = false;
            Assert.That(ui.MouseGetControl(worldCursor), Is.Not.SameAs(world.Viewport), "A hidden world view cannot be hovered.");
            beforeLoss = aims.Count;
            Tick(fire, 0.11f, cursor, true);
            Assert.That(aims[^1].Fire, Is.False, "Hiding the world view while a trigger is held releases it.");
            ReleaseNav();
            beforeLoss = aims.Count;
            Tick(fire, 0.5f, worldCursor, true);
            Assert.That(aims, Has.Count.EqualTo(beforeLoss), "A hidden world view is not an aim surface.");
            Assert.That(Key(world.Viewport, EngineKeyFunctions.UIClick, BoundKeyState.Down, worldCursor).Handled, Is.False,
                "A hidden world view must not capture clicks.");
            Key(world.Viewport, EngineKeyFunctions.UIClick, BoundKeyState.Up, worldCursor);
            Assert.That(aims, Has.Count.EqualTo(beforeLoss));
            PressNav();
            ReleaseNav();
            Assert.That(aims, Has.Count.EqualTo(beforeLoss + 1), "Aiming through the NAV plot keeps working with the world view hidden.");
            world.Visible = true;
            Assert.That(ui.MouseGetControl(worldCursor), Is.SameAs(world.Viewport), "The world view is hoverable again once shown.");

            using var chat = new LineEdit();
            host.AddChild(chat);
            chat.GrabKeyboardFocus();
            beforeLoss = aims.Count;
            Assert.That(Key(nav, EngineKeyFunctions.Use, BoundKeyState.Down, cursor).Handled, Is.False);
            Assert.That(aims, Has.Count.EqualTo(beforeLoss), "A focused chat or command field must prevent the first shot too.");
            Key(nav, EngineKeyFunctions.Use, BoundKeyState.Up, cursor);
            ui.ReleaseKeyboardFocus();
            Assert.That(Key(nav, EngineKeyFunctions.Use, BoundKeyState.Down, cursor, false).Handled, Is.False);
            Assert.That(aims, Has.Count.EqualTo(beforeLoss), "A keyboard Use binding must not become a cockpit trigger.");
            Key(nav, EngineKeyFunctions.Use, BoundKeyState.Up, cursor);
            Assert.That(Key(nav, EngineKeyFunctions.Use, BoundKeyState.Down, cursor, false, true).Handled, Is.False);
            Assert.That(aims, Has.Count.EqualTo(beforeLoss), "A keyboard Use binding cannot arm fire while an unrelated left press is held.");
            Key(nav, EngineKeyFunctions.Use, BoundKeyState.Up, cursor);

            var oldContext = input.Contexts.ActiveContext.Name;
            const string testContext = "wf-cockpit-fire-test";
            input.Contexts.New(testContext, input.Contexts.ActiveContext);
            PressNav();
            input.Contexts.SetActiveContext(testContext);
            Tick(fire, 0.11f, cursor, true);
            Assert.That(aims[^1].Fire, Is.False, "A different input context invalidates the held trigger.");
            input.Contexts.SetActiveContext(oldContext);
            input.Contexts.Remove(testContext);
            ReleaseNav();

            enabled = false;
            Assert.That(Key(nav, EngineKeyFunctions.UIClick, BoundKeyState.Down, cursor).Handled, Is.False,
                "Without a gunnery link, normal navigation input remains available.");
            Key(nav, EngineKeyFunctions.UIClick, BoundKeyState.Up, cursor);
            Assert.That(ordinaryRadarClicks, Is.EqualTo(2));
            enabled = true;
            PressNav();
            Assert.That(PhysicalHandlers(input)!.GetInvocationList().Any(handler => ReferenceEquals(handler.Target, fire)), Is.True);
            lease.Restore();
            Assert.That(PhysicalHandlers(input)?.GetInvocationList().Any(handler => ReferenceEquals(handler.Target, fire)) ?? false, Is.False,
                "Exiting cockpit must unsubscribe the physical input hook.");
            var beforeExit = aims.Count;
            Tick(fire, 1, cursor, true);
            Assert.That(aims, Has.Count.EqualTo(beforeExit), "Exiting cockpit removes the firing hooks and timer.");
            Assert.That(Key(nav, EngineKeyFunctions.UIClick, BoundKeyState.Down, cursor).Handled, Is.False);
            Key(nav, EngineKeyFunctions.UIClick, BoundKeyState.Up, cursor);
            maps.DeleteMap(map);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task WorldAimRidesThePilotsShip()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        await pair.Client.WaitAssertion(() =>
        {
            var entities = pair.Client.EntMan;
            var maps = pair.Client.ResolveDependency<IMapManager>();
            var transforms = entities.System<SharedTransformSystem>();
            var map = maps.CreateMap();
            var ship = maps.CreateGridEntity(map);
            entities.System<SharedMapSystem>().SetTile(ship.Owner, ship.Comp, new Vector2i(1, 1), new Tile(1));
            transforms.SetWorldPosition(ship.Owner, new Vector2(100, 40));
            var pilot = entities.SpawnEntity(null, new EntityCoordinates(ship.Owner, new Vector2(1.5f, 1.5f)));
            Assert.That(entities.GetComponent<TransformComponent>(pilot).GridUid, Is.EqualTo(ship.Owner));
            var point = new MapCoordinates(new Vector2(130, 55), map);
            var aim = WFCockpitFireInput.ShipRelative(entities, pilot, point);
            Assert.That(aim.EntityId, Is.EqualTo(ship.Owner), "The aim point must be expressed on the pilot's ship.");
            Assert.That((transforms.ToMapCoordinates(aim).Position - point.Position).Length(), Is.LessThan(0.001f));
            transforms.SetWorldPosition(ship.Owner, new Vector2(160, 40));
            Assert.That((transforms.ToMapCoordinates(aim).Position - new Vector2(190, 55)).Length(), Is.LessThan(0.001f),
                "When the ship moves the aim point moves with it, as the pointer does on the ship cam.");
            var adrift = entities.SpawnEntity(null, new MapCoordinates(new Vector2(5, 5), map));
            Assert.That(WFCockpitFireInput.ShipRelative(entities, adrift, point).EntityId, Is.Not.EqualTo(ship.Owner),
                "Without a ship the point stays on the map.");
            Assert.That(WFCockpitFireInput.ShipRelative(entities, null, point).IsValid(entities));
            entities.DeleteEntity(pilot);
            entities.DeleteEntity(adrift);
            maps.DeleteMap(map);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ReticleTracksArmedHoverAndRestoresBorrowedCursors()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        await pair.Client.WaitAssertion(() =>
        {
            Assert.That(WFCockpitFireInput.ReticlePath.ToString(), Does.Not.Contain(".rsi/"),
                "A packaged client packs every RSI into one file, so a state's PNG cannot be read by path.");
            Assert.That(pair.Client.ResolveDependency<IResourceManager>().ContentFileExists(WFCockpitFireInput.ReticlePath), Is.True);
            var ui = pair.Client.ResolveDependency<IUserInterfaceManager>();
            var input = pair.Client.ResolveDependency<IInputManager>();
            var maps = pair.Client.ResolveDependency<IMapManager>();
            var clyde = pair.Client.ResolveDependency<IClyde>();
            var map = maps.CreateMap();
            var origin = new EntityCoordinates(maps.GetMapEntityId(map), Vector2.Zero);
            using var host = new Control { MouseFilter = Control.MouseFilterMode.Ignore };
            using var world = new MainViewport();
            using var nav = new ShuttleNavControl { WfFitInstrument = true, SetSize = new Vector2(float.NaN) };
            var originalWorld = clyde.GetStandardCursor(StandardCursorShape.Hand);
            world.Viewport.CustomCursorShape = originalWorld;
            nav.DefaultCursorShape = Control.CursorShape.Pointer;
            world.Viewport.ViewportSize = new Vector2i(400, 300);
            nav.SetMatrix(origin, Angle.Zero);
            var lease = new WFCockpitLease();
            nav.WfCockpitInteraction(lease);
            var linked = true;
            var armed = false;
            var aims = new List<(EntityCoordinates Target, bool Fire)>();
            using var fire = new WFCockpitFireInput(world, nav, () => linked, () => armed, (target, firing) => aims.Add((target, firing)), lease);
            host.AddChild(world);
            host.AddChild(nav);
            host.AddChild(fire);
            ui.RootControl.AddChild(host);
            ui.ReleaseKeyboardFocus();
            host.Measure(new Vector2(850, 340));
            host.Arrange(UIBox2.FromDimensions(Vector2.Zero, new Vector2(850, 340)));
            world.Arrange(UIBox2.FromDimensions(Vector2.Zero, new Vector2(400, 300)));
            nav.Arrange(UIBox2.FromDimensions(new Vector2(420, 0), new Vector2(400, 300)));
            var pointer = Pointer(nav, nav.Size / 2);
            Tick(fire, 0, pointer, false);
            Assert.That(nav.DefaultCursorShape, Is.EqualTo(Control.CursorShape.Pointer), "A live link without selected weapons is not armed.");
            Assert.That(Key(nav, EngineKeyFunctions.UIClick, BoundKeyState.Down, pointer).Handled, Is.False,
                "An unarmed cockpit must leave navigation input available.");
            Key(nav, EngineKeyFunctions.UIClick, BoundKeyState.Up, pointer);
            Tick(fire, 0.5f, pointer, false);
            Assert.That(aims, Is.Empty, "A linked gun bank without selected weapons must not send hover aim.");
            Assert.That(fire.AimTarget, Is.Null, "An unarmed bank has no aim point to preview.");
            armed = true;
            Tick(fire, 0, pointer, false);
            Assert.That(fire.AimTarget, Is.Not.Null, "An armed hover publishes the aim point the preview lines end at.");
            var transforms = pair.Client.EntMan.System<SharedTransformSystem>();
            Assert.That(nav.WfCockpitTryGetWorldToView(out var worldToView, out var plotMap), Is.True);
            Assert.That(plotMap, Is.EqualTo(map));
            var aimPixel = Vector2.Transform(transforms.ToMapCoordinates(fire.AimTarget!.Value).Position, worldToView);
            Assert.That((aimPixel - nav.PixelSize / 2f).Length(), Is.LessThan(2f),
                "The preview transform must agree with the plot's own aim mapping at the pointer.");
            var east = Vector2.Transform(new Vector2(10, 0), worldToView);
            var north = Vector2.Transform(new Vector2(0, 10), worldToView);
            Assert.That(east.X, Is.GreaterThan(aimPixel.X + 1), "World east is plot right.");
            Assert.That(north.Y, Is.LessThan(aimPixel.Y - 1), "World north is plot up.");
            var selectedWeapon = UnknownWeapon(1);
            var preview = new WFCockpitAimPreview(() => fire.AimTarget, weapon => weapon == selectedWeapon);
            var lines = new WFCockpitAimLines(nav, preview);
            Assert.That(lines.Lines(), Is.Empty, "No weapons, no preview.");
            var mount = pair.Client.EntMan.SpawnEntity(null, new MapCoordinates(new Vector2(6, 3), map));
            var idle = pair.Client.EntMan.SpawnEntity(null, new MapCoordinates(new Vector2(-4, 2), map));
            preview.UpdateWeapons(new[]
            {
                new FireControllableEntry { NetEntity = selectedWeapon, Coordinates = pair.Client.EntMan.GetNetCoordinates(new EntityCoordinates(mount, Vector2.Zero)), IgnoresLos = true },
                new FireControllableEntry { NetEntity = UnknownWeapon(2), Coordinates = pair.Client.EntMan.GetNetCoordinates(new EntityCoordinates(idle, Vector2.Zero)), IgnoresLos = true },
            });
            var drawn = lines.Lines().ToArray();
            Assert.That(drawn, Has.Length.EqualTo(1), "Only selected weapons draw a preview line.");
            Assert.That((drawn[0].From - Vector2.Transform(new Vector2(6, 3), worldToView)).Length(), Is.LessThan(0.5f), "The line starts at the weapon.");
            Assert.That((drawn[0].To - aimPixel).Length(), Is.LessThan(0.5f), "The line ends at the aim point.");
            lines.Dispose();
            var reticle = nav.CustomCursorShape;
            Assert.That(reticle, Is.Not.Null, "Armed plots use the visible gun-sight asset rather than a platform crosshair.");
            Assert.That(world.Viewport.CustomCursorShape, Is.SameAs(originalWorld), "Only the hovered aiming surface changes cursor.");
            linked = false;
            Tick(fire, 0, pointer, false);
            Assert.That(nav.DefaultCursorShape, Is.EqualTo(Control.CursorShape.Pointer));
            Assert.That(fire.AimTarget, Is.Null, "Losing the link removes the aim point and its preview.");
            linked = true;
            Tick(fire, 0, pointer, false);
            Assert.That(nav.CustomCursorShape, Is.SameAs(reticle));
            Tick(fire, 0, pointer, false, false);
            Assert.That(nav.DefaultCursorShape, Is.EqualTo(Control.CursorShape.Pointer), "Unfocused windows must not advertise firing.");
            var pressed = (bool[]) input.GetType().GetField("_keysPressed", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(input)!;
            pressed[(int) Keyboard.Key.Shift] = true;
            Tick(fire, 0, pointer, false);
            Assert.That(nav.DefaultCursorShape, Is.EqualTo(Control.CursorShape.Pointer), "Alternate modifier gestures retain their normal pointer.");
            pressed[(int) Keyboard.Key.Shift] = false;
            Tick(fire, 0, default, false);
            Assert.That(nav.DefaultCursorShape, Is.EqualTo(Control.CursorShape.Pointer), "An invalid pointer has no armed cursor.");
            using var chat = new LineEdit();
            host.AddChild(chat);
            chat.GrabKeyboardFocus();
            Tick(fire, 0, pointer, false);
            Assert.That(nav.DefaultCursorShape, Is.EqualTo(Control.CursorShape.Pointer));
            ui.ReleaseKeyboardFocus();
            using var button = new Button();
            host.AddChild(button);
            button.Arrange(UIBox2.FromDimensions(nav.Position + nav.Size / 2 - new Vector2(16), new Vector2(32)));
            Assert.That(ui.MouseGetControl(pointer), Is.SameAs(button));
            Tick(fire, 0, pointer, false);
            Assert.That(nav.DefaultCursorShape, Is.EqualTo(Control.CursorShape.Pointer), "HUD buttons must not acquire the reticle beneath them.");
            button.Visible = false;
            nav.SetMatrix(null, null);
            Tick(fire, 0, pointer, false);
            Assert.That(nav.DefaultCursorShape, Is.EqualTo(Control.CursorShape.Pointer), "A plot without a valid target frame is not an aim surface.");
            nav.SetMatrix(origin, Angle.Zero);
            Tick(fire, 0, pointer, false);
            Assert.That(nav.CustomCursorShape, Is.SameAs(reticle));
            armed = false;
            Tick(fire, 0, pointer, false);
            Assert.That(nav.DefaultCursorShape, Is.EqualTo(Control.CursorShape.Pointer));
            armed = true;
            Tick(fire, 0, pointer, false);
            lease.Restore();
            Assert.That(nav.DefaultCursorShape, Is.EqualTo(Control.CursorShape.Pointer));
            Assert.That(world.Viewport.CustomCursorShape, Is.SameAs(originalWorld), "Leaving cockpit restores pre-existing custom cursors exactly.");
            maps.DeleteMap(map);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task NavigationAimFollowsPanZoomAndRotation()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        await pair.Client.WaitAssertion(() =>
        {
            var maps = pair.Client.ResolveDependency<IMapManager>();
            var map = maps.CreateMap();
            var mapEntity = maps.GetMapEntityId(map);
            using var nav = new ShuttleNavControl { WfFitInstrument = true, SetSize = new Vector2(float.NaN), WorldRange = 40 };
            nav.SetMatrix(new EntityCoordinates(mapEntity, new Vector2(10, 20)), Angle.Zero);
            typeof(ShuttleNavControl).GetField("_rotation", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(nav, Angle.FromDegrees(90));
            nav.Offset = new Vector2(5, -3);
            nav.Measure(new Vector2(400));
            nav.Arrange(UIBox2.FromDimensions(Vector2.Zero, new Vector2(400)));
            var center = nav.Size / 2;
            var target = nav.WfCockpitAimCoordinates(center);
            Assert.That(Vector2.Distance(target.Position, new Vector2(13, 25)), Is.LessThan(0.01));
            var radius = (Math.Min(nav.PixelWidth, nav.PixelHeight) / 2 - (int) (4 * nav.UIScale)) / nav.UIScale;
            target = nav.WfCockpitAimCoordinates(center + new Vector2(radius, 0));
            Assert.That(Vector2.Distance(target.Position, new Vector2(13, 65)), Is.LessThan(0.01));
            nav.WorldRange = 20;
            target = nav.WfCockpitAimCoordinates(center + new Vector2(radius, 0));
            Assert.That(Vector2.Distance(target.Position, new Vector2(13, 45)), Is.LessThan(0.01));
            maps.DeleteMap(map);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task NavPlotAndShipCamDrawTheSameAimLines()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        await pair.Client.WaitAssertion(() =>
        {
            var ui = pair.Client.ResolveDependency<IUserInterfaceManager>();
            var maps = pair.Client.ResolveDependency<IMapManager>();
            var entities = pair.Client.EntMan;
            var map = maps.CreateMap();
            var otherMap = maps.CreateMap();
            var mapEntity = maps.GetMapEntityId(map);
            var gunA = UnknownWeapon(11);
            var gunB = UnknownWeapon(12);
            var selection = new HashSet<NetEntity> { gunA };
            EntityCoordinates? aim = new EntityCoordinates(mapEntity, new Vector2(12, 4));
            var preview = new WFCockpitAimPreview(() => aim, selection.Contains);
            using var host = new Control { MouseFilter = Control.MouseFilterMode.Ignore };
            using var world = new MainViewport();
            using var nav = new ShuttleNavControl { WfFitInstrument = true, SetSize = new Vector2(float.NaN), WorldRange = 40 };
            using var lines = new WFCockpitAimLines(nav, preview);
            var overlay = new WFCockpitAimOverlay(preview, world.Viewport);
            world.Viewport.ViewportSize = new Vector2i(400, 300);
            host.AddChild(world);
            host.AddChild(nav);
            ui.RootControl.AddChild(host);
            host.Measure(new Vector2(850, 340));
            host.Arrange(UIBox2.FromDimensions(Vector2.Zero, new Vector2(850, 340)));
            world.Arrange(UIBox2.FromDimensions(Vector2.Zero, new Vector2(400, 300)));
            nav.Arrange(UIBox2.FromDimensions(new Vector2(420, 0), new Vector2(400, 300)));
            var eye = new Robust.Shared.Graphics.Eye { Position = new MapCoordinates(Vector2.Zero, map) };
            world.Viewport.Eye = eye;
            nav.SetMatrix(new EntityCoordinates(mapEntity, new Vector2(10, 20)), Angle.Zero);

            var mount = entities.SpawnEntity(null, new MapCoordinates(new Vector2(6, 3), map));
            var mountAt = entities.GetNetCoordinates(new EntityCoordinates(mount, Vector2.Zero));
            preview.UpdateWeapons(new[]
            {
                new FireControllableEntry { NetEntity = gunA, Coordinates = mountAt, IgnoresLos = true },
                new FireControllableEntry { NetEntity = gunB, Coordinates = mountAt, IgnoresLos = true },
            });

            Assert.That(overlay.TryGetSegments(eye, map, out var cam), Is.True);
            var camLines = cam.ToArray();
            Assert.That(camLines, Has.Length.EqualTo(1), "Only the selected weapon draws a line on the ship cam.");
            Assert.That(Vector2.Distance(camLines[0].From, new Vector2(6, 3)), Is.LessThan(0.01f), "The ship cam line starts at the weapon.");
            Assert.That(Vector2.Distance(camLines[0].To, new Vector2(12, 4)), Is.LessThan(0.01f), "The ship cam line ends at the aim point.");
            Assert.That(camLines[0].Color, Is.EqualTo(WFInstrumentTheme.Accent), "A weapon with no radar blip uses the console accent.");

            Assert.That(nav.WfCockpitTryGetWorldToView(out var worldToView, out _), Is.True);
            var plot = lines.Lines().ToArray();
            Assert.That(plot, Has.Length.EqualTo(1), "The NAV plot draws the same weapons as the ship cam.");
            Assert.That(Vector2.Distance(plot[0].From, Vector2.Transform(camLines[0].From, worldToView)), Is.LessThan(0.01f));
            Assert.That(Vector2.Distance(plot[0].To, Vector2.Transform(camLines[0].To, worldToView)), Is.LessThan(0.01f));
            Assert.That(plot[0].Color, Is.EqualTo(camLines[0].Color));

            Assert.That(overlay.TryGetSegments(eye, otherMap, out _), Is.False, "Only the aim point's map is drawn on.");
            Assert.That(overlay.TryGetSegments(eye, MapId.Nullspace, out _), Is.False);
            Assert.That(overlay.TryGetSegments(null, map, out _), Is.False);
            Assert.That(overlay.TryGetSegments(new Robust.Shared.Graphics.Eye { Position = eye.Position }, map, out _), Is.False,
                "Another viewport's eye is not the cockpit's world view.");
            var level = new ScalingViewport.ZEye(0, 0, 0) { Primary = true, Position = eye.Position, Rotation = eye.Rotation };
            Assert.That(overlay.TryGetSegments(level, map, out _), Is.True, "The pilot's own level pass is the world view.");
            level.Primary = false;
            Assert.That(overlay.TryGetSegments(level, map, out _), Is.False, "Passes for the levels around it are skipped.");

            world.Visible = false;
            Assert.That(overlay.TryGetSegments(eye, map, out _), Is.False, "A hidden world view draws nothing.");
            Assert.That(lines.Lines(), Has.Count.EqualTo(1), "The NAV plot keeps its lines while the world view is hidden.");
            world.Visible = true;

            selection.Clear();
            Assert.That(overlay.TryGetSegments(eye, map, out _), Is.False, "Nothing selected, nothing drawn.");
            Assert.That(lines.Lines(), Is.Empty);
            selection.Add(gunB);
            aim = null;
            Assert.That(overlay.TryGetSegments(eye, map, out _), Is.False, "A pointer that is not aiming draws nothing.");
            Assert.That(lines.Lines(), Is.Empty);

            selection.Clear();
            selection.Add(gunA);
            aim = new EntityCoordinates(mapEntity, new Vector2(12, 4));
            var tracked = lines.Lines().ToArray();
            var carrier = entities.SpawnEntity(null, new MapCoordinates(new Vector2(4, -2), map));
            nav.SetMatrix(new EntityCoordinates(carrier, new Vector2(6, 22)), Angle.Zero);
            var panned = lines.Lines().ToArray();
            Assert.That(panned, Has.Length.EqualTo(tracked.Length), "Which entity the radar tracks cannot change which lines draw.");
            Assert.That(Vector2.Distance(panned[0].From, tracked[0].From), Is.LessThan(0.01f));
            Assert.That(Vector2.Distance(panned[0].To, tracked[0].To), Is.LessThan(0.01f));
            Assert.That(overlay.TryGetSegments(eye, map, out cam), Is.True);
            Assert.That(cam, Has.Count.EqualTo(1));

            nav.SetMatrix(new EntityCoordinates(maps.GetMapEntityId(otherMap), Vector2.Zero), Angle.Zero);
            Assert.That(lines.Lines(), Is.Empty, "A plot of another map has no line to draw.");

            maps.DeleteMap(map);
            maps.DeleteMap(otherMap);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AimLinesTakeTheWeaponsRadarBlipColour()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        await pair.Client.WaitAssertion(() =>
        {
            var maps = pair.Client.ResolveDependency<IMapManager>();
            var timing = pair.Client.ResolveDependency<IGameTiming>();
            var entities = pair.Client.EntMan;
            var map = maps.CreateMap();
            var mapEntity = maps.GetMapEntityId(map);
            var coloured = UnknownWeapon(21);
            var plain = UnknownWeapon(22);
            var at = entities.GetNetCoordinates(new EntityCoordinates(mapEntity, new Vector2(2, 2)));
            var preview = new WFCockpitAimPreview(() => new EntityCoordinates(mapEntity, new Vector2(12, 4)), _ => true);
            var radar = entities.System<RadarBlipsSystem>();
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var blipField = typeof(RadarBlipsSystem).GetField("_blips", flags)!;
            var paletteField = typeof(RadarBlipsSystem).GetField("_configPalette", flags)!;
            var updatedField = typeof(RadarBlipsSystem).GetField("_lastUpdatedTime", flags)!;
            var oldBlips = blipField.GetValue(radar);
            var oldPalette = paletteField.GetValue(radar);
            var oldUpdated = updatedField.GetValue(radar);
            try
            {
                paletteField.SetValue(radar, new List<BlipConfig> { new() { Color = Color.Cyan } });
                blipField.SetValue(radar, new List<BlipNetData> { new(coloured, at, Vector2.Zero, Angle.Zero, 0, null) });
                updatedField.SetValue(radar, timing.CurTime);
                preview.UpdateWeapons(new[]
                {
                    new FireControllableEntry { NetEntity = coloured, Coordinates = at, IgnoresLos = true },
                    new FireControllableEntry { NetEntity = plain, Coordinates = at, IgnoresLos = true },
                });

                var segments = preview.Refresh();
                Assert.That(segments, Has.Count.EqualTo(2));
                Assert.That(segments[0].Color, Is.EqualTo(Color.Cyan), "A weapon with a radar blip is drawn in the blip's colour.");
                Assert.That(segments[1].Color, Is.EqualTo(WFInstrumentTheme.Accent), "A weapon without one uses the console accent.");
            }
            finally
            {
                blipField.SetValue(radar, oldBlips);
                paletteField.SetValue(radar, oldPalette);
                updatedField.SetValue(radar, oldUpdated);
            }
            maps.DeleteMap(map);
        });
        await pair.CleanReturnAsync();
    }

    /// <summary>A weapon id no entity has, so the client falls back to the coordinates the gunnery state gave it.</summary>
    internal static NetEntity UnknownWeapon(int index) => new(9_000_000 + index);

    internal static ScreenCoordinates Pointer(Control control, Vector2 local) =>
        new(control.GlobalPixelPosition + local * control.UIScale, control.Window!.Id);

    internal static GUIBoundKeyEventArgs Key(Control control, BoundKeyFunction function, BoundKeyState state,
        ScreenCoordinates pointer, bool physicalMouse = true, bool heldMouse = false, bool newPhysicalEvent = true)
    {
        var input = IoCManager.Resolve<IInputManager>();
        var key = !physicalMouse ? Keyboard.Key.E : function == EngineKeyFunctions.UseSecondary ? Keyboard.Key.MouseRight :
            function == ContentKeyFunctions.MouseMiddle ? Keyboard.Key.MouseMiddle : Keyboard.Key.MouseLeft;
        if (newPhysicalEvent)
            PhysicalHandlers(input)?.Invoke(new KeyEventArgs(key, false, false, false, false, false, 0),
                state == BoundKeyState.Down ? KeyEventType.Down : KeyEventType.Up);
        var pressed = (bool[]) input.GetType().GetField("_keysPressed", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(input)!;
        pressed[(int) Keyboard.Key.MouseLeft] = heldMouse || key == Keyboard.Key.MouseLeft && state == BoundKeyState.Down;
        var args = new GUIBoundKeyEventArgs(function, state, pointer, true,
            pointer.Position / control.UIScale - control.GlobalPosition, pointer.Position - control.GlobalPixelPosition);
        Invoke(control, state == BoundKeyState.Down ? "KeyBindDown" : "KeyBindUp", args);
        return args;
    }

    private static KeyEventAction? PhysicalHandlers(IInputManager input) => (KeyEventAction?) input.GetType()
        .GetField("FirstChanceOnKeyEvent", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(input);

    internal static void Tick(WFCockpitFireInput input, float delta, ScreenCoordinates pointer, bool leftDown, bool focused = true) =>
        Invoke(input, "UpdateInput", delta, pointer, leftDown, focused);

    private static void Invoke(object target, string method, params object[] args) => target.GetType()
        .GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, args);
}
