#nullable enable
using Content.IntegrationTests.Pair;
using Content.Server.Power.Components;
using Content.Shared._WF.Xenonids.Stab;
using Content.Shared.CombatMode;
using Content.Shared.Damage;
using Content.Shared.Doors.Components;
using Content.Shared.Doors.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Weapons.Melee;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Timing;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._WF.Xenonids;

/// <summary>Xeno claws and tails damage station structures and humans by RMC's rules.</summary>
[TestFixture]
public sealed class XenoMeleeDamageTest
{
    /// <summary>Swings once with a fresh xeno at a fresh target and returns the target's damage.</summary>
    private static async Task<FixedPoint2> Swing(TestPair pair, TestMapData map, string xenoProto, string targetProto, bool bolt = false, bool tail = false)
    {
        var server = pair.Server;
        var entities = server.EntMan;
        EntityUid xeno = default, target = default;

        await server.WaitPost(() =>
        {
            var maps = entities.System<SharedMapSystem>();
            for (var x = -1; x <= 3; x++)
            for (var y = -1; y <= 1; y++)
                maps.SetTile(map.Grid, new Vector2i(x, y), map.Tile.Tile);

            target = entities.SpawnEntity(targetProto, new EntityCoordinates(map.Grid, 1.5f, 0.5f));
            xeno = entities.SpawnEntity(xenoProto, new EntityCoordinates(map.Grid, 0.5f, 0.5f));
            if (bolt)
            {
                // bolting needs power; without a receiver the door counts as powered
                entities.RemoveComponent<ApcPowerReceiverComponent>(target);
                var bolts = entities.GetComponent<DoorBoltComponent>(target);
                entities.System<SharedDoorSystem>().SetBoltsDown((target, bolts), true);
                Assert.That(bolts.BoltsDown);
            }
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            entities.System<SharedCombatModeSystem>().SetInCombatMode(xeno, true);
            if (tail)
            {
                var ev = new XenoTailStabEvent
                {
                    Performer = xeno,
                    Entity = target,
                    Coords = entities.GetComponent<TransformComponent>(target).Coordinates,
                };
                entities.EventBus.RaiseLocalEvent(xeno, ev);
                Assert.That(ev.Handled, "The tail stab should hit.");
                return;
            }

            var melee = entities.System<SharedMeleeWeaponSystem>();
            var weapon = entities.GetComponent<MeleeWeaponComponent>(xeno);
            weapon.NextAttack = server.ResolveDependency<IGameTiming>().CurTime;
            Assert.That(melee.AttemptLightAttack(xeno, xeno, weapon, target), $"{xenoProto} should swing at {targetProto}.");
        });
        await pair.RunTicksSync(5);

        var damage = FixedPoint2.Zero;
        await server.WaitAssertion(() =>
        {
            damage = entities.GetComponent<DamageableComponent>(target).TotalDamage;
            entities.DeleteEntity(xeno);
            entities.DeleteEntity(target);
        });
        return damage;
    }

    /// <summary>Claws hurt machines and humans, tails hurt humans, sharp claws break airlocks and walls, and a drone's can't.</summary>
    [Test]
    public async Task ClawsDamageByRmcRules()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();

        Assert.That(await Swing(pair, map, "CMXenoDrone", "Autolathe"), Is.GreaterThan(FixedPoint2.New(20)),
            "A drone's 22.5 brute should get past a machine's flat reductions.");
        Assert.That(await Swing(pair, map, "CMXenoDrone", "MobHuman", tail: true), Is.GreaterThan(FixedPoint2.Zero),
            "A drone's tail stab should hurt a human; like RMC's, it only hits mobs and barricades.");
        Assert.That(await Swing(pair, map, "CMXenoDrone", "MobHuman"), Is.GreaterThan(FixedPoint2.Zero),
            "A drone's slash should hurt a human.");

        Assert.That(await Swing(pair, map, "CMXenoWarrior", "Airlock", bolt: true), Is.EqualTo(FixedPoint2.New(50)),
            "Sharp claws take a tenth of a bolted airlock's 500 health.");
        Assert.That(await Swing(pair, map, "CMXenoDrone", "Airlock", bolt: true), Is.EqualTo(FixedPoint2.Zero),
            "A drone's normal claws can't break a bolted airlock.");
        Assert.That(await Swing(pair, map, "CMXenoWarrior", "Airlock"), Is.EqualTo(FixedPoint2.New(50)),
            "Sharp claws break an unbolted airlock at the bolted rate.");
        Assert.That(await Swing(pair, map, "CMXenoQueen", "Airlock"), Is.EqualTo(FixedPoint2.New(62.5)),
            "Very sharp claws break an airlock in eight hits.");
        Assert.That(await Swing(pair, map, "CMXenoQueen", "Airlock", bolt: true), Is.EqualTo(FixedPoint2.New(62.5)),
            "Bolts don't slow very sharp claws.");
        Assert.That(await Swing(pair, map, "CMXenoDrone", "Airlock"), Is.EqualTo(FixedPoint2.Zero),
            "A drone's normal claws can't break an unbolted airlock either.");
        Assert.That(await Swing(pair, map, "MobHuman", "Airlock"), Is.EqualTo(FixedPoint2.Zero),
            "A human's fists still bounce off an airlock's flat reductions.");

        Assert.That(await Swing(pair, map, "CMXenoQueen", "WallSolid"), Is.EqualTo(FixedPoint2.New(65)),
            "Very sharp claws take a twentieth of a wall's 1300 health.");
        Assert.That(await Swing(pair, map, "CMXenoDrone", "WallSolid"), Is.EqualTo(FixedPoint2.Zero),
            "A drone's normal claws can't break a wall.");

        await pair.CleanReturnAsync();
    }
}
