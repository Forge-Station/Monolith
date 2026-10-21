using System.Linq;
using System.Numerics;
using Content.Server._NF.Shuttles.Components;
using Content.Server._WF.Planets;
using Content.Server._WF.Planets.Flight;
using Content.Server._WF.RoundSetup;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Events;
using Content.Server.Station.Components;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared._WF.Planets.Flight;
using Content.Shared._WF.RoundSetup;
using Content.Shared.Maps;
using Content.Shared.Physics;
using Content.Shared.UserInterface;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using static Content.IntegrationTests.Tests._WF.Planets.PlanetFixture;

namespace Content.IntegrationTests.Tests._WF.RoundSetup;

/// <summary>Crash starts hold their full warning window, share a ship and fail closed when it is gone.</summary>
[TestFixture]
[TestOf(typeof(WFCrashScenarioSystem))]
public sealed class CrashScenarioTest
{
    /// <summary>Every random candidate loads as a shuttle with enough unobstructed deck for a full crew.</summary>
    [TestCaseSource(typeof(WFCrashScenarioSystem), nameof(WFCrashScenarioSystem.VesselIds))]
    public async Task CrashVesselHasUsableCrewSpace(string vesselId)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        await server.WaitAssertion(() =>
        {
            var vessel = server.ResolveDependency<IPrototypeManager>().Index<VesselPrototype>(vesselId);
            Assert.That(server.System<MapLoaderSystem>().TryLoadGrid(map.MapId, vessel.ShuttlePath, out var loaded),
                Is.True, $"Crash candidate {vesselId} must load.");
            var hull = loaded!.Value.Owner;
            em.AddComponents(hull, vessel.AddComponents);
            Assert.That(em.HasComponent<ShuttleComponent>(hull), Is.True);
            var turf = server.System<TurfSystem>();
            var tiles = server.System<SharedMapSystem>().GetAllTilesEnumerator(hull, loaded.Value.Comp);
            var usable = 0;
            while (tiles.MoveNext(out var tile))
            {
                if (!tile.Value.Tile.IsEmpty && !turf.IsSpace(tile.Value)
                    && !turf.IsTileBlocked(tile.Value, CollisionGroup.MobMask))
                    usable++;
            }
            Assert.That(usable, Is.GreaterThanOrEqualTo(WFRoundSetupPolicy.MaximumMembers),
                $"Crash candidate {vesselId} needs room for the full crew.");
            em.DeleteEntity(hull);
        });
        await pair.CleanReturnAsync();
    }

    /// <summary>Preparation is unarmed, boarding starts three minutes, and expiry uses normal descent.</summary>
    [Test]
    public async Task BoardingArmsSharedShipAndExpiryDropsFromOrbit()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var em = server.EntMan;
        await EnableFeature(pair);
        var (layers, _, _) = await BuildOwnedStack(pair);
        var system = server.System<WFCrashScenarioSystem>();
        var hull = EntityUid.Invalid;
        var station = EntityUid.Invalid;
        var actor = EntityUid.Invalid;
        const int lobby = 19001;

        await server.WaitAssertion(() =>
        {
            Assert.That(system.TryPrepare(lobby, out station, out var reason), Is.True, reason);
            Assert.That(system.TryGetSpawn(lobby, out var first), Is.True);
            hull = first.EntityId;
            Assert.That(system.TryGetSpawn(lobby, out var second), Is.True);
            Assert.That(second.EntityId, Is.EqualTo(hull));
            Assert.That(em.HasComponent<StationJobsComponent>(station), Is.True);
            Assert.That(em.HasComponent<StationSpawningComponent>(station), Is.True);
            Assert.That(em.GetComponent<WFCrashScenarioStationComponent>(station).LobbyId, Is.EqualTo(lobby));
            Assert.That(em.GetComponent<WFCrashScenarioComponent>(hull).Started, Is.False);
            Assert.That(em.HasComponent<ForceAnchorComponent>(hull), Is.True);
            Assert.That(em.HasComponent<Content.Shared._Mono.ShipRepair.Components.ShipRepairDataComponent>(hull), Is.True,
                "the crash hull has no repair data to rebuild the wreck from.");
            Assert.That(em.GetComponent<TransformComponent>(hull).MapUid, Is.EqualTo(layers[^1]));
            var turf = server.System<TurfSystem>();
            Assert.That(turf.TryGetTileRef(first, out var tile), Is.True);
            Assert.That(turf.IsSpace(tile!.Value), Is.False);
            Assert.That(turf.IsTileBlocked(tile.Value, CollisionGroup.MobMask), Is.False);
            actor = em.SpawnEntity("MobHuman", first);
            Assert.That(system.TryGetCountdown(actor, out _), Is.False);
            var helm = FindShuttleConsole(em, hull);
            Assert.That(helm, Is.Not.EqualTo(EntityUid.Invalid));
            var open = new ActivatableUIOpenAttemptEvent(actor);
            em.EventBus.RaiseLocalEvent(helm, open);
            Assert.That(open.Cancelled, Is.True);
            var ftl = new ConsoleFTLAttemptEvent(hull, false, string.Empty);
            em.EventBus.RaiseEvent(EventSource.Local, ref ftl);
            Assert.That(ftl.Cancelled, Is.True);
            var engines = Children(em, hull).Where(em.HasComponent<ThrusterComponent>).ToArray();
            Assert.That(engines, Is.Not.Empty);
            Assert.That(engines.All(uid => !em.GetComponent<ThrusterComponent>(uid).Enabled), Is.True);
        });

        // Tick the client too; one left past PVS's 20-tick dirty buffer gets false "empty state" errors.
        await pair.RunTicksSync(pair.SecondsToTicks(13f));
        await server.WaitAssertion(() =>
        {
            Assert.That(em.HasComponent<WFOrbitDecayComponent>(hull), Is.False,
                "The generic sixty-second decay must not replace scenario preparation.");
            Assert.That(em.GetComponent<TransformComponent>(hull).MapUid, Is.EqualTo(layers[^1]));
            system.NotifyArrival(lobby, pair.Player!);
            var crash = em.GetComponent<WFCrashScenarioComponent>(hull);
            var now = server.ResolveDependency<IGameTiming>().CurTime;
            Assert.That(crash.DescendAt - now, Is.EqualTo(TimeSpan.FromMinutes(3)));
            Assert.That(crash.Started, Is.True);
            Assert.That(system.TryGetCountdown(actor, out var deadline), Is.True);
            Assert.That(deadline, Is.EqualTo(crash.DescendAt));
            system.NotifyArrival(lobby, pair.Player!);
            Assert.That(crash.DescendAt - now, Is.EqualTo(TimeSpan.FromMinutes(3)), "A second survivor must not restart the timer.");
            crash.DescendAt = now;
            Assert.That(system.TryGetCountdown(actor, out _), Is.False);
        });

        await pair.RunTicksSync(pair.SecondsToTicks(2f));
        await server.WaitAssertion(() =>
        {
            Assert.That(em.GetComponent<WFCrashScenarioComponent>(hull).Descending, Is.True);
            Assert.That(em.HasComponent<ForceAnchorComponent>(hull), Is.False);
            Assert.That(em.GetComponent<TransformComponent>(hull).MapUid, Is.Not.EqualTo(layers[^1]));
            Assert.That(em.HasComponent<WFLiftLostComponent>(hull), Is.True,
                "The standard atmospheric crash system must take over.");
            system.RemoveScenario(lobby);
            Assert.That(em.EntityExists(hull), Is.True, "Closing a used lobby must preserve its ship and crew.");
            Assert.That(system.TryGetSpawn(lobby, out _), Is.False);
            em.DeleteEntity(actor);
            em.DeleteEntity(station);
        });

        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    /// <summary>An expired crash start falls the whole way and crashes on the ground, after which its helm unlocks.</summary>
    [Test]
    public async Task ExpiredStartCrashesOnTheGround()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        await EnableFeature(pair);
        var (layers, _, _) = await BuildOwnedStack(pair);
        var system = server.System<WFCrashScenarioSystem>();
        var hull = EntityUid.Invalid;
        var station = EntityUid.Invalid;
        const int lobby = 19003;

        await server.WaitAssertion(() =>
        {
            Assert.That(system.TryPrepare(lobby, out station, out var reason), Is.True, reason);
            Assert.That(system.TryGetSpawn(lobby, out var spawn), Is.True);
            hull = spawn.EntityId;
            var crash = em.GetComponent<WFCrashScenarioComponent>(hull);
            crash.Started = true;
            crash.DescendAt = server.ResolveDependency<IGameTiming>().CurTime;
        });

        // Poll the whole scenario, since a breakup moves the impact onto fragments.
        var crashed = EntityUid.Invalid;
        var seconds = 0;
        for (; seconds < 120 && crashed == EntityUid.Invalid; seconds++)
        {
            await server.WaitRunTicks(pair.SecondsToTicks(1f));
            await server.WaitPost(() =>
            {
                var query = em.EntityQueryEnumerator<WFCrashScenarioComponent, WFCrashImpactComponent>();
                while (query.MoveNext(out var uid, out var crash, out _))
                {
                    if (crash.LobbyId == lobby)
                        crashed = uid;
                }
            });
        }

        await server.WaitAssertion(() =>
        {
            var map = em.EntityExists(hull) ? em.GetComponent<TransformComponent>(hull).MapUid : null;
            Assert.That(crashed, Is.Not.EqualTo(EntityUid.Invalid),
                $"The crash start never crashed within {seconds} s; the hull is on {em.ToPrettyString(map)}, "
                + $"lift lost: {em.HasComponent<WFLiftLostComponent>(hull)}.");
            Assert.That(em.GetComponent<TransformComponent>(crashed).MapUid, Is.EqualTo(layers[0]),
                "The crashed hull is not on the ground layer.");
        });

        // The scenario sweep runs twice a second; after it sees the impact the helm opens again.
        await server.WaitRunTicks(pair.SecondsToTicks(1f));
        await server.WaitAssertion(() =>
        {
            Assert.That(em.GetComponent<WFCrashScenarioComponent>(crashed).Crashed, Is.True);
            var helm = FindShuttleConsole(em, crashed);
            if (helm != EntityUid.Invalid)
            {
                var actor = em.SpawnEntity("MobHuman", em.GetComponent<TransformComponent>(helm).Coordinates);
                var open = new ActivatableUIOpenAttemptEvent(actor);
                em.EventBus.RaiseLocalEvent(helm, open);
                Assert.That(open.Cancelled, Is.False, "The helm stayed locked after the crash.");
                em.DeleteEntity(actor);
            }

            system.RemoveScenario(lobby);
            em.DeleteEntity(station);
        });

        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    /// <summary>Destroyed preparations cannot grant a replacement ship or borrow another lobby's spawn.</summary>
    [Test]
    public async Task FragmentsRemainOwnedAndDeletedStationFailsClosed()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        await EnableFeature(pair);
        var (layers, _, _) = await BuildOwnedStack(pair);
        var system = server.System<WFCrashScenarioSystem>();
        var fragment = await BuildHull(pair, em.GetComponent<MapComponent>(layers[^1]).MapId, new Vector2(2000f, 0f));
        await MapInitHull(pair, fragment);
        const int lobby = 19002;
        await server.WaitAssertion(() =>
        {
            Assert.That(system.TryPrepare(lobby, out var station, out var reason), Is.True, reason);
            Assert.That(system.TryGetSpawn(lobby, out var first), Is.True);
            Assert.That(system.TryGetSpawn(lobby + 1, out _), Is.False);
            var original = em.GetComponent<WFCrashScenarioComponent>(first.EntityId);
            original.Started = true;
            original.DescendAt = server.ResolveDependency<IGameTiming>().CurTime + TimeSpan.FromMinutes(3);
            var split = new GridSplitEvent(new[] { fragment }, first.EntityId);
            em.EventBus.RaiseEvent(EventSource.Local, ref split);
            var inherited = em.GetComponent<WFCrashScenarioComponent>(fragment);
            Assert.That(inherited.LobbyId, Is.EqualTo(lobby));
            Assert.That(inherited.DescendAt, Is.EqualTo(original.DescendAt));
            Assert.That(inherited.Started, Is.True);
            var survivorBody = em.SpawnEntity("MobHuman", new EntityCoordinates(fragment, 0.5f, 0.5f));
            Assert.That(system.TryGetCountdown(survivorBody, out var inheritedDeadline), Is.True);
            Assert.That(inheritedDeadline, Is.EqualTo(original.DescendAt));
            em.DeleteEntity(first.EntityId);
            Assert.That(system.TryGetSpawn(lobby, out var survivor), Is.True);
            Assert.That(survivor.EntityId, Is.EqualTo(fragment));
            em.DeleteEntity(station);
            em.DeleteEntity(fragment);
            Assert.That(system.TryPrepare(lobby, out _, out reason), Is.False);
            Assert.That(reason, Is.EqualTo("wf-round-setup-crash-no-wreck"));
            Assert.That(system.TryGetSpawn(lobby, out _), Is.False);
            system.RemoveScenario(lobby);
        });
        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }
}
