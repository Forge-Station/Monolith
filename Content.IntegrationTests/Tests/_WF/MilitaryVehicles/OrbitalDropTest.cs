#nullable enable
using System.Linq;
using System.Numerics;
using Content.Server.Cargo.Systems;
using Content.Server._WF.MilitaryVehicles;
using Content.Shared._WF.MilitaryVehicles;
using Content.Shared._WF.VehicleShop;
using Content.Shared._WF.Vehicles.Core;
using Content.Shared.Light.Components;
using Content.Shared.Light.EntitySystems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using static Content.IntegrationTests.Tests._WF.Planets.PlanetFixture;

namespace Content.IntegrationTests.Tests._WF.MilitaryVehicles;

/// <summary>
/// Orbital drop beacons land their vehicle on a planet's open ground after the countdown, with a key for whoever set
/// them off; they refuse a station deck, a roofed spot and a blocked one. Each is listed in the drops catalogue, resells
/// for less than that, and its vehicle appraises for less than the beacon costs.
/// </summary>
[TestFixture]
[TestOf(typeof(WFOrbitalDropSystem))]
public sealed class OrbitalDropTest
{
    private const string Beacon = "WFOrbitalDropBeaconAPC";
    private const string Vehicle = "VehicleAPC";
    private const string Catalog = "WFMilitaryCatalogDrops";

    /// <summary>Far from the origin, where no terrain has loaded.</summary>
    private static readonly Vector2i Site = new(4000, 4000);

    private static int CountVehicles(IEntityManager entMan, MapId map)
    {
        return entMan.EntityQuery<MetaDataComponent>()
            .Count(meta => meta.EntityPrototype?.ID == Vehicle && entMan.GetComponent<TransformComponent>(meta.Owner).MapID == map);
    }

    [Test]
    public async Task DropsOnOpenGroundOnly()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        await EnableFeature(pair);
        var layers = await BuildStandalone(pair);
        var ground = layers[0];
        await LayTiles(pair, ground, Site - new Vector2i(20, 20), Site + new Vector2i(20, 20));
        var deck = await pair.CreateTestMap();

        var drops = server.System<WFOrbitalDropSystem>();
        var ticksPerSecond = (int) Math.Round(1 / server.Timing.TickPeriod.TotalSeconds);
        var groundMap = entMan.GetComponent<TransformComponent>(ground).MapID;
        EntityUid beacon = default, user = default;

        await server.WaitAssertion(() =>
        {
            var at = new EntityCoordinates(ground, new Vector2(Site.X + 0.5f, Site.Y + 0.5f));
            beacon = entMan.SpawnEntity(Beacon, at);
            user = entMan.SpawnEntity("MobHuman", at.Offset(new Vector2(8f, 0f)));
            var comp = entMan.GetComponent<WFOrbitalDropBeaconComponent>(beacon);
            var ent = (beacon, comp);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(drops.CheckDropZone(ent, at, out _), Is.Null, "Open ground refused the drop.");
                Assert.That(drops.CheckDropZone(ent, new EntityCoordinates(deck.Grid.Owner, 0.5f, 0.5f), out _),
                    Is.EqualTo("wf-orbital-drop-not-planet"), "A station deck took an orbital drop.");
            }

            // A roof over one tile of the room keeps it off.
            var roofs = server.System<SharedRoofSystem>();
            var grid = entMan.GetComponent<MapGridComponent>(ground);
            entMan.EnsureComponent<RoofComponent>(ground);
            roofs.SetRoof((ground, grid, null), Site + new Vector2i(1, 1), true);
            Assert.That(drops.CheckDropZone(ent, at, out _), Is.EqualTo("wf-orbital-drop-roofed"), "A roofed spot took the drop.");
            roofs.SetRoof((ground, grid, null), Site + new Vector2i(1, 1), false);

            // So does a crate in the way, which is named.
            var crate = entMan.SpawnEntity("CrateGenericSteel", at.Offset(new Vector2(1f, 0f)));
            Assert.That(drops.CheckDropZone(ent, at, out var thing), Is.EqualTo("wf-orbital-drop-blocked"), "A crate in the way didn't block the drop.");
            Assert.That(thing, Is.EqualTo(crate), "The drop named the wrong thing in the way.");
            entMan.DeleteEntity(crate);

            // Whoever sets it off stands on the spot: that arms it, and only blocks the touchdown.
            var caller = entMan.SpawnEntity("MobHuman", at);
            Assert.That(drops.CheckDropZone(ent, at, out _, mobs: false), Is.Null, "The one calling the drop blocked it.");
            Assert.That(drops.CheckDropZone(ent, at, out _), Is.EqualTo("wf-orbital-drop-blocked"), "Someone under the drop didn't block it.");
            entMan.DeleteEntity(caller);

            drops.Arm(ent, user);
        });

        try
        {
            await server.WaitRunTicks((int) (19.5 * ticksPerSecond));
            await server.WaitAssertion(() =>
            {
                Assert.That(CountVehicles(entMan, groundMap), Is.Zero, "The vehicle landed before the countdown was up.");
                Assert.That(entMan.EntityExists(beacon), Is.True, "The beacon went early.");
            });

            await server.WaitRunTicks(2 * ticksPerSecond);
            await server.WaitAssertion(() =>
            {
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(CountVehicles(entMan, groundMap), Is.EqualTo(1), "The vehicle didn't land.");
                    Assert.That(entMan.Deleted(beacon), Is.True, "The used beacon is still there.");

                    var vehicle = entMan.EntityQuery<MetaDataComponent>().First(meta => meta.EntityPrototype?.ID == Vehicle).Owner;
                    var keyId = entMan.GetComponent<VehicleLockComponent>(vehicle).KeyId;
                    var held = false;
                    foreach (var item in server.System<Content.Shared.Hands.EntitySystems.SharedHandsSystem>().EnumerateHeld(user))
                        held |= entMan.TryGetComponent<VehicleKeyComponent>(item, out var key) && key.KeyId == keyId;
                    Assert.That(held, Is.True, "Whoever called the drop wasn't handed its key.");
                }
            });
        }
        finally
        {
            await Teardown(pair, layers);
        }

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task BeaconsNeverSellForAProfit()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var pricing = server.System<PricingSystem>();
            var catalog = server.ProtoMan.Index<WFVehicleCatalogPrototype>(Catalog);
            var beacons = server.ProtoMan.EnumeratePrototypes<EntityPrototype>()
                .Where(proto => !proto.Abstract && proto.TryGetComponent<WFOrbitalDropBeaconComponent>(out _, entMan.ComponentFactory))
                .ToList();
            Assert.That(beacons, Is.Not.Empty, "There are no orbital drop beacons.");

            using (Assert.EnterMultipleScope())
            {
                foreach (var proto in beacons)
                {
                    proto.TryGetComponent<WFOrbitalDropBeaconComponent>(out var comp, entMan.ComponentFactory);
                    Assert.That(comp!.Catalog.Id, Is.EqualTo(Catalog), $"{proto.ID} is priced off another catalogue.");
                    var listing = catalog.Vehicles.FirstOrDefault(entry => entry.Vehicle == proto.ID);
                    Assert.That(listing, Is.Not.Null, $"{proto.ID} isn't sold in {Catalog}.");
                    if (listing == null)
                        continue;

                    var spawned = entMan.SpawnEntity(proto.ID, new MapCoordinates(0, 0, map.MapId));
                    Assert.That(pricing.GetPrice(spawned), Is.InRange(1, listing.Price - 1), $"{proto.ID} resells for nothing, or for what it cost.");
                    entMan.DeleteEntity(spawned);

                    // The vehicle as it lands, parts and all, sells for less than the beacon cost.
                    var vehicle = entMan.SpawnEntity(comp.Vehicle, new MapCoordinates(0, 0, map.MapId));
                    var value = pricing.GetPrice(vehicle);
                    TestContext.Out.WriteLine($"{comp.Vehicle}: appraised {value:0}, beacon listed {listing.Price}");
                    Assert.That(value, Is.LessThan(listing.Price), $"{comp.Vehicle} appraises for more than its drop beacon costs.");
                    entMan.DeleteEntity(vehicle);
                }
            }
        });

        await pair.CleanReturnAsync();
    }
}
