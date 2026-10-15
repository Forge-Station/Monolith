#pragma warning disable RA0002
using Content.Server._Forge.KIAS;
using Content.Server.Power.Components;
using Content.Shared._Forge.KIAS;
using Content.Shared.Power;
using Content.Shared.NodeContainer;
using Content.Shared.Interaction;
using Content.Shared.Mind;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Robust.Server.Player;
using System.Linq;
using Content.Shared.Anomaly;
using Content.Shared.Anomaly.Components;
using Robust.Shared.Containers;
using Content.Server.Shuttles.Events;
using System.Numerics;
using Content.Shared._NF.Shipyard.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._Forge.KIAS;

[TestFixture]
public sealed class KiasGridTests
{
    [Test]
    public async Task CableCutReconnectDuplicateCoreAndShutdown()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var server = pair.Server;
        var em = server.ResolveDependency<IEntityManager>();
        var maps = em.System<SharedMapSystem>();
        var kias = em.System<KiasSystem>();
        var crew = em.System<KiasCrewSystem>();
        EntityUid core = default, device = default, scanner = default;
        EntityUid SpawnPowered(string prototype, int x, int y)
        {
            var uid = em.SpawnEntity(prototype, new EntityCoordinates(map.Grid, x + 0.5f, y + 0.5f));
            em.RemoveComponent<ApcPowerReceiverComponent>(uid);
            return uid;
        }
        await server.WaitAssertion(() =>
        {
            for (var x = 0; x < 13; x++)
            for (var y = 0; y < 4; y++)
                maps.SetTile(map.Grid, map.Grid.Comp, new Vector2i(x, y), map.Tile.Tile);
            core = SpawnPowered("KiasCore", 0, 0);
            device = SpawnPowered("KiasCrewServer", 10, 2);
            var outside = SpawnPowered("KiasNavigationServer", 10, 3);
            scanner = SpawnPowered("KiasRoomScanner", 5, 1);
            var secondScanner = SpawnPowered("KiasRoomScanner", 6, 1);
            SpawnPowered("KiasAtmosServer", 2, 0);
            SpawnPowered("KiasRecorder", 3, 0);
            SpawnPowered("KiasNavigationServer", 2, 1);
            SpawnPowered("KiasHorizon", 4, 0);
            var cables = new EntityUid[11];
            for (var x = 0; x < cables.Length; x++)
                cables[x] = em.SpawnEntity("KiasDataCable", new EntityCoordinates(map.Grid, x + 0.5f, 0.5f));
            kias.Rebuild(map.Grid);
            Assert.Multiple(() =>
            {
                Assert.That(kias.IsOnline(device), Is.True);
                Assert.That(kias.IsOnline(outside), Is.False);
                Assert.That(em.HasComponent<NodeContainerComponent>(cables[0]), Is.False);
                Assert.That(em.GetComponent<CableComponent>(cables[0]).CableType, Is.EqualTo(CableType.Data));
            });
            em.DeleteEntity(cables[5]);
            kias.Rebuild(map.Grid);
            Assert.That(kias.IsOnline(device), Is.False);
            em.SpawnEntity("KiasDataCable", new EntityCoordinates(map.Grid, 5.5f, 0.5f));
            kias.Rebuild(map.Grid);
            Assert.That(kias.IsOnline(device), Is.True);
            crew.RebuildCoverage(map.Grid);
        });
        await pair.RunTicksSync(30);
        await server.WaitAssertion(() =>
        {
            var token = em.SpawnEntity("KiasCrewTransponder", new EntityCoordinates(map.Grid, 4.5f, 1.5f));
            var serverComponent = em.GetComponent<KiasCrewServerComponent>(device);
            Assert.That(serverComponent.RegistrationLocked, Is.False);
            var registration = new InteractUsingEvent(core, token, device, new EntityCoordinates(map.Grid, 10.5f, 2.5f));
            em.EventBus.RaiseLocalEvent(device, registration);
            Assert.That(em.GetComponent<KiasTransponderComponent>(token).Core, Is.EqualTo(core));
            var rejectedToken = em.SpawnEntity("KiasCrewTransponder", new EntityCoordinates(map.Grid, 4.5f, 1.5f));
            serverComponent.RegistrationLocked = true;
            registration = new InteractUsingEvent(core, rejectedToken, device, new EntityCoordinates(map.Grid, 10.5f, 2.5f));
            em.EventBus.RaiseLocalEvent(device, registration);
            Assert.That(em.GetComponent<KiasTransponderComponent>(rejectedToken).Core, Is.Null);
            serverComponent.RegistrationLocked = false;
            registration = new InteractUsingEvent(core, token, device, new EntityCoordinates(map.Grid, 10.5f, 2.5f));
            em.EventBus.RaiseLocalEvent(device, registration);
            crew.RefreshCounts(map.Grid);
            Assert.That(em.GetComponent<KiasGridComponent>(map.Grid).Crew, Is.EqualTo(1));
            var containers = em.System<SharedContainerSystem>();
            var moduleSlot = containers.GetContainer(scanner, "kias-module-3");
            foreach (var module in moduleSlot.ContainedEntities.ToArray()) em.DeleteEntity(module);
            var spectral = em.SpawnEntity("KiasSpectralModule", new EntityCoordinates(map.Grid, 5.5f, 1.5f));
            Assert.That(containers.Insert(spectral, moduleSlot), Is.True);
            var anomaly = em.SpawnEntity(null, new EntityCoordinates(map.Grid, 5.5f, 1.5f));
            em.AddComponent(anomaly, new AnomalyComponent { InitialStabilityRange = (0, 0) });
            var anomalies = em.System<SharedAnomalySystem>();
            anomalies.ChangeAnomalyStability(anomaly, -1f);
            var log = em.GetComponent<KiasGridComponent>(map.Grid).Log;
            var before = log.Count;
            anomalies.ChangeAnomalyStability(anomaly, 0.6f);
            Assert.That(log.Count, Is.EqualTo(before + 1));
            anomalies.ChangeAnomalyStability(anomaly, 0.1f);
            Assert.That(log.Count, Is.EqualTo(before + 1));
            anomalies.ChangeAnomalyStability(anomaly, -0.7f);
            anomalies.ChangeAnomalyStability(anomaly, 0.6f);
            Assert.That(log.Count, Is.EqualTo(before + 1));
            Assert.That(log.Last(), Does.Contain(em.System<KiasSafetySystem>().Location(map.Grid, anomaly)));
            em.DeleteEntity(anomaly);
            var transform = em.System<SharedTransformSystem>();
            var arrival = maps.CreateGridEntity(map.MapId);
            maps.SetTile(arrival, arrival.Comp, new Vector2i(0, 0), map.Tile.Tile);
            transform.SetLocalPosition(arrival, new Vector2(500, 0));
            var contactBefore = log.Count;
            var ftl = new FTLCompletedEvent(arrival, map.MapUid);
            em.EventBus.RaiseLocalEvent(arrival, ref ftl, true);
            Assert.That(log.Count, Is.EqualTo(contactBefore + 1));
            transform.SetLocalPosition(arrival, new Vector2(3000, 0));
            em.EventBus.RaiseLocalEvent(arrival, ref ftl, true);
            Assert.That(log.Count, Is.EqualTo(contactBefore + 1));
            var ownFtl = new FTLCompletedEvent(map.Grid, map.MapUid);
            em.EventBus.RaiseLocalEvent(map.Grid, ref ownFtl, true);
            Assert.That(log.Count, Is.EqualTo(contactBefore + 1));
            var autopilot = new KiasAutopilotArrivedEvent(map.Grid, core);
            em.EventBus.RaiseLocalEvent(map.Grid, ref autopilot, true);
            Assert.That(log.Count, Is.EqualTo(contactBefore + 2));
            var tool = em.SpawnEntity("KiasServiceTool", new EntityCoordinates(map.Grid, 0.5f, 0.5f));
            var toolComponent = em.GetComponent<KiasServiceToolComponent>(tool);
            var speaker = SpawnPowered("KiasSpeaker", 5, 0);
            kias.Rebuild(map.Grid);
            toolComponent.Mode = KiasServiceMode.Link;
            toolComponent.Message = "Custom room announcement";
            var link = new AfterInteractEvent(core, tool, scanner, new EntityCoordinates(map.Grid, 5.5f, 1.5f), true);
            em.EventBus.RaiseLocalEvent(tool, link);
            link = new AfterInteractEvent(core, tool, speaker, new EntityCoordinates(map.Grid, 5.5f, 0.5f), true);
            em.EventBus.RaiseLocalEvent(tool, link);
            var speakerComponent = em.GetComponent<KiasSpeakerComponent>(speaker);
            Assert.That(speakerComponent.Links, Has.Count.EqualTo(1));
            Assert.Multiple(() =>
            {
                Assert.That(speakerComponent.Links[0].Source, Is.EqualTo(scanner));
                Assert.That(speakerComponent.Links[0].SourcePort, Is.EqualTo("KiasMotion"));
                Assert.That(speakerComponent.Links[0].Message, Is.EqualTo(toolComponent.Message));
            });
            toolComponent.Mode = KiasServiceMode.Test;
            var test = new AfterInteractEvent(core, tool, scanner, new EntityCoordinates(map.Grid, 5.5f, 1.5f), true);
            em.EventBus.RaiseLocalEvent(tool, test);
            Assert.That(em.System<KiasServiceSystem>().IsTesting(map.Grid), Is.True);
            var cooldown = toolComponent.NextTest;
            test = new AfterInteractEvent(core, tool, scanner, new EntityCoordinates(map.Grid, 5.5f, 1.5f), true);
            em.EventBus.RaiseLocalEvent(tool, test);
            Assert.That(toolComponent.NextTest, Is.EqualTo(cooldown));
            for (var cycle = 0; cycle < 10; cycle++)
            {
                kias.SetEnabled(core, false);
                Assert.Multiple(() =>
                {
                    Assert.That(kias.ActiveGrids, Does.Not.Contain(map.Grid.Owner));
                    Assert.That(kias.IsOnline(device), Is.False);
                    Assert.That(em.System<KiasServiceSystem>().IsTesting(map.Grid), Is.False);
                });
                var offlineCount = log.Count;
                em.EventBus.RaiseLocalEvent(map.Grid, ref autopilot, true);
                Assert.That(log.Count, Is.EqualTo(offlineCount));
                kias.SetEnabled(core, true);
                Assert.That(kias.IsOnline(device), Is.True);
            }
            var duplicate = SpawnPowered("KiasCore", 1, 0);
            kias.Rebuild(map.Grid);
            Assert.That(kias.ActiveGrids, Does.Not.Contain(map.Grid.Owner));
            Assert.That(em.GetComponent<KiasDeviceComponent>(core).Status, Is.EqualTo(KiasDeviceStatus.DuplicateCore));
            em.DeleteEntity(duplicate);
            kias.Rebuild(map.Grid);
            Assert.That(kias.IsOnline(device), Is.True);
            em.DeleteEntity(device);
            em.DeleteEntity(scanner);
            kias.Rebuild(map.Grid);
            Assert.That(em.GetComponent<KiasGridComponent>(map.Grid).Devices, Does.Not.Contain(device));
            Assert.That(speakerComponent.Links, Is.Empty);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task PlayerBodyRemainsTrackedAfterDeathAndMindTransfer()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        var minds = em.System<SharedMindSystem>();
        var crew = em.System<KiasCrewSystem>();
        var player = pair.Server.ResolveDependency<IPlayerManager>().Sessions.Single();
        await pair.Server.WaitAssertion(() =>
        {
            var coordinates = new EntityCoordinates(map.Grid, 0.5f, 0.5f);
            var body = em.SpawnEntity("MobHuman", coordinates);
            var npc = em.SpawnEntity("MobCat", coordinates);
            var borg = em.SpawnEntity("BorgChassisGeneric", coordinates);
            var mind = minds.CreateMind(player.UserId);
            minds.TransferTo(mind, body);
            var ownership = em.EnsureComponent<ShipOwnershipComponent>(map.Grid);
            ownership.OwnerUserId = player.UserId;
            var core = em.SpawnEntity("KiasCore", coordinates);
            var key = em.SpawnEntity("KiasMasterKey", coordinates);
            var kias = em.System<KiasSystem>();
            em.EnsureComponent<ShuttleDeedComponent>(map.Grid).ShuttleUid = map.Grid.Owner;
            var deedId = em.SpawnEntity("PassengerIDCard", coordinates);
            em.EnsureComponent<ShuttleDeedComponent>(deedId).ShuttleUid = map.Grid.Owner;
            Assert.That(em.System<Content.Shared.Hands.EntitySystems.SharedHandsSystem>().TryPickup(body, deedId), Is.True);
            kias.Rebuild(map.Grid);
            var keyUse = new InteractUsingEvent(npc, key, core, coordinates);
            em.EventBus.RaiseLocalEvent(core, keyUse);
            Assert.That(em.GetComponent<KiasCoreComponent>(core).Enabled, Is.True);
            keyUse = new InteractUsingEvent(body, key, core, coordinates);
            em.EventBus.RaiseLocalEvent(core, keyUse);
            Assert.That(em.GetComponent<KiasCoreComponent>(core).Enabled, Is.False);
            keyUse = new InteractUsingEvent(body, key, core, coordinates);
            em.EventBus.RaiseLocalEvent(core, keyUse);
            Assert.That(em.GetComponent<KiasCoreComponent>(core).Enabled, Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(crew.IsKiasTrackedEntity(body), Is.True);
                Assert.That(crew.IsKiasTrackedEntity(borg), Is.True);
                Assert.That(crew.IsKiasTrackedEntity(npc), Is.False);
            });
            em.System<MobStateSystem>().ChangeMobState(body, MobState.Dead);
            minds.TransferTo(mind, null, createGhost: false);
            Assert.That(crew.IsKiasTrackedEntity(body), Is.True);
            var chimera = em.SpawnEntity("MobLetoferolHorror", coordinates);
            Assert.That(crew.IsKiasTrackedEntity(chimera), Is.False);
            minds.TransferTo(mind, chimera);
            Assert.That(crew.IsKiasTrackedEntity(chimera), Is.True);
            var artifact = em.SpawnEntity("SimpleXenoArtifact", coordinates);
            em.EnsureComponent<Content.Shared.Mind.Components.MindContainerComponent>(artifact);
            Assert.That(crew.IsKiasTrackedEntity(artifact), Is.False);
            minds.TransferTo(mind, artifact);
            Assert.That(crew.IsKiasTrackedEntity(artifact), Is.True);
            Assert.That(crew.IsKiasTrackedEntity(chimera), Is.True);
            minds.TransferTo(mind, null, createGhost: false);
            Assert.That(crew.IsKiasTrackedEntity(artifact), Is.True);
        });
        await pair.CleanReturnAsync();
    }
}
