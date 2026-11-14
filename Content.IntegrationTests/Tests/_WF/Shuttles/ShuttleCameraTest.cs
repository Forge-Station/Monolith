using System.Collections.Generic;
using System.Numerics;
using Content.Client._WF.Shuttles.Systems;
using Content.IntegrationTests.Tests.Interaction;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared._WF.Shuttles;
using Content.Shared.Movement.Components;
using Content.Shared.Shuttles.BUIStates;
using Content.Shared.Shuttles.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.Shuttles;

/// <summary>
/// A pilot's hull camera views: where each camera sits, that the eye rides it on both sides, and that
/// leaving the helm puts everything back.
/// </summary>
public sealed class ShuttleCameraTest : InteractionTest
{
    // The stock test mob has no zoom of its own to set.
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: InteractionTestMob
  id: ShuttleCameraTestMob
  components:
  - type: ContentEye
";

    protected override string PlayerPrototype => "ShuttleCameraTestMob";

    [Test]
    public async Task ViewsFollowTheHull()
    {
        var consoleSystem = SEntMan.System<ShuttleConsoleSystem>();
        var mapSystem = SEntMan.System<SharedMapSystem>();
        var grid = MapData.Grid;

        EntityUid console = default;

        await Server.WaitPost(() =>
        {
            // A 5x3 block with a two tile bow sticking out of the middle.
            var tiles = new List<(Vector2i GridIndices, Tile Tile)>();
            for (var x = 0; x < 5; x++)
            {
                for (var y = 0; y < 3; y++)
                {
                    tiles.Add((new Vector2i(x, y), new Tile(1)));
                }
            }

            tiles.Add((new Vector2i(2, 3), new Tile(1)));
            tiles.Add((new Vector2i(2, 4), new Tile(1)));
            mapSystem.SetTiles(grid.Owner, grid.Comp, tiles);

            console = SEntMan.SpawnEntity("ComputerShuttle", new EntityCoordinates(grid.Owner, new Vector2(1.5f, 1.5f)));

            SEntMan.EnsureComponent<PilotComponent>(SPlayer);
            consoleSystem.AddPilot(console, SPlayer, SEntMan.GetComponent<ShuttleConsoleComponent>(console));
        });

        await RunTicks(5);

        ShuttleCameraComponent camera = default!;

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.TryGetComponent(SPlayer, out camera), "Taking the helm should start the camera.");
            Assert.That(camera.View, Is.EqualTo(ShuttleCameraView.Helm));
            Assert.That(camera.Camera, Is.Null);
            Assert.That(camera.Zoom, Is.EqualTo(1.5f));
        });

        var expected = new Dictionary<ShuttleCameraView, Vector2>
        {
            [ShuttleCameraView.Front] = new(2.5f, 5.5f),
            [ShuttleCameraView.Rear] = new(2.5f, -0.5f),
            [ShuttleCameraView.Left] = new(-0.5f, 1.5f),
            [ShuttleCameraView.Right] = new(5.5f, 1.5f),
        };

        foreach (var (view, position) in expected)
        {
            await SetCamera(console, view, 3f);
            await RunTicks(10);

            EntityUid hullCamera = default;

            await Server.WaitAssertion(() =>
            {
                Assert.That(camera.View, Is.EqualTo(view));
                Assert.That(camera.Camera, Is.Not.Null, $"{view} should put a camera on the hull.");
                hullCamera = camera.Camera!.Value;

                var xform = SEntMan.GetComponent<TransformComponent>(hullCamera);
                Assert.That(xform.ParentUid, Is.EqualTo(grid.Owner), "The camera should ride the grid.");
                Assert.That(xform.LocalPosition, Is.EqualTo(position), $"{view} camera is in the wrong place.");

                Assert.That(SEntMan.GetComponent<EyeComponent>(SPlayer).Target, Is.EqualTo(hullCamera));
                Assert.That(SEntMan.GetComponent<EyeComponent>(hullCamera).PvsScale, Is.EqualTo(2f),
                    "PVS range should grow around the camera with the zoom.");
                Assert.That(SEntMan.GetComponent<ContentEyeComponent>(SPlayer).TargetZoom, Is.EqualTo(new Vector2(3f)));
            });

            await Client.WaitAssertion(() =>
            {
                var clientCamera = CEntMan.GetEntity(SEntMan.GetNetEntity(hullCamera));
                Assert.That(CEntMan.EntityExists(clientCamera), "The camera should reach the pilot's client.");
                var listener = CEntMan.System<ShuttleCameraListenerSystem>();
                var clientXform = CEntMan.System<SharedTransformSystem>();
                var body = clientXform.GetMapCoordinates(CPlayer);
                Assert.That(listener.Active, "A camera view should hear from the pilot's body.");
                Assert.That(listener.GetListener().MapId, Is.EqualTo(body.MapId));
                Assert.That((listener.GetListener().Position - body.Position).Length(), Is.LessThan(0.01f));
                var eyePosition = Client.ResolveDependency<Robust.Client.Graphics.IEyeManager>().CurrentEye.Position.Position;
                Assert.That((listener.SourceShift - (eyePosition - body.Position)).Length(), Is.LessThan(0.01f));
                Assert.That(listener.SourceShift.Length(), Is.GreaterThan(1f), "The camera should sit away from the body.");
                Assert.That(CEntMan.GetComponent<EyeComponent>(CPlayer).Target, Is.EqualTo(clientCamera),
                    "The client's eye should be riding the camera.");
            });
        }

        // Shoot the bow off and the front camera should fall back to what's left of the hull.
        await SetCamera(console, ShuttleCameraView.Front, 3f);
        await RunTicks(5);

        await Server.WaitPost(() =>
        {
            mapSystem.SetTile(grid.Owner, grid.Comp, new Vector2i(2, 4), Tile.Empty);
            mapSystem.SetTile(grid.Owner, grid.Comp, new Vector2i(2, 3), Tile.Empty);
        });

        // Placement is throttled, so give it more than one interval.
        await RunSeconds(1.5f);

        await Server.WaitAssertion(() =>
        {
            var xform = SEntMan.GetComponent<TransformComponent>(camera.Camera!.Value);
            Assert.That(xform.ParentUid, Is.EqualTo(grid.Owner));
            Assert.That(xform.LocalPosition, Is.EqualTo(new Vector2(2.5f, 3.5f)),
                "The front camera should follow the hull back.");
        });

        // Out of range requests are clamped rather than trusted.
        await SetCamera(console, ShuttleCameraView.Helm, 50f);
        await RunTicks(10);

        EntityUid lastCamera = default;

        await Server.WaitAssertion(() =>
        {
            Assert.That(camera.View, Is.EqualTo(ShuttleCameraView.Helm));
            Assert.That(camera.Camera, Is.Null);
            Assert.That(camera.Zoom, Is.EqualTo(ShuttleCameraComponent.MaxZoom));
            Assert.That(SEntMan.GetComponent<EyeComponent>(SPlayer).Target, Is.Null);
            Assert.That(SEntMan.GetComponent<EyeComponent>(SPlayer).PvsScale, Is.EqualTo(2f),
                "At the helm the wider range sits on the pilot, and stops growing before the zoom does.");

            // Losing the camera without the console's help still has to hand the range back.
            SEntMan.RemoveComponent<ShuttleCameraComponent>(SPlayer);
            Assert.That(SEntMan.GetComponent<EyeComponent>(SPlayer).PvsScale, Is.EqualTo(1f));
            Assert.That(SEntMan.GetComponent<ContentEyeComponent>(SPlayer).TargetZoom, Is.EqualTo(Vector2.One));
        });

        // Leave on a hull view, so getting up has something to clean away.
        await SetCamera(console, ShuttleCameraView.Front, 2f, true);
        await RunTicks(5);

        await Server.WaitPost(() => camera = SEntMan.GetComponent<ShuttleCameraComponent>(SPlayer));

        // Low-light is drawn by the client. Switching the eye's lighting off would take FOV with it.
        await Server.WaitAssertion(() =>
        {
            Assert.That(camera.LowLight, Is.True);
            Assert.That(SEntMan.GetComponent<EyeComponent>(SPlayer).DrawLight, Is.True);
            Assert.That(SEntMan.GetComponent<EyeComponent>(SPlayer).DrawFov, Is.True);
        });

        await Client.WaitAssertion(() =>
        {
            Assert.That(CEntMan.GetComponent<ShuttleCameraComponent>(CPlayer).LowLight, Is.True,
                "The client needs the setting to draw the feed.");
        });

        await Server.WaitPost(() =>
        {
            lastCamera = camera.Camera!.Value;
            consoleSystem.RemovePilot(SPlayer);
        });

        await RunTicks(10);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<ShuttleCameraComponent>(SPlayer), Is.False);
            Assert.That(SEntMan.EntityExists(lastCamera), Is.False, "The hull camera should go with the pilot.");

            var eye = SEntMan.GetComponent<EyeComponent>(SPlayer);
            Assert.That(eye.Target, Is.Null);
            Assert.That(eye.PvsScale, Is.EqualTo(1f));
            Assert.That(SEntMan.GetComponent<ContentEyeComponent>(SPlayer).TargetZoom, Is.EqualTo(Vector2.One));
        });

        await Client.WaitAssertion(() =>
        {
            Assert.That(CEntMan.GetComponent<EyeComponent>(CPlayer).Target, Is.Null);
            var listener = CEntMan.System<ShuttleCameraListenerSystem>();
            Assert.That(listener.Active, Is.False, "Back at the helm the engine hears from the eye again.");
            Assert.That(listener.SourceShift, Is.EqualTo(Vector2.Zero));
        });

        // Sitting back down picks up where the console was left.
        await Server.WaitPost(() =>
        {
            SEntMan.EnsureComponent<PilotComponent>(SPlayer);
            consoleSystem.AddPilot(console, SPlayer, SEntMan.GetComponent<ShuttleConsoleComponent>(console));
        });

        await RunTicks(5);

        await Server.WaitAssertion(() =>
        {
            var restored = SEntMan.GetComponent<ShuttleCameraComponent>(SPlayer);
            Assert.That(restored.View, Is.EqualTo(ShuttleCameraView.Front));
            Assert.That(restored.Zoom, Is.EqualTo(2f));
            Assert.That(restored.LowLight, Is.True);
            Assert.That(restored.Camera, Is.Not.Null);

            consoleSystem.RemovePilot(SPlayer);
        });

        await RunTicks(5);
    }

    private async Task SetCamera(EntityUid console, ShuttleCameraView view, float zoom, bool lowLight = false)
    {
        await Server.WaitPost(() =>
        {
            var message = new ShuttleCameraSetMessage(view, zoom, lowLight)
            {
                Actor = SPlayer,
                UiKey = ShuttleConsoleUiKey.Key,
                Entity = SEntMan.GetNetEntity(console),
            };

            SEntMan.EventBus.RaiseLocalEvent(console, message);
        });
    }
}
