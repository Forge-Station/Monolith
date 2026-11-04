#nullable enable
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server._WF.Planets;
using Content.Server._WF.SandMaw;
using Content.Shared._WF.Planets;
using Content.Shared._WF.Tether;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Item;
using Content.Shared.Movement.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.SandMaw;

/// <summary>Merak's sand maw: a tentacle drags you in, slashing it frees you, and a swallowed victim tears their way out.</summary>
[TestFixture]
public sealed class SandMawTest
{
    private static async Task<(EntityUid Maw, EntityUid Victim)> Rig(TestPair pair, float distance)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        var maps = server.System<SharedMapSystem>();
        EntityUid maw = default, victim = default;
        await server.WaitPost(() =>
        {
            var plating = server.ResolveDependency<ITileDefinitionManager>()["Plating"].TileId;
            for (var x = -10; x <= 10; x++)
            for (var y = -10; y <= 10; y++)
                maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, y), new Tile(plating));
            maw = entMan.SpawnEntity("WFMerakSandMaw", new EntityCoordinates(map.Grid.Owner, 0.5f, 0.5f));
            victim = entMan.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid.Owner, 0.5f + distance, 0.5f));
        });
        return (maw, victim);
    }

    [Test]
    public async Task DraggedInSwallowedAndBreakingOut()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var xforms = server.System<SharedTransformSystem>();
        var (maw, victim) = await Rig(pair, 4f);
        await pair.RunSeconds(1);

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<WFSandMawComponent>(maw);
            Assert.That(comp.Grabs.ContainsKey(victim), Is.True, "The maw should have lashed a tentacle round the human.");
            Assert.That(entMan.GetComponent<RopeAttachPointComponent>(victim).Ropes, Has.Count.EqualTo(1));
        });
        await pair.RunSeconds(8);

        EntityUid belly = default;
        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<WFSandMawComponent>(maw);
            Assert.That(comp.Belly, Is.Not.Null, "The human was never reeled in.");
            belly = comp.Belly!.Value;
            Assert.That(entMan.GetComponent<TransformComponent>(victim).GridUid, Is.EqualTo(belly), "The human should be in the belly.");
            Assert.That(entMan.HasComponent<RopeAttachPointComponent>(victim), Is.False, "The tentacle lets go once it has swallowed.");
            var loot = entMan.EntityQuery<ItemComponent>().Count(item => entMan.GetComponent<TransformComponent>(item.Owner).GridUid == belly);
            Assert.That(loot, Is.GreaterThanOrEqualTo(5), "The belly should hold the loot of the fallen.");

            var wall = entMan.EntityQuery<WFSandMawBellyWallComponent>().First(w => w.Maw == maw).Owner;
            var blunt = server.ProtoMan.Index<DamageTypePrototype>("Blunt");
            server.System<DamageableSystem>().TryChangeDamage(wall, new DamageSpecifier(blunt, 200), true);
        });
        await pair.RunTicksSync(5);

        await server.WaitPost(() =>
        {
            var exit = entMan.EntityQuery<WFSandMawExitComponent>().Single(e => e.Maw == maw).Owner;
            xforms.SetCoordinates(victim, entMan.GetComponent<TransformComponent>(exit).Coordinates);
        });
        await pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<TransformComponent>(victim).GridUid, Is.Not.EqualTo(belly), "Stepping into the tear should climb out.");
            var distance = Vector2.Distance(xforms.GetWorldPosition(victim), xforms.GetWorldPosition(maw));
            Assert.That(distance, Is.GreaterThan(entMan.GetComponent<WFSandMawComponent>(maw).GrabRange), "Out past the tentacle's reach.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Swallowed only at the throat, and always with a way out: a pickaxe lies in the belly and its blows, resistances
    /// and all, break a belly wall.
    /// </summary>
    [Test]
    public async Task SwallowedAtTheThroatWithAPickaxeThatBreaksOut()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var xforms = server.System<SharedTransformSystem>();
        var (maw, victim) = await Rig(pair, 4f);

        // Reeled to just outside the old swallow distance, he is still on the rim.
        var swallowRadius = 0f;
        for (var i = 0; i < 200; i++)
        {
            await pair.RunTicksSync(3);
            var near = false;
            await server.WaitPost(() =>
            {
                swallowRadius = entMan.GetComponent<WFSandMawComponent>(maw).SwallowRadius;
                var distance = Vector2.Distance(xforms.GetWorldPosition(victim), xforms.GetWorldPosition(maw));
                near = entMan.GetComponent<WFSandMawComponent>(maw).Belly == null && distance < swallowRadius + 0.6f && distance > swallowRadius + 0.05f;
            });
            if (near)
            {
                await server.WaitAssertion(() =>
                    Assert.That(entMan.GetComponent<TransformComponent>(victim).GridUid, Is.Not.EqualTo(entMan.GetComponent<WFSandMawComponent>(maw).Belly),
                        "Swallowed on the funnel's rim, short of the throat."));
                break;
            }
        }

        await pair.RunSeconds(8);
        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<WFSandMawComponent>(maw);
            Assert.That(comp.Belly, Is.Not.Null, "The human was never swallowed.");
            var belly = comp.Belly!.Value;
            var pickaxe = entMan.EntityQuery<MetaDataComponent>()
                .FirstOrDefault(m => m.EntityPrototype?.ID == "Pickaxe" && entMan.GetComponent<TransformComponent>(m.Owner).GridUid == belly);
            Assert.That(pickaxe, Is.Not.Null, "No pickaxe in the belly.");

            var wall = entMan.EntityQuery<WFSandMawBellyWallComponent>().First(w => w.Maw == maw).Owner;
            var blow = entMan.GetComponent<Content.Shared.Weapons.Melee.MeleeWeaponComponent>(pickaxe!.Owner).Damage;
            var damageable = server.System<DamageableSystem>();
            for (var hit = 0; hit < 20 && !entMan.Deleted(wall) && !entMan.IsQueuedForDeletion(wall); hit++)
                damageable.TryChangeDamage(wall, blow, false, origin: victim);
            Assert.That(entMan.Deleted(wall) || entMan.IsQueuedForDeletion(wall), Is.True, "Twenty pickaxe blows didn't break a belly wall.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TheGripTightensAndHoldsOn()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var xforms = server.System<SharedTransformSystem>();
        var (maw, victim) = await Rig(pair, 5f);
        await pair.RunSeconds(1);

        float held = 0, fresh = 0;
        await server.WaitAssertion(() =>
        {
            held = entMan.GetComponent<WFSandMawComponent>(maw).Grabs[victim].Length;
            fresh = entMan.GetComponent<MovementSpeedModifierComponent>(victim).WalkSpeedModifier;
            Assert.That(fresh, Is.LessThan(1f), "A catch should be slowed.");
            // Running off: the tentacle won't pay out.
            xforms.SetWorldPosition(victim, xforms.GetWorldPosition(maw) + new Vector2(5.9f, 0f));
        });
        await pair.RunTicksSync(2);

        await server.WaitAssertion(() =>
        {
            var distance = Vector2.Distance(xforms.GetWorldPosition(victim), xforms.GetWorldPosition(maw));
            Assert.That(distance, Is.LessThanOrEqualTo(held + 0.05f), "The tentacle should hold its catch at its length.");
        });
        await pair.RunSeconds(1);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<WFSandMawComponent>(maw).Grabs.ContainsKey(victim), Is.True, "Swallowed too soon.");
            Assert.That(entMan.GetComponent<MovementSpeedModifierComponent>(victim).WalkSpeedModifier, Is.LessThan(fresh),
                "The catch should tire the longer it's held.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TheBellyIsUnderTheMaw()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var maps = server.System<SharedMapSystem>();
        var (maw, victim) = await Rig(pair, 3f);
        EntityUid lower = default;
        await server.WaitPost(() =>
        {
            // A ground map with a rock layer under it, as a planet has.
            var ground = entMan.GetComponent<TransformComponent>(maw).MapUid!.Value;
            lower = maps.CreateMap(out _);
            entMan.AddComponent<MapGridComponent>(lower);
            var network = entMan.SpawnEntity(null, MapCoordinates.Nullspace);
            var comp = entMan.AddComponent<WFPlanetNetworkComponent>(network);
            comp.Layers.Add(ground);
            comp.LowerLayers.Add(lower);
            entMan.EnsureComponent<WFPlanetLayerComponent>(ground).Network = entMan.GetNetEntity(network);
        });
        await pair.RunSeconds(8);

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<WFSandMawComponent>(maw);
            Assert.That(comp.Belly, Is.EqualTo(lower), "The belly should be cut out of the layer below.");
            Assert.That(comp.PocketBelly, Is.False);
            var victimXform = entMan.GetComponent<TransformComponent>(victim);
            Assert.That(victimXform.MapUid, Is.EqualTo(lower), "The human should have dropped into it.");
            var under = server.System<SharedTransformSystem>().GetWorldPosition(maw);
            Assert.That(Vector2.Distance(server.System<SharedTransformSystem>().GetWorldPosition(victim), under), Is.LessThan(3f),
                "The belly is right under the maw.");
            var floor = server.ResolveDependency<ITileDefinitionManager>()["FloorFlesh"].TileId;
            Assert.That(maps.GetTileRef(lower, entMan.GetComponent<MapGridComponent>(lower), under.Floored()).Tile.TypeId, Is.EqualTo(floor));

            var wall = entMan.EntityQuery<WFSandMawBellyWallComponent>().First(w => w.Maw == maw).Owner;
            server.System<DamageableSystem>().TryChangeDamage(wall, new DamageSpecifier(server.ProtoMan.Index<DamageTypePrototype>("Blunt"), 200), true);
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.EntityQuery<WFSandMawExitComponent>().Any(e => e.Maw == maw), Is.False,
                "Under the maw, a broken wall opens into the rock, not back up.");
            entMan.DeleteEntity(maw);
            Assert.That(entMan.Deleted(lower), Is.False, "The maw must not take the layer with it.");
            entMan.DeleteEntity(lower);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SlashingTheTentacleLetsGo()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var (maw, victim) = await Rig(pair, 5f);
        await pair.RunSeconds(1);

        await server.WaitAssertion(() =>
        {
            var grip = entMan.GetComponent<WFSandMawComponent>(maw).Grabs[victim].Grip;
            var damage = server.System<DamageableSystem>();
            // Blunt barely marks it; an edge cuts it.
            damage.TryChangeDamage(grip, new DamageSpecifier(server.ProtoMan.Index<DamageTypePrototype>("Blunt"), 20));
            Assert.That(entMan.Deleted(grip) || entMan.IsQueuedForDeletion(grip), Is.False, "A punch shouldn't cut the tentacle.");
            damage.TryChangeDamage(grip, new DamageSpecifier(server.ProtoMan.Index<DamageTypePrototype>("Slash"), 20));
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<WFSandMawComponent>(maw).Grabs.ContainsKey(victim), Is.False, "Slashing should free the human.");
            Assert.That(entMan.HasComponent<RopeAttachPointComponent>(victim), Is.False);
        });

        await pair.CleanReturnAsync();
    }
}
