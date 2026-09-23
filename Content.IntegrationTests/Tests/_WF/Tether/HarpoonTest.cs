using System.Collections.Generic;
using System.Numerics;
using Content.Server._WF.Tether;
using Content.Shared._WF.Tether;
using Content.Shared._WF.Tether.Harpoon;
using Content.Shared.Actions;
using Content.Shared.Buckle;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Systems;

namespace Content.IntegrationTests.Tests._WF.Tether;

/// <summary>
/// The harpoon turret: manning it, the shot-well rule, and the tow cable a good shot leaves behind.
/// </summary>
[TestFixture]
public sealed class HarpoonTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: ropeType
  id: WFTestTowCable
  stiffness: 400000
  dampingRatio: 0.7
  maxStretch: 0.05
  breakForce: 0
  maxLength: 100

- type: entity
  id: WFTestHarpoonTurret
  parent: BaseShipHarpoonTurret
  components:
  - type: ShipHarpoonTurret
    ropeType: WFTestTowCable
    arc: 120
  - type: BallisticAmmoProvider
    proto: ShipHarpoon
    capacity: 1
    cycleable: false
";

    private const string Operator = "MobHuman";
    private const string Wall = "WallSolid";

    [Test]
    public async Task ManningGrantsAndRemovesTheControls()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var maps = server.ResolveDependency<IMapManager>();

        EntityUid grid = default, turret = default, user = default;

        await server.WaitPost(() =>
        {
            entities.DeleteEntity(map.Grid);
            grid = MakeGrid(entities, maps, map.MapId, Vector2.Zero, 3);
            turret = entities.SpawnEntity("WFTestHarpoonTurret", new EntityCoordinates(grid, new Vector2(1.5f, 1.5f)));
            user = entities.SpawnEntity(Operator, new EntityCoordinates(grid, new Vector2(1.5f, 1.5f)));
        });

        await server.WaitRunTicks(5);
        await server.WaitAssertion(() =>
        {
            Assert.That(entities.System<SharedBuckleSystem>().TryBuckle(user, null, turret), Is.True,
                "The operator must be able to strap into the mount.");

            Assert.Multiple(() =>
            {
                Assert.That(entities.TryGetComponent<MannedTurretOperatorComponent>(user, out var manned), Is.True);
                Assert.That(entities.GetComponent<ShipHarpoonTurretComponent>(turret).Operator,
                    Is.EqualTo(entities.GetNetEntity(user)));
                Assert.That(entities.GetComponent<ActionsComponent>(user).Actions, Has.Count.GreaterThanOrEqualTo(3),
                    "Manning grants the reel and release controls.");
            });

            // The whole point of the upstream hook: the operator's shoot input reaches the turret's gun.
            Assert.That(entities.System<SharedGunSystem>().TryGetGun(user, out var gun, out _), Is.True);
            Assert.That(gun, Is.EqualTo(turret));

            entities.System<SharedBuckleSystem>().Unbuckle(user, null);
            Assert.Multiple(() =>
            {
                Assert.That(entities.HasComponent<MannedTurretOperatorComponent>(user), Is.False);
                Assert.That(entities.GetComponent<ShipHarpoonTurretComponent>(turret).Operator, Is.Null);
                Assert.That(entities.System<SharedGunSystem>().TryGetGun(user, out _, out _), Is.False);
            });

            entities.DeleteEntity(user);
            entities.DeleteEntity(grid);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task StraightShotEmbedsAndTiesOffToBothGrids()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var maps = server.ResolveDependency<IMapManager>();

        EntityUid gridA = default, gridB = default, turret = default, user = default;

        await server.WaitPost(() =>
        {
            entities.DeleteEntity(map.Grid);
            gridA = MakeGrid(entities, maps, map.MapId, Vector2.Zero, 3);
            gridB = MakeGrid(entities, maps, map.MapId, new Vector2(10f, 0f), 3);
            entities.SpawnEntity(Wall, new EntityCoordinates(gridB, new Vector2(0.5f, 1.5f)));
            (turret, user) = MakeTurret(entities, gridA);
        });

        await server.WaitRunTicks(5);
        await server.WaitPost(() =>
        {
            Man(entities, user, turret);
            Fire(entities, user, turret, new MapCoordinates(new Vector2(10.5f, 1.5f), map.MapId));
        });

        await server.WaitRunTicks(40);
        await server.WaitAssertion(() =>
        {
            var rope = entities.System<RopeSystem>();
            var turretComp = entities.GetComponent<ShipHarpoonTurretComponent>(turret);
            Assert.That(turretComp.Harpoon, Is.Not.Null, "A good shot stays the turret's harpoon.");
            var harpoon = entities.GetEntity(turretComp.Harpoon!.Value);

            Assert.Multiple(() =>
            {
                Assert.That(entities.GetComponent<ShipHarpoonComponent>(harpoon).Embedded, Is.True,
                    "A square-on shot at speed sinks in.");
                Assert.That(entities.GetComponent<RopeAttachPointComponent>(turret).Ropes, Has.Count.EqualTo(1),
                    "Embedding ties the tow cable off at the turret.");
                Assert.That(entities.HasComponent<RopeAttachPointComponent>(harpoon), Is.True);
            });

            Assert.That(rope.TryGetBody(turret, out var bodyA, out _), Is.True);
            Assert.That(rope.TryGetBody(harpoon, out var bodyB, out _), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(bodyA, Is.EqualTo(gridA), "The near end pulls on the firing hull.");
                Assert.That(bodyB, Is.EqualTo(gridB), "The far end pulls on the struck hull.");
            });
        });

        // Reeling in shortens the cable.
        float before = 0f, after = 0f;
        await server.WaitPost(() =>
        {
            var turretComp = entities.GetComponent<ShipHarpoonTurretComponent>(turret);
            before = entities.GetComponent<RopeComponent>(turretComp.Rope!.Value).Length;
            Assert.That(entities.HasComponent<MannedTurretOperatorComponent>(user), Is.True,
                "Firing must not throw the operator out of the seat.");
            entities.EventBus.RaiseLocalEvent(user, (object) new HarpoonReelInActionEvent(), true);
            Assert.That(entities.GetComponent<ShipHarpoonTurretComponent>(turret).Reeling, Is.EqualTo(1),
                "The reel control starts the winch.");
        });

        await server.WaitRunTicks(20);
        await server.WaitAssertion(() =>
        {
            var turretComp = entities.GetComponent<ShipHarpoonTurretComponent>(turret);
            Assert.That(turretComp.Rope, Is.Not.Null);
            after = entities.GetComponent<RopeComponent>(turretComp.Rope!.Value).Length;
            Assert.That(after, Is.LessThan(before), "The winch takes the cable in.");

            // Releasing cuts the cable at the turret and leaves the harpoon where it is.
            entities.EventBus.RaiseLocalEvent(user, (object) new HarpoonReleaseActionEvent(), true);
            Assert.Multiple(() =>
            {
                Assert.That(entities.GetComponent<RopeAttachPointComponent>(turret).Ropes, Is.Empty);
                Assert.That(entities.GetComponent<ShipHarpoonTurretComponent>(turret).Rope, Is.Null);
            });

            entities.DeleteEntity(user);
            entities.DeleteEntity(gridA);
            entities.DeleteEntity(gridB);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ShallowShotGlancesOff()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var maps = server.ResolveDependency<IMapManager>();

        EntityUid gridA = default, gridB = default, turret = default, user = default, harpoon = default;

        await server.WaitPost(() =>
        {
            entities.DeleteEntity(map.Grid);
            gridA = MakeGrid(entities, maps, map.MapId, Vector2.Zero, 3);
            // A long flat wall, so the shallow shot cannot sneak into a face it happens to meet square on.
            gridB = MakeGrid(entities, maps, map.MapId, new Vector2(10f, 0f), 1, 20);
            for (var y = 0; y < 20; y++)
            {
                entities.SpawnEntity(Wall, new EntityCoordinates(gridB, new Vector2(0.5f, y + 0.5f)));
            }

            (turret, user) = MakeTurret(entities, gridA);
        });

        await server.WaitRunTicks(5);
        await server.WaitPost(() =>
        {
            Man(entities, user, turret);
            // 57 degrees off the wall's normal: inside the turret's arc, well outside what the barbs can bite.
            Fire(entities, user, turret, new MapCoordinates(new Vector2(10.5f, 14f), map.MapId));
            harpoon = entities.GetEntity(entities.GetComponent<ShipHarpoonTurretComponent>(turret).Harpoon!.Value);
        });

        await server.WaitRunTicks(40);
        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(entities.GetComponent<ShipHarpoonComponent>(harpoon).Embedded, Is.False,
                    "A shot that comes in at a shallow angle skips off.");
                Assert.That(entities.HasComponent<EmbeddableProjectileComponent>(harpoon), Is.False);
                Assert.That(entities.GetComponent<RopeAttachPointComponent>(turret).Ropes, Is.Empty,
                    "A glancing hit leaves no cable behind.");
            });

            entities.DeleteEntity(user);
            entities.DeleteEntity(gridA);
            entities.DeleteEntity(gridB);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A turret bolted to the east edge of a hull, facing east, with an operator standing on it.</summary>
    private static (EntityUid Turret, EntityUid User) MakeTurret(IEntityManager entities, EntityUid grid)
    {
        var coordinates = new EntityCoordinates(grid, new Vector2(2.5f, 1.5f));
        var turret = entities.SpawnEntity("WFTestHarpoonTurret", coordinates);
        var comp = entities.GetComponent<ShipHarpoonTurretComponent>(turret);
        comp.MountRotation = new Vector2(1f, 0f).ToWorldAngle();
        return (turret, entities.SpawnEntity(Operator, coordinates));
    }

    private static void Man(IEntityManager entities, EntityUid user, EntityUid turret)
    {
        Assert.That(entities.System<SharedBuckleSystem>().TryBuckle(user, null, turret), Is.True);
        Assert.That(entities.GetComponent<ShipHarpoonTurretComponent>(turret).Operator,
            Is.EqualTo(entities.GetNetEntity(user)));
    }

    private static void Fire(IEntityManager entities, EntityUid user, EntityUid turret, MapCoordinates target)
    {
        var gun = entities.GetComponent<GunComponent>(turret);
        var transform = entities.System<SharedTransformSystem>();
        entities.System<SharedGunSystem>().AttemptShoot(user, turret, gun, transform.ToCoordinates(target));
        Assert.That(entities.GetComponent<ShipHarpoonTurretComponent>(turret).Harpoon, Is.Not.Null,
            "The turret must actually have loosed a harpoon.");
    }

    /// <summary>A free-floating rectangular hull.</summary>
    private static EntityUid MakeGrid(IEntityManager entities, IMapManager maps, MapId map, Vector2 position, int width, int height = 0)
    {
        var mapSystem = entities.System<SharedMapSystem>();
        var physics = entities.System<SharedPhysicsSystem>();
        var transform = entities.System<SharedTransformSystem>();

        var grid = maps.CreateGridEntity(map);
        var tiles = new List<(Vector2i GridIndices, Tile Tile)>();
        for (var x = 0; x < width; x++)
        {
            for (var y = 0; y < (height == 0 ? width : height); y++)
            {
                tiles.Add((new Vector2i(x, y), new Tile(1)));
            }
        }

        mapSystem.SetTiles(grid.Owner, grid.Comp, tiles);
        physics.SetBodyType(grid.Owner, BodyType.Dynamic);
        transform.SetWorldPosition(grid.Owner, position);
        return grid.Owner;
    }
}
