using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Shared.CombatMode;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Interaction;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.Planets;

/// <summary>A carcinoma tendril growing inside a hull wall can still be clicked, swung at and shot.</summary>
[TestFixture]
public sealed class CarcinomaTendrilAttackTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: WFTendrilTestBullet
  parent: BaseBullet

- type: entity
  id: WFTendrilTestCartridge
  components:
  - type: CartridgeAmmo
    proto: WFTendrilTestBullet
    deleteOnSpawn: true

- type: entity
  id: WFTendrilTestGun
  components:
  - type: Gun
    minAngle: 0
    maxAngle: 0
    selectedMode: SemiAuto
    availableModes:
    - SemiAuto
  - type: BallisticAmmoProvider
    proto: WFTendrilTestCartridge
    capacity: 5
";

    [Test]
    public async Task TendrilInAWallCanBeAttacked()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        var edge = new EntityCoordinates(map.Grid, 1.5f, 0.5f);
        EntityUid tendril = default, inside = default, outside = default;
        await server.WaitPost(() =>
        {
            var maps = em.System<SharedMapSystem>();
            for (var x = -1; x <= 8; x++)
            for (var y = -1; y <= 1; y++)
                maps.SetTile(map.Grid, new Vector2i(x, y), map.Tile.Tile);
            em.SpawnEntity("WallSolid", edge);
            tendril = em.SpawnEntity("WFCarcinomaHullTendril", edge);
            inside = em.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid, 0.5f, 0.5f));
            outside = em.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid, 2.5f, 0.5f));
        });
        await pair.RunTicksSync(5);

        FixedPoint2 Damage() => em.GetComponent<DamageableComponent>(tendril).TotalDamage;
        var before = FixedPoint2.Zero;
        await server.WaitAssertion(() =>
        {
            var interaction = em.System<SharedInteractionSystem>();
            var melee = em.System<SharedMeleeWeaponSystem>();
            var xforms = em.System<SharedTransformSystem>();
            foreach (var (user, facing) in new[] { (inside, new Vector2(1f, 0f)), (outside, new Vector2(-1f, 0f)) })
            {
                Assert.That(interaction.InRangeUnobstructed(user, tendril, 1.5f, overlapCheck: false), "The wall on its tile blocks a click attack.");
                var swept = melee.ArcRayCast(xforms.GetWorldPosition(user), facing.ToWorldAngle(), Angle.FromDegrees(60), 1.5f, map.MapId, user);
                Assert.That(swept, Does.Contain(tendril), "A wide swing misses the tendril.");
            }

            em.System<SharedCombatModeSystem>().SetInCombatMode(inside, true);
            melee.AttemptLightAttack(inside, inside, em.GetComponent<MeleeWeaponComponent>(inside), tendril);
        });
        await pair.RunTicksSync(2);
        await server.WaitAssertion(() =>
        {
            Assert.That(Damage(), Is.GreaterThan(FixedPoint2.Zero), "A click attack from inside the hull did no damage.");
            before = Damage();
            var melee = em.System<SharedMeleeWeaponSystem>();
            var swept = melee.ArcRayCast(em.System<SharedTransformSystem>().GetWorldPosition(outside), new Vector2(-1f, 0f).ToWorldAngle(),
                Angle.FromDegrees(60), 1.5f, map.MapId, outside);
            em.System<SharedCombatModeSystem>().SetInCombatMode(outside, true);
            melee.AttemptHeavyAttack(outside, outside, em.GetComponent<MeleeWeaponComponent>(outside), swept.ToList(), edge);
        });
        await pair.RunTicksSync(2);

        EntityUid gunner = default, aimed = default, stray = default;
        await server.WaitAssertion(() =>
        {
            Assert.That(Damage(), Is.GreaterThan(before), "A wide swing from outside the hull did no damage.");
            em.DeleteEntity(outside);
            var far = new EntityCoordinates(map.Grid, 7.5f, 0.5f);
            gunner = em.SpawnEntity("MobHuman", far);
            aimed = em.SpawnEntity("WFTendrilTestGun", far);
            stray = em.SpawnEntity("WFTendrilTestGun", far);
        });
        await pair.RunTicksSync(5);
        await server.WaitPost(() =>
        {
            before = Damage();
            em.System<SharedGunSystem>().AttemptShoot(gunner, aimed, em.GetComponent<GunComponent>(aimed), edge, tendril);
        });
        await pair.RunTicksSync(30);
        await server.WaitAssertion(() =>
        {
            Assert.That(Damage(), Is.GreaterThan(before), "An aimed shot missed the tendril.");
            before = Damage();
            em.System<SharedGunSystem>().AttemptShoot(gunner, stray, em.GetComponent<GunComponent>(stray), edge);
        });
        await pair.RunTicksSync(1);
        await server.WaitAssertion(() =>
            Assert.That(em.EntityQuery<ProjectileComponent>().Count(), Is.EqualTo(1), "The stray gun did not fire."));
        await pair.RunTicksSync(30);
        await server.WaitAssertion(() =>
        {
            Assert.That(Damage(), Is.EqualTo(before), "A stray shot hit the tendril.");
            Assert.That(em.EntityQuery<ProjectileComponent>().Count(), Is.Zero, "The stray shot never struck the wall.");
        });
        await pair.CleanReturnAsync();
    }
}
