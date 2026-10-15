using Content.Server._Forge.KIAS;
using Content.Server.Power.Components;
using Content.Shared._Forge.KIAS;
using Content.Shared.Damage;
using Content.Shared.Projectiles;
using Content.Shared._Crescent.ShipShields;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Events;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._Forge.KIAS;

[TestFixture]
public sealed class KiasHullTests
{
    [Test]
    public async Task NativeProjectileHullHitAndShieldAbsorptionStayDistinct()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        await pair.Server.WaitAssertion(() =>
        {
            for (var x = 0; x < 6; x++)
            {
                em.System<SharedMapSystem>().SetTile(map.Grid, map.Grid.Comp, new Vector2i(x, 0), map.Tile.Tile);
                em.SpawnEntity("KiasDataCable", new EntityCoordinates(map.Grid, x + 0.5f, 0.5f));
            }
            EntityUid Spawn(string prototype, int x)
            {
                var uid = em.SpawnEntity(prototype, new EntityCoordinates(map.Grid, x + 0.5f, 0.5f));
                em.RemoveComponent<ApcPowerReceiverComponent>(uid);
                return uid;
            }
            Spawn("KiasCore", 0);
            Spawn("KiasDefenceServer", 1);
            Spawn("KiasHullSensor", 2);
            var recorder = Spawn("KiasRecorder", 3);
            var wall = Spawn("WallSolid", 5);
            em.System<KiasSystem>().Rebuild(map.Grid);
            var shield = em.SpawnEntity("ShipShield", new EntityCoordinates(map.Grid, 4.5f, 0.5f));
            var source = em.SpawnEntity(null, new EntityCoordinates(map.Grid, 4.5f, 0.5f));
            em.AddComponent<ApcPowerReceiverComponent>(source).NeedsPower = false;
            var emitter = em.AddComponent<ShipShieldEmitterComponent>(source);
            emitter.ShieldProjectileAbsorptionFraction = 1;
            emitter.ShieldPassthroughFromStress = 0;
            emitter.ShieldHitDamageCap = 0;
            em.GetComponent<ShipShieldComponent>(shield).Source = source;
            var intercepted = em.SpawnEntity("20mmBullet", new EntityCoordinates(map.Grid, 4.5f, 0.5f));
            var ev = new PreventCollideEvent(shield, intercepted, em.EnsureComponent<PhysicsComponent>(shield),
                em.GetComponent<PhysicsComponent>(intercepted), default!, default!);
            em.EventBus.RaiseLocalEvent(shield, ref ev);
            Assert.That(em.GetComponent<ProjectileComponent>(intercepted).ProjectileSpent, Is.True);
            Assert.That(em.GetComponent<KiasRecorderComponent>(recorder).Entries, Is.Empty);
            em.DeleteEntity(shield);
            em.DeleteEntity(source);
            var round = em.SpawnEntity("20mmBullet", new EntityCoordinates(map.Grid, 4.5f, 0.5f));
            var projectile = em.GetComponent<ProjectileComponent>(round);
            projectile.IgnoreResistances = true;
            var damage = em.System<SharedProjectileSystem>().ProjectileCollide((round, projectile, em.GetComponent<PhysicsComponent>(round)), wall);
            Assert.That(damage!.AnyPositive(), Is.True);
        });
        await pair.RunTicksSync(70);
        await pair.Server.WaitAssertion(() =>
        {
            var log = em.GetComponent<KiasGridComponent>(map.Grid).Log;
            Assert.That(log.Count, Is.EqualTo(1), "Only the physical hull hit may reach the hull sensor.");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task DamageAggregationCollisionDebounceAndHardOff()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var server = pair.Server;
        var em = server.ResolveDependency<IEntityManager>();
        var kias = em.System<KiasSystem>();
        EntityUid core = default;
        EntityUid wall = default;
        await server.WaitAssertion(() =>
        {
            var maps = em.System<SharedMapSystem>();
            for (var x = 0; x < 8; x++)
            {
                maps.SetTile(map.Grid, map.Grid.Comp, new Vector2i(x, 0), map.Tile.Tile);
                em.SpawnEntity("KiasDataCable", new EntityCoordinates(map.Grid, x + 0.5f, 0.5f));
            }
            EntityUid Spawn(string prototype, int x)
            {
                var uid = em.SpawnEntity(prototype, new EntityCoordinates(map.Grid, x + 0.5f, 0.5f));
                em.RemoveComponent<ApcPowerReceiverComponent>(uid);
                return uid;
            }
            core = Spawn("KiasCore", 0);
            Spawn("KiasDefenceServer", 1);
            Spawn("KiasRecorder", 2);
            Spawn("KiasHullSensor", 3);
            Spawn("KiasIntegrityMonitor", 4);
            Spawn("KiasCollisionMonitor", 5);
            wall = em.SpawnEntity("WallSolid", new EntityCoordinates(map.Grid, 6.5f, 0.5f));
            kias.Rebuild(map.Grid);
            var minor = new KiasGridCollisionEvent(map.Grid, core, 0.2f);
            em.EventBus.RaiseLocalEvent(map.Grid, ref minor, true);
            Assert.That(em.GetComponent<KiasGridComponent>(map.Grid).Log.Count, Is.Zero);
            var collision = new KiasGridCollisionEvent(map.Grid, core, 10);
            em.EventBus.RaiseLocalEvent(map.Grid, ref collision, true);
            em.EventBus.RaiseLocalEvent(map.Grid, ref collision, true);
            Assert.That(em.GetComponent<KiasGridComponent>(map.Grid).Log.Count, Is.EqualTo(1));
            var damage = new DamageSpecifier();
            damage.DamageDict.Add("Blunt", 5);
            Assert.That(em.HasComponent<KiasHullStructureComponent>(wall), Is.True);
            Assert.That(em.System<DamageableSystem>().TryChangeDamage(wall, damage, ignoreResistances: true)!.AnyPositive(), Is.True);
            em.System<DamageableSystem>().TryChangeDamage(wall, damage, ignoreResistances: true);
            var impact = new KiasHullImpactEvent(map.Grid, wall);
            em.EventBus.RaiseLocalEvent(map.Grid, ref impact, true);
            em.EventBus.RaiseLocalEvent(map.Grid, ref impact, true);
        });
        await pair.RunTicksSync(70);
        await server.WaitAssertion(() =>
        {
            var log = em.GetComponent<KiasGridComponent>(map.Grid).Log;
            Assert.That(log.Count, Is.EqualTo(3));
            var damage = new KiasHullDamageEvent(map.Grid, wall, 20);
            em.EventBus.RaiseLocalEvent(map.Grid, ref damage, true);
            kias.SetEnabled(core, false);
        });
        await pair.RunTicksSync(70);
        await server.WaitAssertion(() =>
        {
            Assert.That(em.GetComponent<KiasGridComponent>(map.Grid).Log.Count, Is.EqualTo(4));
            kias.SetEnabled(core, true);
            var collision = new KiasGridCollisionEvent(map.Grid, core, 10);
            em.EventBus.RaiseLocalEvent(map.Grid, ref collision, true);
            Assert.That(em.GetComponent<KiasGridComponent>(map.Grid).Log.Count, Is.EqualTo(6));
        });
        await pair.CleanReturnAsync();
    }
}
