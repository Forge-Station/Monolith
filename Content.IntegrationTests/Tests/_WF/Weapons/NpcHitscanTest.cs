#nullable enable
using Content.Server.NPC.Components;
using Content.Shared.Damage;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.IntegrationTests.Tests._WF.Weapons;

/// <summary>A laser fired by an NPC, the way its ranged combat fires it, hurts the player it's aimed at.</summary>
[TestFixture]
public sealed class NpcHitscanTest
{
    [Test]
    public async Task AnNpcLaserHurtsThePlayer()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        EntityUid shooter = default, player = default, gun = default;
        await server.WaitPost(() =>
        {
            var maps = server.System<SharedMapSystem>();
            var plating = server.ResolveDependency<ITileDefinitionManager>()["Plating"].TileId;
            for (var x = -2; x <= 8; x++)
            for (var y = -2; y <= 2; y++)
                maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, y), new Tile(plating));
            shooter = entMan.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid.Owner, 0.5f, 0.5f));
            player = entMan.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid.Owner, 5.5f, 0.5f));
            server.PlayerMan.SetAttachedEntity(pair.Player!, player);
            gun = entMan.SpawnEntity("WeaponLaserCarbine", new EntityCoordinates(map.Grid.Owner, 0.5f, 0.5f));
            Assert.That(server.System<SharedHandsSystem>().TryPickupAnyHand(shooter, gun), Is.True);
        });
        await server.WaitRunTicks(5);
        // Fired the way NPC ranged combat fires it, again each half second until it lands: a gun just picked up may not
        // be ready on the first try.
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var hurt = false;
            await server.WaitPost(() =>
            {
                hurt = entMan.GetComponent<DamageableComponent>(player).TotalDamage.Float() > 0f;
                if (hurt)
                    return;
                var guns = server.System<SharedGunSystem>();
                var comp = entMan.GetComponent<GunComponent>(gun);
                guns.SetTarget(comp, player);
                guns.AttemptShoot(shooter, gun, comp, new EntityCoordinates(map.Grid.Owner, 5.5f, 0.5f));
            });
            if (hurt)
                break;
            await pair.RunSeconds(0.5f);
        }
        await server.WaitAssertion(() =>
            Assert.That(entMan.GetComponent<DamageableComponent>(player).TotalDamage.Float(), Is.GreaterThan(0f), "The laser didn't hurt the player."));
        await pair.CleanReturnAsync();
    }

    /// <summary>A beam lands the instant it's fired, so the NPC mustn't lead a running player as it would with a bullet.</summary>
    [Test]
    public async Task AnNpcLaserHitsAMovingPlayer()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        EntityUid shooter = default, player = default;
        var spot = new EntityCoordinates(map.Grid.Owner, 6.5f, 0.5f);
        await server.WaitPost(() =>
        {
            var maps = server.System<SharedMapSystem>();
            var plating = server.ResolveDependency<ITileDefinitionManager>()["Plating"].TileId;
            for (var x = -2; x <= 10; x++)
            for (var y = -4; y <= 4; y++)
                maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, y), new Tile(plating));
            shooter = entMan.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid.Owner, 0.5f, 0.5f));
            player = entMan.SpawnEntity("MobHuman", spot);
            server.PlayerMan.SetAttachedEntity(pair.Player!, player);
            var gun = entMan.SpawnEntity("WeaponLaserCarbine", new EntityCoordinates(map.Grid.Owner, 0.5f, 0.5f));
            Assert.That(server.System<SharedHandsSystem>().TryPickupAnyHand(shooter, gun), Is.True);
            entMan.AddComponent(shooter, new NPCRangedCombatComponent { Target = player });
        });

        var speed = 0f;
        // Running across the line of fire: put back on the spot each tick, still moving.
        for (var i = 0; i < 90; i++)
        {
            await server.WaitPost(() =>
            {
                server.System<SharedTransformSystem>().SetCoordinates(player, spot);
                server.System<SharedPhysicsSystem>().SetLinearVelocity(player, new System.Numerics.Vector2(0f, 6f));
            });
            await server.WaitRunTicks(1);
            await server.WaitPost(() => speed += entMan.GetComponent<PhysicsComponent>(player).LinearVelocity.Length() / 90f);
        }

        await server.WaitAssertion(() =>
        {
            Assert.That(speed, Is.GreaterThan(3f), "The player wasn't really moving.");
            Assert.That(entMan.GetComponent<DamageableComponent>(player).TotalDamage.Float(), Is.GreaterThan(0f),
                "The NPC's laser never hit a moving player.");
        });
        await pair.CleanReturnAsync();
    }
}
