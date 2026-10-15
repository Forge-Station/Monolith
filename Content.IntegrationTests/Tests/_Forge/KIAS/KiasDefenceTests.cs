using Content.Server._Forge.KIAS;
using Content.Server._Mono.FireControl;
using Content.Server.Power.Components;
using Content.Shared._Forge.KIAS;
using Content.Shared.Destructible;
using Robust.Shared.Spawners;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Systems;

namespace Content.IntegrationTests.Tests._Forge.KIAS;

[TestFixture]
public sealed class KiasDefenceTests
{
    [Test]
    public async Task NativeGunneryFiresAndReservationLocksManualCommand()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        var kias = em.System<KiasSystem>();
        EntityUid core = default;
        EntityUid gun = default;
        EntityUid target = default;
        await pair.Server.WaitAssertion(() =>
        {
            var maps = em.System<SharedMapSystem>();
            for (var x = 0; x < 9; x++)
            for (var y = 0; y < 3; y++)
                maps.SetTile(map.Grid, map.Grid.Comp, new Vector2i(x, y), map.Tile.Tile);
            for (var x = 0; x < 9; x++)
                em.SpawnEntity("KiasDataCable", new EntityCoordinates(map.Grid, x + 0.5f, 0.5f));
            EntityUid Spawn(string prototype, int x)
            {
                var uid = em.SpawnEntity(prototype, new EntityCoordinates(map.Grid, x + 0.5f, 1.5f));
                em.RemoveComponent<ApcPowerReceiverComponent>(uid);
                return uid;
            }
            core = Spawn("KiasCore", 0);
            var defence = Spawn("KiasDefenceServer", 1);
            Spawn("KiasPdcRadar", 2);
            var gcs = Spawn("GunneryServerLow", 3);
            gun = Spawn("WeaponTurretL85Autocannon", 6);
            em.System<FireControlSystem>().ForceServerReconnectionOnGrid(map.Grid);
            em.GetComponent<KiasDefenceComponent>(defence).PdcEnabled = true;
            kias.Rebuild(map.Grid);
            Assert.That(kias.IsOnline(gun), Is.True);
            Assert.That(em.GetComponent<FireControllableComponent>(gun).ControllingServer, Is.EqualTo(gcs));
            target = em.SpawnEntity("20mmBullet", new EntityCoordinates(map.MapUid, 80, 1.5f));
            em.RemoveComponent<TimedDespawnComponent>(target);
            em.System<SharedPhysicsSystem>().SetLinearVelocity(target, new(-20, 0));
        });
        await pair.RunTicksSync(40);
        await pair.Server.WaitAssertion(() =>
        {
            var lastFire = em.GetComponent<GunComponent>(gun).LastFire;
            Assert.That(lastFire, Is.GreaterThan(TimeSpan.Zero));
            Assert.That(em.EntityExists(target), Is.False, "The fired interceptor must destroy the incoming projectile.");
            var defence = em.System<KiasDefenceSystem>();
            var reservation = em.GetComponent<KiasPdcWeaponComponent>(gun);
            reservation.AutomaticGrid = map.Grid;
            reservation.AutomaticUntil = pair.Server.ResolveDependency<Robust.Shared.Timing.IGameTiming>().CurTime + TimeSpan.FromSeconds(1);
            Assert.That(defence.AuthorizeFire(gun, false), Is.False);
            kias.SetEnabled(core, false);
            Assert.That(defence.AuthorizeFire(gun, false), Is.True);
            Assert.That(defence.AuthorizeFire(gun, true), Is.False);
            Assert.That(em.GetComponent<KiasPdcWeaponComponent>(gun).AutomaticGrid, Is.Null);
            kias.SetEnabled(core, false);
            Assert.That(defence.AuthorizeFire(gun, true), Is.False);
            Assert.That(em.GetComponent<KiasPdcWeaponComponent>(gun).AutomaticGrid, Is.Null);
            if (em.EntityExists(target))
                em.DeleteEntity(target);
        });
        await pair.CleanReturnAsync();
    }
}
