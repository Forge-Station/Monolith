#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.IntegrationTests.Pair;
using Content.IntegrationTests.Tests._WF.Planets;
using Content.Server._NF.GameRule;
using Content.Server._NF.Trade;
using Content.Server._WF.PlanetCargoHubs;
using Content.Server._WF.PlanetMap;
using Content.Server.Shuttles.Components;
using Content.Shared._WF.PlanetMap;
using Content.Shared._WF.PlanetSignals;
using Content.Server._WF.PlanetPois;
using Content.Server._WF.Planets;
using Content.Server._WF.Planets.Bounds;
using Content.Server.GameTicking;
using Content.Server.Station.Systems;
using Content.Shared._NF.CCVar;
using Content.Shared._NF.Trade;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.LandingGuide;
using Content.Shared._WF.PlanetCargoHubs;
using Content.Shared._WF.PlanetPois;
using Content.Shared._WF.Planets;
using Content.Server.Cargo.Components;
using Content.Shared.Cargo.Events;
using Content.Shared.CCVar;
using Content.Server.GameTicking.Presets;
using Content.Shared.Light.Components;
using Content.Shared.Light.EntitySystems;
using Content.Shared.Stacks;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.IntegrationTests.Tests._WF.PlanetCargoHubs;

/// <summary>
/// A round that moves depot A onto a planet builds the hub on the world with clear berths and a station that takes
/// depot A's trade crates, and the space depots skip that letter.
/// </summary>
[TestFixture]
[TestOf(typeof(WFPlanetCargoHubSystem))]
public sealed class PlanetCargoHubTest
{
    private const string Surface = "WFSurfaceMerak";
    private const int Radius = 320;
    private static readonly Vector2 BodyPosition = new(4000f, 0f);

    [Test]
    public async Task DepotMovesOntoTheWorld()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var hubs = server.System<WFPlanetCargoHubSystem>();
        var (layers, sectorMap, body) = await BuildHubWorld(pair, null);
        var ground = layers[0];

        EntityUid station = default;
        WFPlanetPoiSite site = default!;
        await server.WaitAssertion(() =>
        {
            // One world: depot A goes down on it and depot B, with no other world to go to, stays in space.
            Assert.That(hubs.Plan.Keys, Is.EquivalentTo(new[] { 0 }));
            Assert.That(hubs.Plan[0].Surface.Id, Is.EqualTo(Surface));

            var record = entMan.GetComponent<WFPlanetPoiComponent>(ground);
            Assert.That(record.GenerationError, Is.Null);
            site = record.Sites.Single(placed => placed.Name == WFPlanetCargoHubSystem.SiteName(0));
            Assert.That(site.MapPath, Is.EqualTo("/Maps/_WF/PlanetCargoHubs/cargo_hub_triple.yml"));
            Assert.That(site.Sockets, Is.Not.Empty, "The hub is off the roads.");
            Assert.That(entMan.GetComponent<WFPoiSignalComponent>(site.Signal).Revealed, Is.True, "The hub doesn't show from orbit.");

            Assert.That(hubs.HubStation(0), Is.Not.Null, "Depot A's hub has no station.");
            station = hubs.HubStation(0)!.Value;
            Assert.That(server.System<StationSystem>().GetOwningStation(ground), Is.EqualTo(station));
            Assert.That(entMan.GetComponent<TradeCrateDestinationComponent>(station).DestinationProto.Id, Is.EqualTo("CargoA"));
            Assert.That(entMan.GetComponent<MetaDataComponent>(station).EntityName, Is.EqualTo("Cargo Depot A"));
            Assert.That(entMan.GetComponent<MetaDataComponent>(ground).EntityName, Is.Not.EqualTo("Cargo Depot A"), "The world took the depot's name.");

            // The round is told once, naming the world and the letter; trade crates for it say the same.
            var hub = entMan.GetComponent<WFPlanetCargoHubComponent>(station);
            Assert.That(hub.World, Is.EqualTo("Merak"));
            hubs.AnnounceHubs();
            Assert.That(hub.Announced, Is.True, "The depot was never announced.");
            Assert.That(hubs.AnnounceHubs(), Is.EqualTo(0), "The depot was announced twice.");
            Assert.That(hubs.Notice(hub), Is.EqualTo("Cargo Depot A has set down on Merak; land on its berths."));
            Assert.That(hubs.CrateNote(station), Is.EqualTo(hubs.Notice(hub)));

            // The planet map marks it as a depot, by name, from orbit.
            var marker = server.System<WFPlanetMapSystem>().Sites(record).Single(found => found.Name == "wf-planet-cargo-hub-a");
            Assert.That(marker.Kind, Is.EqualTo(WFPlanetMapSiteKind.Depot));

            // The sector map has an FTL beacon by the world, named after the depot.
            Assert.That(hub.Beacon, Is.Not.Null, "The depot has no FTL beacon.");
            var beacons = entMan.EntityQuery<FTLBeaconComponent, TransformComponent>()
                .Where(found => found.Item2.MapUid == sectorMap)
                .Select(found => found.Item1.Owner)
                .Where(uid => entMan.GetComponent<MetaDataComponent>(uid).EntityName == "Cargo Depot A (Merak)")
                .ToList();
            Assert.That(beacons, Is.EquivalentTo(new[] { hub.Beacon!.Value }), "The FTL beacon list doesn't have the depot.");
            var beaconAt = server.System<SharedTransformSystem>().GetWorldPosition(hub.Beacon.Value);
            Assert.That(Vector2.Distance(beaconAt, BodyPosition), Is.InRange(1f, 2000f), "The beacon isn't by the world.");

            // Three berths, each a clear, unroofed pad of floor big enough for a medium ship.
            var grid = entMan.GetComponent<MapGridComponent>(ground);
            var roof = entMan.GetComponent<RoofComponent>(ground);
            var maps = server.System<SharedMapSystem>();
            var roofs = server.System<SharedRoofSystem>();
            var berths = entMan.EntityQuery<WFCargoHubBerthComponent, TransformComponent>()
                .Where(found => found.Item2.GridUid == ground && site.Bounds.ContainsTile(maps.TileIndicesFor(ground, grid, found.Item2.Coordinates)))
                .ToList();
            Assert.That(berths, Has.Count.EqualTo(3));
            foreach (var (berth, xform) in berths)
            {
                Assert.That(berth.Size.X, Is.GreaterThanOrEqualTo(24));
                Assert.That(berth.Size.Y, Is.GreaterThanOrEqualTo(36));
                var corner = maps.TileIndicesFor(ground, grid, xform.Coordinates);
                for (var x = 0; x < berth.Size.X; x++)
                for (var y = 0; y < berth.Size.Y; y++)
                {
                    var tile = corner + new Vector2i(x, y);
                    Assert.That(maps.GetTileRef(ground, grid, tile).Tile.IsEmpty, Is.False, $"Berth tile {tile} has no floor.");
                    Assert.That(roofs.IsRooved((ground, grid, roof), tile), Is.False, $"Berth tile {tile} is roofed.");
                    foreach (var anchored in maps.GetAnchoredEntities((ground, grid), tile))
                    {
                        Assert.That(entMan.TryGetComponent<FixturesComponent>(anchored, out var fixtures) && WFLandingSurveySystem.IsSolid(fixtures),
                            Is.False, $"{entMan.ToPrettyString(anchored)} blocks berth tile {tile}.");
                    }
                }
            }
        });

        // The world's signal list names the depot, so its orbit marker and radar tag show it.
        bool SignalNamed()
        {
            return entMan.TryGetComponent<WFPlanetSignalsComponent>(body, out var signals)
                   && signals.Signals.Any(signal => signal.Kind == WFPlanetSignalKind.Site
                                                     && signal.Name == "wf-planet-cargo-hub-a"
                                                     && WFPlanetCargoHubSites.IsHub(signal.Name));
        }

        var named = false;
        for (var attempt = 0; attempt < 40 && !named; attempt++)
        {
            await pair.RunTicksSync(10);
            await server.WaitPost(() => named = SignalNamed());
        }

        await server.WaitAssertion(() => Assert.That(SignalNamed(), Is.True, "The world's signals don't name the depot."));

        // A trade crate for depot A, put on a sell pallet by the hub's console, sells at its destination price.
        EntityUid crate = default;
        EntityUid console = default;
        await server.WaitPost(() =>
        {
            var maps = server.System<SharedMapSystem>();
            var grid = entMan.GetComponent<MapGridComponent>(ground);
            var transform = server.System<SharedTransformSystem>();
            bool InSite(EntityUid uid) => entMan.GetComponent<TransformComponent>(uid).GridUid == ground
                                          && site.Bounds.ContainsTile(maps.TileIndicesFor(ground, grid, entMan.GetComponent<TransformComponent>(uid).Coordinates));
            console = entMan.EntityQuery<CargoPalletConsoleComponent>().Select(found => found.Owner).First(InSite);
            var at = transform.GetWorldPosition(console);
            var pallet = entMan.EntityQuery<CargoPalletComponent>().Select(found => found.Owner)
                .Where(InSite)
                .OrderBy(uid => Vector2.Distance(transform.GetWorldPosition(uid), at))
                .First();
            Assert.That(Vector2.Distance(transform.GetWorldPosition(pallet), at), Is.LessThanOrEqualTo(8f), "No sell pallet within the console's reach.");
            crate = entMan.SpawnEntity("CrateTradeSecureNormal", entMan.GetComponent<TransformComponent>(pallet).Coordinates);
            // The destination is drawn at random among the depots; set it to this one.
            typeof(TradeCrateComponent).GetField(nameof(TradeCrateComponent.DestinationStation))!
                .SetValue(entMan.GetComponent<TradeCrateComponent>(crate), station);
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var value = entMan.GetComponent<TradeCrateComponent>(crate).ValueAtDestination;
            entMan.EventBus.RaiseLocalEvent(console, new CargoPalletSellMessage());
            Assert.That(entMan.Deleted(crate), Is.True, "The crate wasn't sold.");
            var transform = server.System<SharedTransformSystem>();
            var at = transform.GetWorldPosition(console);
            var paid = entMan.EntityQuery<StackComponent, TransformComponent>()
                .Where(found => found.Item2.GridUid == ground && Vector2.Distance(transform.GetWorldPosition(found.Item2), at) < 1f)
                .Sum(found => found.Item1.Count);
            Assert.That(paid, Is.GreaterThanOrEqualTo(value), "The crate sold for less than its destination price.");
        });

        // The space depots: B spawns, A doesn't.
        await server.WaitAssertion(() =>
        {
            // The space depots only spawn under a preset; a pooled server between rounds may have none.
            var ticker = server.System<GameTicker>();
            var preset = typeof(GameTicker).GetProperty(nameof(GameTicker.CurrentPreset))!;
            var oldPreset = ticker.CurrentPreset;
            preset.SetValue(ticker, server.ProtoMan.Index<GamePresetPrototype>("NFAdventure"));
            var depotProtos = server.ProtoMan.EnumeratePrototypes<PointOfInterestPrototype>().Where(poi => poi.SpawnGroup == "CargoDepot").ToList();
            var maps = server.System<SharedMapSystem>();
            maps.CreateMap(out var mapId);
            server.System<PointOfInterestSystem>().GenerateDepots(mapId, depotProtos, out var depots);
            var names = depots.Select(depot => entMan.GetComponent<MetaDataComponent>(depot).EntityName).ToList();
            Assert.That(names, Is.EquivalentTo(new[] { "Cargo Depot B" }));
            var stations = server.System<StationSystem>();
            foreach (var depot in depots)
            {
                if (stations.GetOwningStation(depot) is { } depotStation)
                    entMan.DeleteEntity(depotStation);
            }

            maps.DeleteMap(mapId);
            preset.SetValue(ticker, oldPreset);
            hubs.ResetPlan();
        });

        await PlanetFixture.Teardown(pair, layers);
        await server.WaitPost(() => entMan.DeleteEntity(sectorMap));
        await pair.CleanReturnAsync();
    }

    /// <summary>A depot always goes down when a world can take it, however low the chance; a chance of 0 keeps both in space.</summary>
    [TestCase(0.0001f, 1)]
    [TestCase(0f, 0)]
    public async Task OneDepotAlwaysGoesDown(float chance, int expected)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var hubs = server.System<WFPlanetCargoHubSystem>();
        var (layers, sectorMap, _) = await BuildHubWorld(pair, 11, chance);

        await server.WaitAssertion(() =>
        {
            Assert.That(hubs.Plan, Has.Count.EqualTo(expected));
            var record = entMan.GetComponent<WFPlanetPoiComponent>(layers[0]);
            Assert.That(record.Sites.Count(placed => placed.Name == WFPlanetCargoHubSystem.SiteName(0)
                                                     || placed.Name == WFPlanetCargoHubSystem.SiteName(1)), Is.EqualTo(expected));
            hubs.ResetPlan();
        });

        await PlanetFixture.Teardown(pair, layers);
        await server.WaitPost(() => entMan.DeleteEntity(sectorMap));
        await pair.CleanReturnAsync();
    }

    /// <summary>Whatever the round's dice, the hub is on the roads: the planner routes it first and never leaves it off.</summary>
    [TestCase(1)]
    [TestCase(7)]
    [TestCase(23)]
    [TestCase(4242)]
    public async Task HubIsAlwaysOnTheRoads(int seed)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var hubs = server.System<WFPlanetCargoHubSystem>();
        var (layers, sectorMap, _) = await BuildHubWorld(pair, seed);

        await server.WaitAssertion(() =>
        {
            var record = entMan.GetComponent<WFPlanetPoiComponent>(layers[0]);
            var site = record.Sites.Single(placed => placed.Name == WFPlanetCargoHubSystem.SiteName(0));
            Assert.That(site.Connection, Is.Not.EqualTo(WFPlanetPoiConnection.Isolated), $"Seed {seed}: the hub was left off the roads.");
            Assert.That(site.Sockets, Is.Not.Empty, $"Seed {seed}: the hub has no entrance on the roads.");
            Assert.That(record.RoadTiles, Is.Not.Empty, $"Seed {seed}: the world has no roads.");
            var socket = site.Sockets[0].Position + WFPlanetRoadPlanner.Direction(site.Sockets[0].Direction);
            Assert.That(record.RoadTiles, Does.Contain(socket), $"Seed {seed}: no road leaves the hub's entrance.");
            hubs.ResetPlan();
        });

        await PlanetFixture.Teardown(pair, layers);
        await server.WaitPost(() => entMan.DeleteEntity(sectorMap));
        await pair.CleanReturnAsync();
    }

    /// <summary>Builds Merak with depot A's hub on it, its sites placed and preloaded; a seed fixes the round's dice.</summary>
    private static async Task<(List<EntityUid> Layers, EntityUid SectorMap, EntityUid Body)> BuildHubWorld(TestPair pair, int? seed, float chance = 1f)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        await PlanetFixture.EnableFeature(pair);
        var hubs = server.System<WFPlanetCargoHubSystem>();
        var layers = new List<EntityUid>();
        var holds = new List<Task>();
        EntityUid sectorMap = default;
        EntityUid body = default;

        await server.WaitPost(() =>
        {
            if (seed is { } fixedSeed)
                server.ResolveDependency<IRobustRandom>().SetSeed(fixedSeed);
            server.CfgMan.SetCVar(CCVars.NPCEnabled, false);
            server.CfgMan.SetCVar(PlanetCVars.Bounds, true);
            server.CfgMan.SetCVar(PlanetCargoHubCVars.Chance, chance);
            server.CfgMan.SetCVar(NFCCVars.CargoDepots, 2);
            hubs.ResetPlan();

            // The world's sector body, for its signals and the depot's FTL beacon.
            sectorMap = server.System<SharedMapSystem>().CreateMap(out _);
            body = entMan.SpawnEntity(null, new EntityCoordinates(sectorMap, BodyPosition));
            server.System<MetaDataSystem>().SetEntityName(body, "Merak");
            entMan.AddComponent<WFSectorPlanetComponent>(body).Surface = Surface;

            var surface = server.ProtoMan.Index<WFPlanetSurfacePrototype>(Surface);
            var network = server.System<WFPlanetNetworkSystem>().BuildNetwork(surface, Vector2.Zero, "Merak", body);
            Assert.That(network, Is.Not.Null);
            layers.AddRange(entMan.GetComponent<WFPlanetNetworkComponent>(network!.Value).Layers);
            var preload = new WFPlanetPreloadStartingEvent(layers[0], Vector2.Zero, Radius, holds);
            entMan.EventBus.RaiseEvent(EventSource.Local, ref preload);
        });

        for (var attempt = 0; attempt < 900 && holds.Any(hold => !hold.IsCompleted); attempt++)
        {
            await pair.RunTicksSync(10);
        }

        var ground = layers[0];
        var work = new List<Task>();
        await server.WaitPost(() =>
        {
            Assert.That(holds.All(hold => hold.IsCompletedSuccessfully), Is.True, "The world's sites failed to place.");
            var preloaded = new WFPlanetPreloadedEvent(ground, work);
            entMan.EventBus.RaiseEvent(EventSource.Local, ref preloaded);
        });

        for (var attempt = 0; attempt < 300 && work.Any(task => !task.IsCompleted); attempt++)
        {
            await pair.RunTicksSync(10);
        }

        return (layers, sectorMap, body);
    }
}
