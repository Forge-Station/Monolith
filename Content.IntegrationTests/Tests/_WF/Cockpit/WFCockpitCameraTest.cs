#nullable enable annotations

using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Content.Client._WF.Shuttles.Systems;
using Content.Client._WF.Shuttles.UI;
using Content.Client.Gameplay;
using Content.Client.Shuttles.UI;
using Content.Server._WF.Cockpit;
using Content.Server._WF.Shuttles.Systems;
using Content.Server.Power.Components;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.Cockpit;
using Content.Shared._WF.Shuttles;
using Content.Shared.Access.Components;
using Content.Shared.Buckle;
using Content.Shared.Buckle.Components;
using Content.Shared.Shuttles.Components;
using Robust.Client.State;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.Cockpit;

/// <summary>Temporary pilot cameras cannot overwrite another pilot's persisted helm view.</summary>
public sealed class WFCockpitCameraTest
{
    [Test]
    public async Task OverlappingCameraLeasesPreserveSharedSettingsInEitherExitOrder()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var map = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var em = pair.Server.EntMan;
            var cockpit = em.System<WFCockpitGunnerySystem>();
            var cameras = em.System<Content.Server._WF.Shuttles.Systems.ShuttleCameraSystem>();
            var helm = em.SpawnEntity("ComputerShuttle", map.GridCoords);
            em.RemoveComponent<ApcPowerReceiverComponent>(helm);
            em.RemoveComponent<AccessReaderComponent>(helm);
            var first = em.SpawnEntity("MobHuman", map.GridCoords);
            var second = em.SpawnEntity("MobHuman", map.GridCoords);
            var ordinary = em.SpawnEntity("MobHuman", map.GridCoords);
            pair.Server.PlayerMan.SetAttachedEntity(pair.Player!, first);
            foreach (var actor in new[] { first, second, ordinary })
            {
                em.EnsureComponent<PilotComponent>(actor);
                em.System<ShuttleConsoleSystem>().AddPilot(helm, actor, em.GetComponent<ShuttleConsoleComponent>(helm));
                em.System<SharedUserInterfaceSystem>().OpenUi(helm, ShuttleConsoleUiKey.Key, actor);
            }

            foreach (var saved in new[] { false, true })
            foreach (var reverse in new[] { false, true })
            foreach (var changedByOrdinaryPilot in new[] { false, true })
            {
                em.RemoveComponent<ShuttleCameraSettingsComponent>(helm);
                if (saved)
                {
                    var original = em.AddComponent<ShuttleCameraSettingsComponent>(helm);
                    original.View = ShuttleCameraView.Left;
                    original.Zoom = 2f;
                    original.LowLight = true;
                }
                cameras.OnPilotAdded(first, (helm, em.GetComponent<ShuttleConsoleComponent>(helm)));
                var previous = em.GetComponent<ShuttleCameraComponent>(first);
                var previousView = previous.View;
                var previousZoom = previous.Zoom;
                var previousLowLight = previous.LowLight;

                // Isolate the camera leases; session authorization is exercised by the network test below.
                Lease("CaptureCamera", first, helm);
                Set(first, ShuttleCameraView.External, 2.5f, false);
                cameras.OnPilotAdded(second, (helm, em.GetComponent<ShuttleConsoleComponent>(helm)));
                Assert.That(em.GetComponent<ShuttleCameraComponent>(second).View,
                    Is.EqualTo(saved ? ShuttleCameraView.Left : ShuttleCameraView.Helm),
                    "A later pilot must inherit the saved helm view, never another cockpit's temporary EXT view.");
                Lease("CaptureCamera", second, helm);
                Set(second, ShuttleCameraView.Rear, 3f, false);
                em.System<SharedUserInterfaceSystem>().RaiseUiMessage(helm, ShuttleConsoleUiKey.Key,
                    new ShuttleCameraZoomMessage(4f) { Actor = second });
                AssertSaved(saved, ShuttleCameraView.Left, 2f, true);

                if (changedByOrdinaryPilot)
                    Set(ordinary, ShuttleCameraView.Right, 1.75f, false);
                var departing = reverse ? second : first;
                var remaining = reverse ? first : second;
                Lease("RestoreCamera", departing);
                Assert.That(cockpit.HasCameraSession(remaining, helm), Is.True);
                Lease("RestoreCamera", remaining);
                AssertSaved(saved || changedByOrdinaryPilot,
                    changedByOrdinaryPilot ? ShuttleCameraView.Right : ShuttleCameraView.Left,
                    changedByOrdinaryPilot ? 1.75f : 2f, !changedByOrdinaryPilot);
                var restored = em.GetComponent<ShuttleCameraComponent>(first);
                Assert.That((restored.View, restored.Zoom, restored.LowLight),
                    Is.EqualTo((previousView, previousZoom, previousLowLight)),
                    "The connected pilot's live camera must restore independently of shared settings and exit order.");
                Assert.That(cockpit.HasCameraSession(first, helm) || cockpit.HasCameraSession(second, helm), Is.False);
            }
            pair.Server.PlayerMan.SetAttachedEntity(pair.Player!, null);
            foreach (var actor in new[] { first, second, ordinary })
            {
                em.System<SharedUserInterfaceSystem>().CloseUi(helm, ShuttleConsoleUiKey.Key, actor);
                em.System<ShuttleConsoleSystem>().RemovePilot(actor);
                em.DeleteEntity(actor);
            }
            em.DeleteEntity(helm);
            return;

            void Set(EntityUid actor, ShuttleCameraView view, float zoom, bool lowLight) =>
                em.System<SharedUserInterfaceSystem>().RaiseUiMessage(helm, ShuttleConsoleUiKey.Key,
                    new ShuttleCameraSetMessage(view, zoom, lowLight) { Actor = actor });

            void Lease(string method, params object[] args) =>
                typeof(WFCockpitGunnerySystem).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(cockpit, args);

            void AssertSaved(bool exists, ShuttleCameraView view, float zoom, bool lowLight)
            {
                Assert.That(em.TryGetComponent<ShuttleCameraSettingsComponent>(helm, out var settings), Is.EqualTo(exists));
                if (settings != null)
                    Assert.That((settings.View, settings.Zoom, settings.LowLight), Is.EqualTo((view, (float?) zoom, lowLight)),
                        "Temporary camera changes and either exit order must leave the shared settings alone.");
            }
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SameTickNetworkEntryAndExitKeepTheOriginalCamera()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var map = await pair.CreateTestMap();
        await pair.Client.WaitPost(() => pair.Client.CfgMan.SetCVar(WolfgateCVars.CockpitAutoEnter, false));
        var em = pair.Server.EntMan;
        EntityUid actor = default, helm = default, seat = default;
        ShuttleConsoleWindow? window = null;
        await pair.Server.WaitAssertion(() =>
        {
            var maps = em.System<SharedMapSystem>();
            for (var x = 0; x < 3; x++)
            for (var y = 0; y < 3; y++)
                maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, y), map.Tile.Tile);
            // Give the physical pilot a clear seat instead of overlapping the helm's solid fixture.
            var seatPosition = new EntityCoordinates(map.Grid.Owner, new Vector2(1.5f, 0.5f));
            actor = em.SpawnEntity("MobHuman", seatPosition);
            pair.Server.PlayerMan.SetAttachedEntity(pair.Player!, actor);
            helm = em.SpawnEntity("ComputerShuttle", new EntityCoordinates(map.Grid.Owner, new Vector2(1.5f, 1.5f)));
            seat = em.SpawnEntity("ChairPilotSeat", seatPosition);
            em.RemoveComponent<ApcPowerReceiverComponent>(helm);
            em.RemoveComponent<AccessReaderComponent>(helm);
            var original = em.AddComponent<ShuttleCameraSettingsComponent>(helm);
            original.View = ShuttleCameraView.Left;
            original.Zoom = 2f;
            original.LowLight = true;
            em.EnsureComponent<PilotComponent>(actor);
            em.System<ShuttleConsoleSystem>().AddPilot(helm, actor, em.GetComponent<ShuttleConsoleComponent>(helm));
            em.System<SharedUserInterfaceSystem>().OpenUi(helm, ShuttleConsoleUiKey.Key, actor);
            Assert.That(em.System<SharedBuckleSystem>().TryBuckle(actor, actor, seat), Is.True);
        });
        await pair.RunTicksSync(10);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(em.GetComponent<BuckleComponent>(actor).BuckledTo, Is.EqualTo(seat),
                "The physical test actor must remain seated while the entry request travels.");
            Assert.That(em.TryGetComponent<PilotComponent>(actor, out var piloting), Is.True);
            Assert.That(piloting!.Console, Is.EqualTo(helm));
            Assert.That(em.System<SharedWFCockpitSystem>().CanEnter(actor, helm), Is.True,
                "The native seat and live piloting prerequisites must hold before sending entry.");
            Assert.That(em.System<SharedUserInterfaceSystem>().IsUiOpen(helm, ShuttleConsoleUiKey.Key, actor), Is.True);
        });
        // A pooled client starts outside gameplay, where there is no in-game screen for the cockpit to use.
        await pair.Client.WaitPost(() => pair.Client.ResolveDependency<IStateManager>().RequestStateChange<GameplayState>());
        await pair.RunTicksSync(10);
        await pair.Client.WaitAssertion(() =>
        {
            var ui = pair.Client.ResolveDependency<IUserInterfaceManager>();
            window = Descendants(ui.WindowRoot).OfType<ShuttleConsoleWindow>().Single();
            var controller = ui.GetUIController<Content.Client._WF.Cockpit.WFCockpitUIController>();
            Assert.That(controller.CanEnter(window.WfCockpitConsole), Is.True,
                "The client must see the native seat and live piloting prerequisites before entry.");
            var sent = new List<string>();
            window.WfCockpitGunneryCommand += message =>
                sent.Add(message is WFCockpitGunnerySessionMessage { Active: true } ? "session" : message.GetType().Name);
            window.ShipCameraRequested += (view, _, _) => sent.Add(view.ToString());
            // The real entry path: its order of messages is what keeps the helm's saved camera untouched.
            Assert.That(controller.Enter(window), Is.True, "Entry must go through the cockpit controller and its view.");
            Assert.That(sent, Does.Contain("session"));
            Assert.That(sent, Does.Contain(nameof(ShuttleCameraView.External)));
            Assert.That(sent.IndexOf("session"), Is.LessThan(sent.IndexOf(nameof(ShuttleCameraView.External))),
                "The cockpit session must be sent before the default EXT camera request.");
        });
        await pair.RunTicksSync(10);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(em.System<WFCockpitGunnerySystem>().HasCameraSession(actor, helm), Is.True);
            Assert.That(em.GetComponent<ShuttleCameraComponent>(actor).View, Is.EqualTo(ShuttleCameraView.External));
            Assert.That(em.GetComponent<ShuttleCameraSettingsComponent>(helm).View, Is.EqualTo(ShuttleCameraView.Left),
                "The network entry session must arrive before its same-tick default EXT request.");
        });
        await pair.Client.WaitAssertion(() =>
        {
            var external = pair.Client.EntMan.System<ShuttleExternalCameraSystem>();
            Assert.That(external.TryWheelZoom(1f), Is.True);
            Assert.That(external.PendingZoom, Is.Not.Null);
            external.WfEndCockpitInput();
            Assert.That(external.PendingZoom, Is.Null, "Exiting cannot retry a queued temporary zoom after camera restoration.");
            window!.FindControl<ShuttleCameraBar>("CameraBar").FindControl<Slider>("ZoomSlider").Value = 3f;
            var controller = pair.Client.ResolveDependency<IUserInterfaceManager>()
                .GetUIController<Content.Client._WF.Cockpit.WFCockpitUIController>();
            controller.Exit(window);
            Assert.That(controller.Active, Is.False);
        });
        await pair.RunTicksSync(10);
        await pair.Server.WaitAssertion(() =>
        {
            var camera = em.GetComponent<ShuttleCameraComponent>(actor);
            var settings = em.GetComponent<ShuttleCameraSettingsComponent>(helm);
            Assert.That((camera.View, camera.Zoom, camera.LowLight), Is.EqualTo((ShuttleCameraView.Left, 2f, true)),
                "A same-tick camera change followed by exit must finish on the pre-cockpit live camera.");
            Assert.That((settings.View, settings.Zoom, settings.LowLight), Is.EqualTo((ShuttleCameraView.Left, (float?) 2f, true)));
            Assert.That(em.System<WFCockpitGunnerySystem>().HasCameraSession(actor, helm), Is.False);
            em.System<SharedUserInterfaceSystem>().CloseUi(helm, ShuttleConsoleUiKey.Key, actor);
            pair.Server.PlayerMan.SetAttachedEntity(pair.Player!, null);
            foreach (var entity in new[] { actor, seat, helm })
                em.DeleteEntity(entity);
        });
        await pair.CleanReturnAsync();
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        yield return root;
        foreach (var child in root.Children)
        foreach (var descendant in Descendants(child))
            yield return descendant;
    }
}
