#nullable enable annotations

using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using Content.Client.Shuttles.UI;
using Robust.Client.UserInterface;
using System.Numerics;
using Content.Server._Mono.FireControl;
using Content.Server._Mono.Projectiles.TargetSeeking;
using Content.Server._WF.Cockpit;
using Content.Server._WF.CombatConsole;
using Content.Server.Players.PlayTimeTracking;
using Content.Server.Power.Components;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Server.Weapons.Ranged.Systems;
using Content.Shared._Mono.FireControl;
using Content.Shared._NF.Shuttles.Events;
using Content.Shared._Mono.ShipGuns;
using Content.Shared._Mono.Ships.Components;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.Cockpit;
using Content.Shared._WF.CombatConsole;
using Content.Shared.Access;
using Content.Shared.Access.Components;
using Content.Shared.Buckle;
using Content.Shared.Buckle.Components;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Interaction;
using Content.Shared.Projectiles;
using Content.Shared.Shuttles.BUIStates;
using Content.Shared.Shuttles.Components;
using Content.Shared.UserInterface;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.Enums;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Cockpit;

/// <summary>Exercises cockpit gunnery through the real helm subscription and existing group, flare and fire handlers.</summary>
[TestFixture]
public sealed class WFCockpitGunneryTest
{
    [Test]
    public async Task NearbyUnopenedConsoleUsesPhysicalReachAndRecoversAfterPowerLoss()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var map = await pair.CreateTestMap();
        await pair.Client.WaitPost(() => pair.Client.CfgMan.SetCVar(WolfgateCVars.CockpitAutoEnter, false));
        var em = pair.Server.EntMan;
        EntityUid actor = default, seat = default, helm = default, gun = default, server = default, groupedWeapon = default;
        NetEntity gunNet = default, weaponNet = default;
        ShuttleConsoleWindow? clientWindow = null;
        await pair.Server.WaitAssertion(() =>
        {
            var maps = em.System<SharedMapSystem>();
            for (var x = 0; x < 3; x++)
            for (var y = 0; y < 3; y++)
                maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, y), map.Tile.Tile);
            var seatPosition = new EntityCoordinates(map.Grid.Owner, new Vector2(1.5f, 0.5f));
            actor = em.SpawnEntity("MobHuman", seatPosition);
            pair.Server.PlayerMan.SetAttachedEntity(pair.Player!, actor);
            seat = em.SpawnEntity("ChairPilotSeat", seatPosition);
            helm = em.SpawnEntity("ComputerShuttle", new EntityCoordinates(map.Grid.Owner, new Vector2(1.5f, 1.5f)));
            gun = em.SpawnEntity("ComputerGunneryConsole", new EntityCoordinates(map.Grid.Owner, new Vector2(2.5f, 1.5f)));
            server = em.SpawnEntity("GunneryServerLow", new EntityCoordinates(map.Grid.Owner, new Vector2(0.5f, 2.5f)));
            foreach (var powered in new[] { helm, gun, server })
                em.GetComponent<ApcPowerReceiverComponent>(powered).Powered = true;
            em.RemoveComponent<AccessReaderComponent>(helm);
            em.RemoveComponent<AccessReaderComponent>(gun);
            var reader = em.AddComponent<AccessReaderComponent>(gun);
            reader.AccessLists.Add(new HashSet<ProtoId<AccessLevelPrototype>> { "Captain" });
            var ui = em.System<SharedUserInterfaceSystem>();
            var cockpit = em.System<WFCockpitGunnerySystem>();
            var transforms = em.System<SharedTransformSystem>();
            var interaction = em.System<SharedInteractionSystem>();
            transforms.SetCoordinates(seat, em.GetComponent<TransformComponent>(seat),
                new EntityCoordinates(map.Grid.Owner, new Vector2(1.5f, 0.3f)), unanchor: false);
            em.EnsureComponent<PilotComponent>(actor);
            em.System<ShuttleConsoleSystem>().AddPilot(helm, actor, em.GetComponent<ShuttleConsoleComponent>(helm));
            ui.OpenUi(helm, ShuttleConsoleUiKey.Key, actor);
            Assert.That(em.System<SharedBuckleSystem>().TryBuckle(actor, actor, seat), Is.True);
            Assert.That(Vector2.Distance(transforms.GetWorldPosition(actor), transforms.GetWorldPosition(gun)),
                Is.GreaterThan(SharedInteractionSystem.InteractionRange), "The chair placement puts the console center beyond the old cutoff.");
            Assert.That(interaction.InRangeAndAccessible(actor, gun), Is.True,
                "The diagonal console is physically reachable using the normal fixture-distance and line-of-sight rules.");
            var control = em.GetComponent<FireControlConsoleComponent>(gun);
            Assert.That(control.ConnectedServer, Is.Null, "The gunnery console has not been opened or manually linked.");
            ui.RaiseUiMessage(helm, ShuttleConsoleUiKey.Key, new WFCockpitGunnerySessionMessage(true, true) { Actor = actor });
            Assert.That(cockpit.GetConsole(actor), Is.Null, "Physical proximity must not bypass the console access reader.");
            Assert.That(control.ConnectedServer, Is.Null, "An unauthorized candidate must not trigger server discovery.");
            reader.Enabled = false;
            cockpit.Update(0.3f);
            Assert.That(cockpit.GetConsole(actor), Is.EqualTo(gun), "The active cockpit discovers a reachable unopened console when access becomes available.");
            Assert.That(control.ConnectedServer, Is.EqualTo(server));
            Assert.That(em.GetComponent<FireControlServerComponent>(server).Consoles, Does.Contain(gun));
            Assert.That(ui.IsUiOpen(gun, FireControlConsoleUiKey.Key, actor), Is.False);
            var gunPower = em.GetComponent<ApcPowerReceiverComponent>(gun);
            gunPower.Powered = false;
            cockpit.Update(0.3f);
            Assert.That(cockpit.GetConsole(actor), Is.Null);
            Assert.That(cockpit.TryCommand(actor, helm, em.GetNetEntity(gun), new WFDispenseFlaresMessage()), Is.False);
            gunPower.Powered = true;
            cockpit.Update(0.3f);
            Assert.That(cockpit.GetConsole(actor), Is.EqualTo(gun), "Gunnery power restoration recovers the existing cockpit intent without a second session request.");
            var helmPower = em.GetComponent<ApcPowerReceiverComponent>(helm);
            helmPower.Powered = false;
            var helmLost = new Content.Shared.Power.PowerChangedEvent(false, 0f);
            em.EventBus.RaiseLocalEvent(helm, ref helmLost);
            Assert.That(ui.IsUiOpen(helm, ShuttleConsoleUiKey.Key, actor), Is.False, "Real helm power loss closes the helm window.");
            cockpit.Update(0.3f);
            Assert.That(cockpit.GetConsole(actor), Is.Null, "Helm power loss ends the cockpit session.");
            Assert.That(cockpit.TryCommand(actor, helm, em.GetNetEntity(gun), new WFDispenseFlaresMessage()), Is.False);
            helmPower.Powered = true;
            var helmRestored = new Content.Shared.Power.PowerChangedEvent(true, 0f);
            em.EventBus.RaiseLocalEvent(helm, ref helmRestored);
            cockpit.Update(0.3f);
            Assert.That(cockpit.GetConsole(actor), Is.Null, "Returning power does not reopen the helm or revive the session.");
            ui.CloseUi(helm, ShuttleConsoleUiKey.Key, actor);
            // Keep the network fixture powered without an APC changing it during subsequent ticks.
            foreach (var entity in new[] { helm, gun, server })
                em.RemoveComponent<ApcPowerReceiverComponent>(entity);
            groupedWeapon = em.SpawnEntity(null, map.GridCoords);
            em.AddComponent<FireControllableComponent>(groupedWeapon).ControllingServer = server;
            em.GetComponent<FireControlServerComponent>(server).Controlled.Add(groupedWeapon);
            em.System<FireControlSystem>().WfRefreshConsole(gun);
            gunNet = em.GetNetEntity(gun);
            weaponNet = em.GetNetEntity(groupedWeapon);
            em.EnsureComponent<PilotComponent>(actor);
            em.System<ShuttleConsoleSystem>().AddPilot(helm, actor, em.GetComponent<ShuttleConsoleComponent>(helm));
            ui.OpenUi(helm, ShuttleConsoleUiKey.Key, actor);
        });
        await pair.RunTicksSync(10);
        await pair.Client.WaitAssertion(() =>
        {
            static IEnumerable<Control> Descendants(Control root)
            {
                yield return root;
                foreach (var child in root.Children)
                foreach (var nested in Descendants(child))
                    yield return nested;
            }
            clientWindow = Descendants(pair.Client.ResolveDependency<IUserInterfaceManager>().WindowRoot)
                .OfType<ShuttleConsoleWindow>().Single();
            clientWindow.WfSendCockpitGunnery(new WFCockpitGunnerySessionMessage(true));
            clientWindow.WfSendCockpitGunnery(new WFCockpitGunneryCommandMessage(gunNet,
                new WFSaveWeaponGroupMessage(3, new List<NetEntity> { weaponNet })));
        });
        await pair.RunTicksSync(10);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(em.System<WFCockpitGunnerySystem>().GetConsole(actor), Is.EqualTo(gun),
                "The real client BUI must establish a discovered link with the network sender as actor.");
            Assert.That(em.GetComponent<WFCombatConsoleComponent>(gun).Groups.ContainsKey(3), Is.False,
                "A client command sent over the network without gun control must be rejected.");
        });
        await pair.Client.WaitAssertion(() =>
            clientWindow!.WfSendCockpitGunnery(new WFCockpitGunnerySessionMessage(true, true)));
        await pair.RunTicksSync(10);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(em.System<WFCockpitGunnerySystem>().CanOperate(actor, gun), Is.True,
                "The delivered GUNS mode message must authorize the attached, seated operator before its first command.");
            Assert.That(em.GetComponent<FireControlServerComponent>(server).Controlled, Does.Contain(groupedWeapon),
                "The network fixture's weapon must remain connected while its command travels to the server.");
        });
        await pair.Client.WaitAssertion(() =>
            clientWindow!.WfSendCockpitGunnery(new WFCockpitGunneryCommandMessage(gunNet,
                new WFSaveWeaponGroupMessage(3, new List<NetEntity> { weaponNet }))));
        await pair.RunTicksSync(10);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(em.GetComponent<WFCombatConsoleComponent>(gun).Groups.TryGetValue(3, out var saved), Is.True,
                "The authorized group command must reach the server through the live client BUI.");
            Assert.That(saved, Is.EquivalentTo(new[] { groupedWeapon }),
                "A real window command must cross client networking, inherit its sender identity, and reach the authorized server handler.");
        });
        await pair.Client.WaitAssertion(() =>
            clientWindow!.WfSendCockpitGunnery(new WFCockpitGunnerySessionMessage(true)));
        await pair.RunTicksSync(10);
        await pair.Server.WaitAssertion(() =>
            Assert.That(em.System<WFCockpitGunnerySystem>().CanOperate(actor, gun), Is.False));
        await pair.Client.WaitAssertion(() =>
        {
            clientWindow!.WfSendCockpitGunnery(new WFCockpitGunnerySessionMessage(true, true));
            clientWindow.WfSendCockpitGunnery(new WFCockpitGunneryCommandMessage(gunNet,
                new WFSaveWeaponGroupMessage(2, new List<NetEntity> { weaponNet })));
        });
        await pair.RunTicksSync(10);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(em.GetComponent<WFCombatConsoleComponent>(gun).Groups.TryGetValue(2, out var saved), Is.True,
                "Switching to GUNS and saving a group within one client tick must retain message order.");
            Assert.That(saved, Is.EquivalentTo(new[] { groupedWeapon }));
            em.System<SharedUserInterfaceSystem>().CloseUi(helm, ShuttleConsoleUiKey.Key, actor);
            pair.Server.PlayerMan.SetAttachedEntity(pair.Player!, null);
            foreach (var entity in new[] { actor, seat, helm, gun, server, groupedWeapon })
                em.DeleteEntity(entity);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SeatedPilotUsesOnlyTheReachableAuthorizedConsole()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var map = await pair.CreateTestMap();
        await pair.Client.WaitPost(() => pair.Client.CfgMan.SetCVar(WolfgateCVars.CockpitAutoEnter, false));
        var em = pair.Server.EntMan;
        var dummy = await pair.Server.AddDummySession();
        // The ticker joins the dummy once its database lookups land, in real time. Joining it by hand first
        // leaves its play time data half loaded, so wait on the state instead of a tick count.
        var playTimes = pair.Server.ResolveDependency<PlayTimeTrackingManager>();
        var joined = false;
        var timeout = Stopwatch.StartNew();
        while (!joined && timeout.Elapsed < TimeSpan.FromSeconds(30))
        {
            await pair.Server.WaitPost(() => joined = dummy.Status == SessionStatus.InGame && playTimes.TryGetTrackerTimes(dummy, out _));
            if (!joined)
            {
                await pair.RunTicksSync(1);
                await Task.Delay(10);
            }
        }

        Assert.That(joined, Is.True, "The dummy session should finish joining on its own.");
        await pair.Server.WaitAssertion(() =>
        {
            var maps = em.System<SharedMapSystem>();
            maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(1, 0), map.Tile.Tile);
            maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(2, 0), map.Tile.Tile);
            maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(3, 0), map.Tile.Tile);
            var actor = em.SpawnEntity("MobHuman", map.GridCoords);
            pair.Server.PlayerMan.SetAttachedEntity(pair.Player!, actor);
            var helm = em.SpawnEntity("ComputerShuttle", map.GridCoords);
            var seat = em.SpawnEntity("Chair", map.GridCoords);
            var gun = em.SpawnEntity("ComputerGunneryConsole", map.GridCoords);
            var serverUid = em.SpawnEntity(null, map.GridCoords);
            var server = em.AddComponent<FireControlServerComponent>(serverUid);
            server.ConnectedGrid = map.Grid.Owner;
            server.ProcessingPower = 100;
            em.EnsureComponent<FireControlGridComponent>(map.Grid.Owner).ControllingServer = serverUid;
            server.Consoles.Add(gun);
            var control = em.GetComponent<FireControlConsoleComponent>(gun);
            control.ConnectedServer = serverUid;
            var gunPower = em.GetComponent<ApcPowerReceiverComponent>(gun);
            var helmPower = em.GetComponent<ApcPowerReceiverComponent>(helm);
            gunPower.Powered = true;
            helmPower.Powered = true;
            em.RemoveComponent<AccessReaderComponent>(gun);
            em.RemoveComponent<AccessReaderComponent>(helm);
            em.EnsureComponent<CrewedShuttleComponent>(map.Grid.Owner);
            var helms = em.System<ShuttleConsoleSystem>();
            var cockpit = em.System<WFCockpitGunnerySystem>();
            var ui = em.System<SharedUserInterfaceSystem>();
            var transforms = em.System<SharedTransformSystem>();
            var buckles = em.System<SharedBuckleSystem>();
            em.EnsureComponent<PilotComponent>(actor);
            helms.AddPilot(helm, actor, em.GetComponent<ShuttleConsoleComponent>(helm));
            Assert.That(cockpit.SetSession(actor, helm, true, true), Is.False, "A live helm UI and seat are both required.");
            ui.OpenUi(helm, ShuttleConsoleUiKey.Key, actor);
            Assert.That(cockpit.SetSession(actor, helm, true, true), Is.False, "Standing pilots cannot acquire gunnery.");
            Assert.That(buckles.TryBuckle(actor, actor, seat), Is.True);
            var normalOpen = new ActivatableUIOpenAttemptEvent(actor);
            em.EventBus.RaiseLocalEvent(gun, normalOpen);
            Assert.That(normalOpen.Cancelled, Is.True, "The ordinary crewed-ship two-window restriction remains intact.");
            var gunAccess = em.AddComponent<AccessReaderComponent>(gun);
            var helmAccess = em.AddComponent<AccessReaderComponent>(helm);
            ui.RaiseUiMessage(helm, ShuttleConsoleUiKey.Key, new WFCockpitGunnerySessionMessage(true, true) { Actor = actor });
            Assert.That(cockpit.GetConsole(actor), Is.EqualTo(gun));
            Assert.That(ui.IsUiOpen(gun, FireControlConsoleUiKey.Key, actor), Is.False, "The cockpit must not open a second gunnery BUI.");
            Assert.That(cockpit.GetActors(gun), Does.Contain(actor), "Existing crew handoff must see the cockpit operator.");
            ui.RaiseUiMessage(helm, ShuttleConsoleUiKey.Key, new WFCockpitGunnerySessionMessage(true) { Actor = actor });
            Assert.That(cockpit.GetConsole(actor), Is.EqualTo(gun), "A non-controlling session retains discovery and telemetry.");
            Assert.That(cockpit.GetActors(gun), Is.Empty, "A non-controlling session must leave the gunner's console unclaimed.");
            Assert.That(cockpit.TryCommand(actor, helm, em.GetNetEntity(gun), new WFDispenseFlaresMessage()), Is.False,
                "A forged weapon action without gun control cannot operate guns or countermeasures.");
            ui.RaiseUiMessage(helm, ShuttleConsoleUiKey.Key, new WFCockpitGunnerySessionMessage(true, true) { Actor = actor });
            Assert.That(cockpit.GetActors(gun), Does.Contain(actor));
            var sharedState = em.System<FireControlSystem>().WfCockpitState(gun);
            for (var repeat = 0; repeat < 10; repeat++)
                Assert.That(em.System<FireControlSystem>().WfCockpitState(gun), Is.SameAs(sharedState),
                    "Linked cockpit viewers share the current periodic snapshot.");
            var snapshotSettings = em.GetComponent<WFCombatConsoleComponent>(gun);
            snapshotSettings.NextTelemetry = TimeSpan.Zero;
            Assert.That(em.System<FireControlSystem>().WfCockpitState(gun), Is.SameAs(sharedState),
                "An unchanged lightweight refresh must preserve the published snapshot.");
            snapshotSettings.NextTelemetry = snapshotSettings.NextRadarTelemetry = TimeSpan.Zero;
            Assert.That(em.System<FireControlSystem>().WfCockpitState(gun), Is.SameAs(sharedState),
                "Even a full radar refresh must not republish identical state.");

            Assert.That(gunAccess.AccessLog.Count, Is.EqualTo(1), "Acquiring the gunnery link records one successful access.");
            Assert.That(helmAccess.AccessLog.Count, Is.EqualTo(1));
            for (var refresh = 0; refresh < 25; refresh++)
            {
                Assert.That(cockpit.CanOperate(actor, gun), Is.True);
                cockpit.Update(0.3f);
            }
            Assert.That(gunAccess.AccessLog.Count, Is.EqualTo(1), "Continuous authorization must not overwrite the access history.");
            Assert.That(helmAccess.AccessLog.Count, Is.EqualTo(1));

            var weapon = em.SpawnEntity("WeaponTurretL85Autocannon", new EntityCoordinates(map.Grid.Owner, new Vector2(3.5f, 0.5f)));
            var slots = em.System<ItemSlotsSystem>();
            Assert.That(slots.TryEject(weapon, "gun_magazine", null, out var infiniteMagazine), Is.True);
            em.DeleteEntity(infiniteMagazine!.Value);
            var finiteMagazine = em.SpawnEntity("Magazine20mmAS", map.GridCoords);
            Assert.That(slots.TryInsert(weapon, "gun_magazine", finiteMagazine, null), Is.True,
                "Use the compatible finite loader so a real shot measurably consumes ammunition.");
            var fireControl = em.GetComponent<FireControllableComponent>(weapon);
            fireControl.ControllingServer = serverUid;
            server.Controlled.Add(weapon);
            var foreign = em.SpawnEntity(null, map.GridCoords);
            var foreignControl = em.AddComponent<FireControllableComponent>(foreign);
            var flare = em.SpawnEntity("WeaponTurretFlare", new EntityCoordinates(map.Grid.Owner, new Vector2(2.5f, 0.5f)));
            var flareControl = em.GetComponent<FireControllableComponent>(flare);
            flareControl.ControllingServer = serverUid;
            // Sight is not what keeps it from firing, so only the tunnel's launcher filter can.
            flareControl.IgnoreLos = true;
            server.Controlled.Add(flare);
            // Owned by this server and able to fire, but mounted on a second grid.
            var otherGrid = pair.Server.ResolveDependency<IMapManager>().CreateGridEntity(map.MapId);
            transforms.SetLocalPosition(otherGrid.Owner, new Vector2(40, 0));
            var elsewhere = em.SpawnEntity(null, new EntityCoordinates(otherGrid.Owner, new Vector2(0.5f, 0.5f)));
            var elsewhereControl = em.AddComponent<FireControllableComponent>(elsewhere);
            elsewhereControl.ControllingServer = serverUid;
            elsewhereControl.IgnoreLos = true;
            server.Controlled.Add(elsewhere);
            Assert.Multiple(() =>
            {
                Assert.That(em.System<SharedWFCockpitSystem>().CanEnter(actor, helm), Is.True, "Weapon setup must preserve the seated pilot.");
                Assert.That(ui.IsUiOpen(helm, ShuttleConsoleUiKey.Key, actor), Is.True, "Weapon setup must preserve the helm subscription.");
                Assert.That(gunPower.Powered && helmPower.Powered, Is.True, "Weapon setup must preserve bridge power.");
                foreach (var entity in new[] { actor, helm, seat, gun, serverUid, weapon, flare })
                    Assert.That(em.GetComponent<TransformComponent>(entity).GridUid, Is.EqualTo(map.Grid.Owner), $"Fixture {entity} must remain on the connected deck.");
                Assert.That(em.GetComponent<TransformComponent>(gun).Anchored, Is.True);
                Assert.That(em.GetComponent<TransformComponent>(helm).Anchored, Is.True);
                Assert.That(em.GetComponent<TransformComponent>(weapon).Anchored, Is.True);
                Assert.That(em.System<WFCombatConsoleSystem>().TryGetServer(gun, control, out _, out _), Is.True,
                    "Weapon setup must preserve native gunnery server ownership.");
                Assert.That(cockpit.CanOperate(actor, gun), Is.True, "The original cockpit link must remain authorized after mounting weapons.");
            });
            var netGun = em.GetNetEntity(gun);
            var weaponNet = em.GetNetEntity(weapon);
            var selection = new List<NetEntity> { weaponNet, em.GetNetEntity(foreign), em.GetNetEntity(flare), em.GetNetEntity(elsewhere), weaponNet };
            ui.RaiseUiMessage(helm, ShuttleConsoleUiKey.Key,
                new WFCockpitGunneryCommandMessage(netGun, new WFSaveWeaponGroupMessage(0, selection)) { Actor = actor });
            Assert.That(em.GetComponent<WFCombatConsoleComponent>(gun).Groups[0], Is.EquivalentTo(new[] { weapon }),
                "Cockpit group saves use the native filter for disconnected weapons, flares, other grids and duplicates.");
            snapshotSettings.NextTelemetry = snapshotSettings.NextRadarTelemetry = TimeSpan.Zero;
            var afterGroup = em.System<FireControlSystem>().WfCockpitState(gun);
            Assert.That(afterGroup, Is.Not.SameAs(sharedState), "A saved group must publish a new snapshot.");
            Assert.That(afterGroup!.Combat.Groups[0], Is.EquivalentTo(new[] { weaponNet }),
                "The republished snapshot must carry the saved group.");
            var ammoBefore = afterGroup.FireControllables.Single(entry => entry.NetEntity == weaponNet).AmmoCount;
            Assert.That(ammoBefore, Is.Not.Null);
            Assert.That(cockpit.TryCommand(actor, helm, netGun, new WFAutomaticFlaresMessage(true)), Is.True);
            Assert.That(em.GetComponent<WFCombatConsoleComponent>(gun).Automatic, Is.True);
            var target = em.GetNetCoordinates(new EntityCoordinates(map.Grid.Owner, new Vector2(20, 0)));
            var beforeShot = new GetAmmoCountEvent();
            em.EventBus.RaiseLocalEvent(weapon, ref beforeShot);
            var flareBefore = new GetAmmoCountEvent();
            em.EventBus.RaiseLocalEvent(flare, ref flareBefore);
            Assert.That(beforeShot.Count, Is.GreaterThan(0), "The real ship autocannon has a finite, loaded magazine.");
            var mountedGun = em.GetComponent<GunComponent>(weapon);
            Assert.That(em.System<GunSystem>().CanShoot(mountedGun), Is.True, "The autocannon must be ready before its first command.");
            Assert.That(mountedGun.FireRateModified, Is.GreaterThan(0));
            var beforeHover = em.System<FireControlSystem>().WfCockpitState(gun, false);
            Assert.That(cockpit.TryCommand(actor, helm, netGun, new FireControlConsoleFireMessage(new(), target)), Is.True);
            Assert.That(em.System<FireControlSystem>().WfCockpitState(gun, false), Is.SameAs(beforeHover),
                "Cursor guidance must not rebuild radar/ammunition state.");
            ui.RaiseUiMessage(helm, ShuttleConsoleUiKey.Key,
                new WFCockpitGunneryCommandMessage(netGun, new FireControlConsoleFireMessage(selection, target)) { Actor = actor });
            Assert.That(fireControl.NextFire, Is.GreaterThan(TimeSpan.Zero), "Firing reaches the original weapon cooldown path.");
            Assert.That(em.GetComponent<AutoShootGunComponent>(weapon).RemainingTime, Is.GreaterThan(TimeSpan.Zero),
                "The native firing handler schedules the autocannon burst.");
            em.System<GunSystem>().Update(1f / 30f);
            var afterShot = new GetAmmoCountEvent();
            em.EventBus.RaiseLocalEvent(weapon, ref afterShot);
            Assert.That(afterShot.Count, Is.LessThan(beforeShot.Count), "The original firing path must actually consume ammunition.");
            Assert.That(foreignControl.NextFire, Is.EqualTo(TimeSpan.Zero), "A forged weapon outside the connected server must never fire.");
            Assert.That(elsewhereControl.NextFire, Is.EqualTo(TimeSpan.Zero),
                "A weapon this server controls but that sits on another grid must never fire through the tunnel.");
            Assert.That(flareControl.NextFire, Is.EqualTo(TimeSpan.Zero), "The tunnel must not aim a countermeasure launcher.");
            var flareAfterFire = new GetAmmoCountEvent();
            em.EventBus.RaiseLocalEvent(flare, ref flareAfterFire);
            Assert.That(flareAfterFire.Count, Is.EqualTo(flareBefore.Count), "A fire command must not spend countermeasure ammunition.");
            Assert.That(control.NextLog, Is.Not.Null, "A tunnelled shot must reach the native firing-log path.");
            snapshotSettings.NextTelemetry = snapshotSettings.NextRadarTelemetry = TimeSpan.Zero;
            var afterShotState = em.System<FireControlSystem>().WfCockpitState(gun);
            Assert.That(afterShotState, Is.Not.SameAs(afterGroup), "A real shot must publish a new snapshot.");
            Assert.That(afterShotState!.FireControllables.Single(entry => entry.NetEntity == weaponNet).AmmoCount, Is.LessThan(ammoBefore),
                "The republished snapshot must carry the lower ammunition count.");
            Assert.That(cockpit.TryCommand(actor, helm, netGun, new WFDispenseFlaresMessage()), Is.True);
            // Fire control schedules bursts; the native gun update performs the actual shot.
            em.System<GunSystem>().Update(1f / 30f);
            Assert.That(em.GetComponent<WFFlareLauncherComponent>(flare).NextBurst, Is.GreaterThan(TimeSpan.Zero));
            Assert.That(flareControl.NextFire, Is.GreaterThan(TimeSpan.Zero), "DISPENSE is the path that fires the launcher.");
            Assert.That(cockpit.TryCommand(actor, helm, netGun, new WFCockpitGunnerySessionMessage(false)), Is.False,
                "The tunnel only accepts its explicit native command allowlist.");
            Assert.That(cockpit.TryCommand(actor, helm, netGun,
                new FireControlConsoleFireMessage(new(), new NetCoordinates(target.NetEntity, float.NaN, 0))), Is.False);
            Assert.That(cockpit.TryCommand(actor, helm, netGun,
                new FireControlConsoleFireMessage(new(), new NetCoordinates(target.NetEntity, 0, float.PositiveInfinity))), Is.False);
            Assert.That(cockpit.TryCommand(actor, helm, netGun, new FireControlConsoleFireMessage(
                Enumerable.Repeat(weaponNet, WFWeaponGroups.MaximumWeapons + 1).ToList(), target)), Is.False,
                "An oversized selection is rejected before any entity is resolved.");
            var otherMap = maps.CreateMap(out _);
            Assert.That(cockpit.TryCommand(actor, helm, netGun, new FireControlConsoleFireMessage(
                new List<NetEntity> { weaponNet }, em.GetNetCoordinates(new EntityCoordinates(otherMap, Vector2.Zero)))), Is.False,
                "A target on another map is rejected.");
            em.DeleteEntity(otherMap);

            Assert.That(gunAccess.AccessLog.Count, Is.EqualTo(1), "Firing and other commands must not flood the access history either.");
            Assert.That(helmAccess.AccessLog.Count, Is.EqualTo(1));

            void AssertDenied(string reason) => Assert.That(cockpit.TryCommand(actor, helm, netGun,
                new WFSaveWeaponGroupMessage(1, new() { em.GetNetEntity(weapon) })), Is.False, reason);
            gunPower.Powered = false;
            AssertDenied("Loss of gunnery power is enforced before the next telemetry tick.");
            gunPower.Powered = true;
            helmPower.Powered = false;
            AssertDenied("Loss of helm power must also revoke commands immediately.");
            helmPower.Powered = true;
            gunAccess.AccessLists.Add(new HashSet<ProtoId<AccessLevelPrototype>> { "Captain" });
            AssertDenied("Revoked gunnery access is enforced on every command.");
            gunAccess.Enabled = false;
            helmAccess.AccessLists.Add(new HashSet<ProtoId<AccessLevelPrototype>> { "Captain" });
            AssertDenied("Revoked helm access is enforced on every command.");
            helmAccess.Enabled = false;
            var gunTransform = em.GetComponent<TransformComponent>(gun);
            var gunPosition = gunTransform.Coordinates;
            transforms.SetCoordinates(gun, gunTransform, new EntityCoordinates(map.Grid.Owner, new Vector2(10, 0)), unanchor: false);
            AssertDenied("An anchored console outside physical reach is not a remote gunnery link.");
            transforms.SetCoordinates(gun, gunTransform, gunPosition, unanchor: false);
            var helmTransform = em.GetComponent<TransformComponent>(helm);
            var helmPosition = helmTransform.Coordinates;
            transforms.SetCoordinates(helm, helmTransform, new EntityCoordinates(map.Grid.Owner, new Vector2(10, 0)), unanchor: false);
            AssertDenied("Moving the helm out of physical reach also revokes the link immediately.");
            transforms.SetCoordinates(helm, helmTransform, helmPosition, unanchor: false);
            server.Consoles.Remove(gun);
            AssertDenied("The claimed gunnery server must still own this console.");
            server.Consoles.Add(gun);
            Assert.That(cockpit.CanOperate(actor, gun), Is.True);

            var otherActor = em.SpawnEntity("MobHuman", map.GridCoords);
            var otherSeat = em.SpawnEntity("ChairPilotSeat", map.GridCoords);
            em.EnsureComponent<PilotComponent>(otherActor);
            helms.AddPilot(helm, otherActor, em.GetComponent<ShuttleConsoleComponent>(helm));
            ui.OpenUi(helm, ShuttleConsoleUiKey.Key, otherActor);
            Assert.That(buckles.TryBuckle(otherActor, otherActor, otherSeat), Is.True);
            em.GetComponent<ActivatableUIComponent>(gun).SingleUser = true;
            Assert.That(cockpit.SetSession(otherActor, helm, true, true), Is.False,
                "A seated body without an attached connected player cannot retain gunnery control.");
            var activation = em.GetComponent<ActivatableUIComponent>(gun);
            activation.CurrentSingleUser = otherActor;
            AssertDenied("The native single-user console occupant must block cockpit commands.");
            activation.CurrentSingleUser = null;
            Assert.That(cockpit.CanOperate(actor, gun), Is.True);
            pair.Server.PlayerMan.SetAttachedEntity(dummy, otherActor);
            Assert.That(cockpit.SetSession(actor, helm, true), Is.True);
            Assert.That(cockpit.SetSession(otherActor, helm, true, true), Is.True);
            Assert.That(cockpit.GetConsole(otherActor), Is.EqualTo(gun), "A non-controlling session must not block a second operator.");
            Assert.That(cockpit.SetSession(actor, helm, true, true), Is.True);
            Assert.That(cockpit.GetConsole(actor), Is.Null, "A single-user console cannot be shared through two controlling cockpit sessions.");
            Assert.That(cockpit.GetActors(gun), Is.EquivalentTo(new[] { otherActor }));
            Assert.That(cockpit.SetSession(otherActor, helm, false), Is.True);
            cockpit.Update(0.3f);
            Assert.That(cockpit.GetConsole(actor), Is.EqualTo(gun), "Releasing the console lets the waiting operator acquire it.");
            pair.Server.PlayerMan.SetAttachedEntity(dummy, null);
            pair.Server.PlayerMan.SetAttachedEntity(pair.Player!, otherActor);
            Assert.That(cockpit.GetConsole(actor), Is.Null, "Leaving the body immediately releases the cockpit link.");
            Assert.That(cockpit.GetActors(gun), Is.Empty, "Ghosting or changing bodies must release the NPC gunner claim.");
            pair.Server.PlayerMan.SetAttachedEntity(pair.Player!, actor);
            Assert.That(cockpit.GetConsole(actor), Is.Null, "Returning to the body must not reactivate a stale session.");
            ui.CloseUi(helm, ShuttleConsoleUiKey.Key, otherActor);
            ui.OpenUi(helm, ShuttleConsoleUiKey.Key, actor);
            Assert.That(cockpit.SetSession(actor, helm, true, true), Is.True);

            var replacement = em.SpawnEntity("ComputerGunneryConsole", map.GridCoords);
            em.GetComponent<ApcPowerReceiverComponent>(replacement).Powered = true;
            em.RemoveComponent<AccessReaderComponent>(replacement);
            em.GetComponent<FireControlConsoleComponent>(replacement).ConnectedServer = serverUid;
            server.Consoles.Add(replacement);
            cockpit.Update(0.3f);
            Assert.That(cockpit.GetConsole(actor), Is.EqualTo(gun), "A valid link remains stable when another console appears.");
            gunPower.Powered = false;
            cockpit.Update(0.3f);
            Assert.That(cockpit.GetConsole(actor), Is.EqualTo(replacement));
            AssertDenied("A queued command addressed to the old console cannot operate its replacement.");
            // The fixture runs in one tick; simulate seat loss without the normal unbuckling cooldown.
            buckles.Unbuckle((actor, em.GetComponent<BuckleComponent>(actor)), actor);
            Assert.That(em.GetComponent<BuckleComponent>(actor).BuckledTo, Is.Null);
            Assert.That(cockpit.TryCommand(actor, helm, em.GetNetEntity(replacement), new WFDispenseFlaresMessage()), Is.False);
            cockpit.Update(0.3f);
            Assert.That(cockpit.GetConsole(actor), Is.Null);
            Assert.That(cockpit.GetActors(replacement), Is.Empty, "Losing the seat releases crew and telemetry occupancy.");
            ui.CloseUi(helm, ShuttleConsoleUiKey.Key, actor);
            pair.Server.PlayerMan.SetAttachedEntity(pair.Player!, null);
            foreach (var entity in new[] { actor, otherActor, helm, seat, otherSeat, gun, replacement, flare, weapon, foreign, serverUid })
                em.DeleteEntity(entity);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task GunneryConsoleCommandsNeedAnOpenReachableOperator()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var map = await pair.CreateTestMap();
        var em = pair.Server.EntMan;
        await pair.Server.WaitAssertion(() =>
        {
            var maps = em.System<SharedMapSystem>();
            maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(1, 0), map.Tile.Tile);
            var actor = em.SpawnEntity("MobHuman", map.GridCoords);
            var gun = em.SpawnEntity("ComputerGunneryConsole", map.GridCoords);
            var serverUid = em.SpawnEntity(null, map.GridCoords);
            var server = em.AddComponent<FireControlServerComponent>(serverUid);
            server.ConnectedGrid = map.Grid.Owner;
            server.ProcessingPower = 100;
            em.EnsureComponent<FireControlGridComponent>(map.Grid.Owner).ControllingServer = serverUid;
            server.Consoles.Add(gun);
            em.GetComponent<FireControlConsoleComponent>(gun).ConnectedServer = serverUid;
            em.GetComponent<ApcPowerReceiverComponent>(gun).Powered = true;
            var weapon = em.SpawnEntity(null, map.GridCoords);
            em.AddComponent<FireControllableComponent>(weapon).ControllingServer = serverUid;
            server.Controlled.Add(weapon);
            var flare = em.SpawnEntity("WeaponTurretFlare", new EntityCoordinates(map.Grid.Owner, new Vector2(1.5f, 0.5f)));
            var flareControl = em.GetComponent<FireControllableComponent>(flare);
            flareControl.ControllingServer = serverUid;
            server.Controlled.Add(flare);

            var key = FireControlConsoleUiKey.Key;
            var ui = em.System<SharedUserInterfaceSystem>();
            var transforms = em.System<SharedTransformSystem>();
            var selection = new List<NetEntity> { em.GetNetEntity(weapon), em.GetNetEntity(flare) };
            var settings = em.EnsureComponent<WFCombatConsoleComponent>(gun);

            void Send(EntityUid sender)
            {
                ui.RaiseUiMessage(gun, key, new WFSaveWeaponGroupMessage(1, selection) { Actor = sender });
                ui.RaiseUiMessage(gun, key, new WFAutomaticFlaresMessage(true) { Actor = sender });
                ui.RaiseUiMessage(gun, key, new WFDispenseFlaresMessage { Actor = sender });
            }

            void AssertIgnored(string reason)
            {
                Assert.That(settings.Groups.ContainsKey(1), Is.False, reason);
                Assert.That(settings.Automatic, Is.False, reason);
                Assert.That(flareControl.NextFire, Is.EqualTo(TimeSpan.Zero), reason);
            }

            ui.OpenUi(gun, key, actor);
            Send(actor);
            Assert.That(settings.Groups[1], Is.EquivalentTo(new[] { weapon }),
                "An operator at the open console saves groups through the console's own subscription, minus flare launchers.");
            Assert.That(settings.Automatic, Is.True, "AUTO is armed through the console's own subscription.");
            Assert.That(flareControl.NextFire, Is.GreaterThan(TimeSpan.Zero), "DISPENSE fires the launcher through the console's own subscription.");
            settings.Groups.Clear();
            settings.Automatic = false;
            flareControl.NextFire = TimeSpan.Zero;

            var home = em.GetComponent<TransformComponent>(actor).Coordinates;
            transforms.SetCoordinates(actor, new EntityCoordinates(map.MapUid, new Vector2(100, 100)));
            Send(actor);
            AssertIgnored("An open window does not let a distant operator act on the console.");
            transforms.SetCoordinates(actor, home);

            var downed = em.SpawnEntity("MobHuman", map.GridCoords);
            ui.OpenUi(gun, key, downed);
            em.System<Content.Shared.Mobs.Systems.MobStateSystem>().ChangeMobState(downed, Content.Shared.Mobs.MobState.Dead);
            Send(downed);
            AssertIgnored("An operator who can no longer interact must not act on the console.");

            ui.CloseUi(gun, key, actor);
            Send(actor);
            AssertIgnored("Messages from an operator without the window open are ignored.");

            foreach (var entity in new[] { actor, downed, gun, serverUid, weapon, flare })
                em.DeleteEntity(entity);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AutomaticFlaresNeedArmingAndMeetFastMissilesEarly()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var map = await pair.CreateTestMap();
        var em = pair.Server.EntMan;
        EntityUid consoleUid = default, serverUid = default, flareUid = default, missileUid = default;
        await pair.Server.WaitAssertion(() =>
        {
            consoleUid = em.SpawnEntity(null, map.GridCoords);
            serverUid = em.SpawnEntity(null, map.GridCoords);
            em.System<SharedTransformSystem>().AnchorEntity(consoleUid);
            var console = em.AddComponent<FireControlConsoleComponent>(consoleUid);
            var server = em.AddComponent<FireControlServerComponent>(serverUid);
            server.ConnectedGrid = map.Grid.Owner;
            server.ProcessingPower = 100;
            em.EnsureComponent<FireControlGridComponent>(map.Grid.Owner).ControllingServer = serverUid;
            server.Consoles.Add(consoleUid);
            console.ConnectedServer = serverUid;
            em.AddComponent<WFCombatConsoleComponent>(consoleUid).Automatic = false;
            flareUid = em.SpawnEntity("WeaponTurretFlare", map.GridCoords);
            em.GetComponent<FireControllableComponent>(flareUid).ControllingServer = serverUid;
            server.Controlled.Add(flareUid);
            missileUid = em.SpawnEntity(null, new EntityCoordinates(map.MapUid, new Vector2(10, 0)));
            em.AddComponent<ProjectileComponent>(missileUid);
            var seeker = em.AddComponent<TargetSeekingComponent>(missileUid);
            seeker.CurrentTarget = map.Grid.Owner;
            seeker.DetectionRange = 0; // Isolate alert filtering from countermeasure reacquisition.
            seeker.Launched = true;
        });
        await pair.RunTicksSync(20);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(em.GetComponent<WFFlareLauncherComponent>(flareUid).NextBurst, Is.EqualTo(TimeSpan.Zero),
                "A disarmed console must never dispense, however close the lock.");
            Assert.That(em.GetComponent<FireControllableComponent>(flareUid).NextFire, Is.EqualTo(TimeSpan.Zero));
            em.GetComponent<WFCombatConsoleComponent>(consoleUid).Automatic = true;
            em.System<SharedTransformSystem>().SetCoordinates(missileUid, new EntityCoordinates(map.MapUid, new Vector2(600, 0)));
        });
        await pair.RunTicksSync(20);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(em.GetComponent<WFCombatConsoleComponent>(consoleUid).Threats, Is.Zero,
                "A slow missile 600 m out is not yet a threat.");
            Assert.That(em.GetComponent<WFFlareLauncherComponent>(flareUid).NextBurst, Is.EqualTo(TimeSpan.Zero));
            em.GetComponent<TargetSeekingComponent>(missileUid).MaxSpeed = 300;
        });
        await pair.RunTicksSync(20);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(em.GetComponent<WFCombatConsoleComponent>(consoleUid).Threats, Is.EqualTo(1),
                "A fast missile counts as a threat while it is still seconds away.");
            Assert.That(em.GetComponent<WFFlareLauncherComponent>(flareUid).NextBurst, Is.GreaterThan(TimeSpan.Zero),
                "Automatic defense must fire early enough for the flares to matter.");
            foreach (var entity in new[] { missileUid, flareUid, consoleUid, serverUid })
                em.DeleteEntity(entity);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public void SnapshotEqualityNoticesEveryPublishedField()
    {
        foreach (var (type, count) in new[]
        {
            (typeof(FireControlConsoleBoundInterfaceState), 4), (typeof(FireControllableEntry), 6),
            (typeof(WFCombatConsoleState), 9), (typeof(NavInterfaceState), 8), (typeof(DockingPortState), 10),
        })
        {
            Assert.That(type.GetFields(BindingFlags.Public | BindingFlags.Instance), Has.Length.EqualTo(count),
                $"{type.Name} changed shape: teach WFCombatSnapshotEquality about the new field and update this count.");
        }

        static DockingPortState Dock(FireControlConsoleBoundInterfaceState state) => state.NavState.Docks.Values.Single()[0];

        static FireControlConsoleBoundInterfaceState Snapshot()
        {
            var entity = new NetEntity(5);
            var coordinates = new NetCoordinates(entity, 1, 2);
            var dock = new DockingPortState
            {
                Name = "dock", Coordinates = coordinates, Angle = Angle.Zero, Entity = entity, GridDockedWith = null,
                LabelName = "label", RadarColor = Color.Red, HighlightedRadarColor = Color.Blue, ReceiveOnly = false,
                DockType = DockType.Airlock,
            };
            var docks = new Dictionary<NetEntity, List<DockingPortState>> { [entity] = new List<DockingPortState> { dock } };
            var nav = new NavInterfaceState(100f, coordinates, Angle.Zero, docks, InertiaDampeningMode.Dampen,
                new Dictionary<string, string> { ["port"] = "name" }) { MaxIffRange = 5f, HideCoords = false };
            var state = new FireControlConsoleBoundInterfaceState(true,
                new[] { new FireControllableEntry(entity, coordinates, "gun", 10, true, false) }, nav);
            state.Combat.Groups[0].Add(entity);
            state.Combat.WeaponTypes[entity] = ShipGunType.Ballistic;
            state.Combat.WeaponSupplies[entity] = new WFWeaponSupply(WFWeaponSupplyKind.Finite, 3, 5);
            state.Combat.FlareLaunchers.Add(entity);
            return state;
        }

        var mutations = new (string Name, Action<FireControlConsoleBoundInterfaceState> Change)[]
        {
            ("connected", state => state.Connected = false),
            ("entry entity", state => state.FireControllables[0].NetEntity = new NetEntity(6)),
            ("entry coordinates", state => state.FireControllables[0].Coordinates = new NetCoordinates(new NetEntity(5), 3, 4)),
            ("entry name", state => state.FireControllables[0].Name = "other"),
            ("entry ammunition", state => state.FireControllables[0].AmmoCount = 9),
            ("entry manual reload", state => state.FireControllables[0].HasManualReload = false),
            ("entry line of sight", state => state.FireControllables[0].IgnoresLos = true),
            ("entry count", state => state.FireControllables = Array.Empty<FireControllableEntry>()),
            ("nav range", state => state.NavState.MaxRange = 1f),
            ("nav coordinates", state => state.NavState.Coordinates = null),
            ("nav angle", state => state.NavState.Angle = Angle.FromDegrees(30)),
            ("nav dampening", state => state.NavState.DampeningMode = InertiaDampeningMode.Anchor),
            ("nav IFF range", state => state.NavState.MaxIffRange = 6f),
            ("nav hidden coordinates", state => state.NavState.HideCoords = true),
            ("nav port names", state => state.NavState.NetworkPortNames["port"] = "changed"),
            ("nav docks", state => state.NavState.Docks.Clear()),
            ("dock name", state => Dock(state).Name = "other"),
            ("dock coordinates", state => Dock(state).Coordinates = new NetCoordinates(new NetEntity(5), 3, 4)),
            ("dock angle", state => Dock(state).Angle = Angle.FromDegrees(90)),
            ("dock entity", state => Dock(state).Entity = new NetEntity(6)),
            ("dock partner", state => Dock(state).GridDockedWith = new NetEntity(9)),
            ("dock label", state => Dock(state).LabelName = "other"),
            ("dock colour", state => Dock(state).RadarColor = Color.Green),
            ("dock highlight colour", state => Dock(state).HighlightedRadarColor = Color.Green),
            ("dock receive only", state => Dock(state).ReceiveOnly = true),
            ("dock type", state => Dock(state).DockType = DockType.Gas),
            ("combat automatic", state => state.Combat.Automatic = true),
            ("combat ammunition", state => state.Combat.Ammunition = 1),
            ("combat unlimited supply", state => state.Combat.UnlimitedSupply = true),
            ("combat threats", state => state.Combat.Threats = 2),
            ("combat cooldown", state => state.Combat.Cooldown = 3f),
            ("combat group members", state => state.Combat.Groups[0].Clear()),
            ("combat other group", state => state.Combat.Groups[1].Add(new NetEntity(5))),
            ("combat weapon type", state => state.Combat.WeaponTypes[new NetEntity(5)] = ShipGunType.Energy),
            ("combat weapon supply", state => state.Combat.WeaponSupplies[new NetEntity(5)] = new WFWeaponSupply(WFWeaponSupplyKind.Finite, 2, 5)),
            ("combat flare launchers", state => state.Combat.FlareLaunchers.Clear()),
        };

        Assert.That(WFCombatSnapshotEquality.Same(Snapshot(), Snapshot()), Is.True, "Identical snapshots must not be republished.");
        foreach (var (name, change) in mutations)
        {
            var changed = Snapshot();
            change(changed);
            Assert.That(WFCombatSnapshotEquality.Same(Snapshot(), changed), Is.False, $"A change to {name} must be republished.");
        }
    }
}
