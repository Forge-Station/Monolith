using System.Collections.Generic;
using Content.Server.Power.Components;
using Content.Shared._Crescent.Hardpoints;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Power.Components;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Asf;

/// <summary>Checks the ASF ship guns fire the way their descriptions say.</summary>
[TestFixture]
public sealed class AsfShipWeaponsTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: WFAsfShipWeaponsTestTarget
  components:
  - type: Physics
    bodyType: Static
  - type: Fixtures
    fixtures:
      fix1:
        shape:
          !type:PhysShapeAabb
          bounds: ""-0.5,-0.5,0.5,0.5""
        layer:
        - WallLayer
  - type: Damageable
";

    /// <summary>One GCS click (a 0.2 s auto-fire window) fires a full 3-pulse burst, then the gun waits out its pause.</summary>
    [Test]
    public async Task FlutterFiresOneBurstPerClick()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var gunSystem = entities.System<SharedGunSystem>();
        var maps = entities.System<SharedMapSystem>();

        EntityUid gun = default;
        MapId mapId = default;
        float full = 0;
        await server.WaitPost(() =>
        {
            maps.CreateMap(out mapId);
            gun = entities.SpawnEntity("WFAsfTurretFlutter", new MapCoordinates(0, 0, mapId));
            entities.RemoveComponent<BatterySelfRechargerComponent>(gun);
            full = entities.GetComponent<BatteryComponent>(gun).CurrentCharge;
        });

        async Task<float> Click()
        {
            await server.WaitPost(() => gunSystem.AttemptShots(gun, gun, entities.GetComponent<GunComponent>(gun),
                new EntityCoordinates(maps.GetMap(mapId), 10, 0), TimeSpan.FromSeconds(0.2)));
            await pair.RunSeconds(1);
            return entities.GetComponent<BatteryComponent>(gun).CurrentCharge;
        }

        var cost = entities.GetComponent<HitscanBatteryAmmoProviderComponent>(gun).FireCost;
        Assert.That(await Click(), Is.EqualTo(full - 3 * cost), "One click fires all three pulses.");
        Assert.That(await Click(), Is.EqualTo(full - 3 * cost), "A click during the pause fires nothing.");
        await pair.RunSeconds(1);
        Assert.That(await Click(), Is.EqualTo(full - 6 * cost), "After the pause the next click fires another burst.");

        await pair.CleanReturnAsync();
    }

    /// <summary>Every hardpoint-only gun on an ASF hull loads attached to a compatible hardpoint.</summary>
    [Test]
    public async Task HullGunsSitOnHardpoints()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var prototypes = server.ResolveDependency<IPrototypeManager>();
        var factory = server.ResolveDependency<IComponentFactory>();
        var loader = entities.System<MapLoaderSystem>();
        var maps = entities.System<SharedMapSystem>();

        await server.WaitAssertion(() =>
        {
            Assert.That(prototypes.Index<EntityPrototype>("WFAsfComputerShipyard")
                .TryGetComponent<ShipyardListingComponent>(out var listing, factory));
            foreach (var id in listing!.Shuttles)
            {
                maps.CreateMap(out var mapId);
                Assert.That(loader.TryLoadGrid(mapId, prototypes.Index<VesselPrototype>(id).ShuttlePath, out var grid), Is.True);

                var loose = new List<string>();
                var query = entities.EntityQueryEnumerator<HardpointAnchorableOnlyComponent, TransformComponent>();
                while (query.MoveNext(out var uid, out var gun, out var xform))
                {
                    if (xform.GridUid == grid!.Value.Owner && gun.anchoredTo == null)
                        loose.Add($"{entities.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID} at {xform.Coordinates.Position}");
                }

                Assert.That(loose, Is.Empty, $"Guns off a hardpoint on {id}: " + string.Join(", ", loose));
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>The Hummingbird beam damages the first three objects in its line and stops before the fourth.</summary>
    [Test]
    public async Task HummingbirdCutsThroughThreeObjects()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var gunSystem = entities.System<SharedGunSystem>();
        var maps = entities.System<SharedMapSystem>();

        EntityUid gun = default;
        var targets = new EntityUid[4];
        await server.WaitPost(() =>
        {
            maps.CreateMap(out var mapId);
            gun = entities.SpawnEntity("WFAsfTurretHummingbird", new MapCoordinates(0, 0, mapId));
            for (var i = 0; i < targets.Length; i++)
                targets[i] = entities.SpawnEntity("WFAsfShipWeaponsTestTarget", new MapCoordinates(3 + 2 * i, 0, mapId));
        });
        await pair.RunTicksSync(5);

        await server.WaitPost(() => gunSystem.AttemptShots(gun, gun, entities.GetComponent<GunComponent>(gun),
            new EntityCoordinates(entities.GetComponent<TransformComponent>(gun).MapUid!.Value, 20, 0),
            TimeSpan.FromSeconds(0.2)));
        await pair.RunSeconds(1);

        await server.WaitAssertion(() =>
        {
            FixedPoint2 Structural(EntityUid uid) =>
                entities.GetComponent<DamageableComponent>(uid).Damage.DamageDict.TryGetValue("Structural", out var v)
                    ? v
                    : FixedPoint2.Zero;

            for (var i = 0; i < 3; i++)
                Assert.That(Structural(targets[i]), Is.GreaterThan(FixedPoint2.Zero), $"Object {i + 1} in the line is cut.");
            Assert.That(Structural(targets[3]), Is.EqualTo(FixedPoint2.Zero), "The beam stops after three objects.");
        });

        await pair.CleanReturnAsync();
    }
}
