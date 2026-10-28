using System.Linq;
using System.Numerics;
using Content.Server._WF.Xenobiology;
using Content.Server.Body.Systems;
using Content.Shared._WF.Xenobiology.Bounties;
using Content.Shared._WF.Xenobiology.Components;
using Content.Shared._WF.Xenobiology.Systems;
using Content.Shared.Chemistry;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reaction;
using Content.Shared.Climbing.Events;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;
using Content.Shared.Power.EntitySystems;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Xenobiology;

/// <summary>
/// Goob xenobiology on Wolfgate: breeding, extract reactions, grinding, latching and bounties.
/// </summary>
[TestFixture]
public sealed class XenobiologyTest
{
    private const string GreyPending = "MobSlimeXenobioBabyGrey";

    private static EntityUid SpawnGrey(IEntityManager entities, Robust.Shared.Map.EntityCoordinates coords)
    {
        var before = entities.EntityQuery<SlimeComponent>().Select(s => s.Owner).ToHashSet();
        var marker = entities.SpawnEntity(GreyPending, coords);
        entities.System<XenobiologySystem>().SpawnPending((marker, entities.GetComponent<PendingSlimeSpawnComponent>(marker)));
        return entities.EntityQuery<SlimeComponent>().Select(s => s.Owner).Single(uid => !before.Contains(uid));
    }

    [Test]
    public async Task SlimesGrowAndSplitIntoTheirBreeds()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var protos = server.ResolveDependency<IPrototypeManager>();

        EntityUid slime = default;
        await server.WaitPost(() =>
        {
            slime = SpawnGrey(entities, map.GridCoords);
            var comp = entities.GetComponent<SlimeComponent>(slime);
            Assert.Multiple(() =>
            {
                Assert.That(comp.Breed.Id, Is.EqualTo("GreyMutation"));
                Assert.That(entities.GetComponent<MetaDataComponent>(slime).EntityName, Does.Contain("grey slime"));
                Assert.That(entities.GetComponent<MobGrowthComponent>(slime).IsFirstStage, Is.True);
            });

            entities.System<HungerSystem>().SetHunger(slime, 100f);
        });

        await server.WaitRunTicks(120);
        await server.WaitAssertion(() =>
        {
            var growth = entities.GetComponent<MobGrowthComponent>(slime);
            Assert.That(growth.CurrentStage, Is.EqualTo("adult"), "A fed baby grows into an adult.");

            var comp = entities.GetComponent<SlimeComponent>(slime);
            comp.MutationChance = 1f;
            var mutations = comp.PotentialMutations.ToHashSet();
            entities.System<XenobiologySystem>().DoMitosis((slime, comp));

            var children = entities.EntityQuery<SlimeComponent>().Where(s => s.Owner != slime).ToList();
            Assert.That(children, Is.Not.Empty);
            Assert.That(children.Select(c => c.Breed), Is.All.Matches<ProtoId<Content.Shared._WF.Xenobiology.BreedPrototype>>(b => mutations.Contains(b)),
                "At full mutation chance every child is one of the parent's mutations.");

            foreach (var child in children)
            {
                Assert.That(protos.Index(child.Breed).BreedName, Is.EqualTo(entities.GetComponent<MobGrowthComponent>(child.Owner).BaseEntityName));
                entities.DeleteEntity(child.Owner);
            }
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ExtractsReactOnceToTheirReagents()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();

        EntityUid reagentExtract = default, spawnExtract = default;
        await server.WaitPost(() =>
        {
            var reactive = entities.System<ReactiveSystem>();
            reagentExtract = entities.SpawnEntity("GreySlimeExtract", map.GridCoords);
            spawnExtract = entities.SpawnEntity("GreySlimeExtract", map.GridCoords);

            reactive.DoEntityReaction(reagentExtract, new Solution("Water", FixedPoint2.New(5)), ReactionMethod.Injection);
            reactive.DoEntityReaction(spawnExtract, new Solution("Blood", FixedPoint2.New(5)), ReactionMethod.Injection);
            // A spent extract no longer reacts.
            reactive.DoEntityReaction(reagentExtract, new Solution("Blood", FixedPoint2.New(5)), ReactionMethod.Injection);
        });

        await server.WaitRunTicks(2);
        await server.WaitAssertion(() =>
        {
            var solutions = entities.System<SharedSolutionContainerSystem>();
            Assert.That(solutions.TryGetSolution(reagentExtract, "extract", out _, out var solution), Is.True);
            var cubes = entities.EntityQuery<MetaDataComponent>().Count(m => m.EntityPrototype?.ID == "MonkeyCube");

            Assert.Multiple(() =>
            {
                Assert.That(solution!.GetTotalPrototypeQuantity("Epinephrine"), Is.EqualTo(FixedPoint2.New(15)),
                    "Water makes the grey extract fill with epinephrine.");
                Assert.That(entities.GetComponent<SlimeExtractComponent>(reagentExtract).Spent, Is.True);
                Assert.That(entities.HasComponent<ReactiveComponent>(reagentExtract), Is.False);
                Assert.That(entities.Deleted(spawnExtract) || entities.IsQueuedForDeletion(spawnExtract), Is.True,
                    "Blood makes monkey cubes and uses the extract up.");
                Assert.That(cubes, Is.EqualTo(3), "Three monkey cubes from the blood, none from the spent extract.");
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task GrinderTurnsDeadSlimesIntoExtracts()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();

        EntityUid grinder = default;
        var produced = 0;
        await server.WaitPost(() =>
        {
            grinder = entities.SpawnEntity("SlimeGrinder", map.GridCoords);
            // No power net on the test map; a grinder without a receiver always runs.
            entities.RemoveComponent<Content.Server.Power.Components.ApcPowerReceiverComponent>(grinder);
        });

        await server.WaitRunTicks(5);
        await server.WaitPost(() =>
        {
            Assert.That(entities.GetComponent<TransformComponent>(grinder).Anchored, Is.True);
            var slime = SpawnGrey(entities, map.GridCoords);
            produced = entities.GetComponent<SlimeComponent>(slime).ExtractsProduced;
            var damage = new DamageSpecifier { DamageDict = { { "Blunt", 200 } } };
            entities.System<DamageableSystem>().TryChangeDamage(slime, damage, true);

            Assert.That(entities.System<Content.Shared.Mobs.Systems.MobStateSystem>().IsDead(slime), Is.True, "The slime is dead.");
            var ev = new ClimbedOnEvent(slime, slime);
            entities.EventBus.RaiseLocalEvent(grinder, ref ev);
            Assert.That(entities.IsQueuedForDeletion(slime) || entities.Deleted(slime), Is.True, "The grinder takes the slime.");
        });

        await server.WaitRunTicks(300);
        await server.WaitAssertion(() =>
        {
            var extracts = entities.EntityQuery<SlimeExtractComponent>()
                .Count(e => entities.GetComponent<MetaDataComponent>(e.Owner).EntityPrototype?.ID == "GreySlimeExtract");
            Assert.That(extracts, Is.EqualTo(produced), "The grinder makes the slime's extract count of its breed's extract.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task LatchedSlimesFeedOnTheirTarget()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();

        EntityUid slime = default, target = default;
        var startHunger = 0f;
        await server.WaitPost(() =>
        {
            slime = SpawnGrey(entities, map.GridCoords);
            target = entities.SpawnEntity("MobMonkey", map.GridCoords.Offset(new Vector2(0.5f, 0)));
            entities.System<SlimeLatchSystem>().Latch((slime, entities.GetComponent<SlimeComponent>(slime)), target);
            startHunger = entities.System<HungerSystem>().GetHunger(entities.GetComponent<HungerComponent>(slime));
        });

        await server.WaitRunTicks(120);
        await server.WaitAssertion(() =>
        {
            var hunger = entities.System<HungerSystem>().GetHunger(entities.GetComponent<HungerComponent>(slime));
            Assert.Multiple(() =>
            {
                Assert.That(entities.GetComponent<DamageableComponent>(target).TotalDamage, Is.GreaterThan(FixedPoint2.Zero));
                Assert.That(hunger, Is.GreaterThan(startHunger), "Feeding fills the slime.");
                Assert.That(entities.GetComponent<TransformComponent>(slime).ParentUid, Is.EqualTo(target));
            });

            var comp = entities.GetComponent<SlimeComponent>(slime);
            entities.System<SlimeLatchSystem>().Unlatch((slime, comp));
            Assert.That(comp.LatchedTarget, Is.Null);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task PurpleJellyMakesASlimeYieldMore()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();

        EntityUid slime = default;
        var before = 0;
        await server.WaitPost(() =>
        {
            slime = SpawnGrey(entities, map.GridCoords);
            before = entities.GetComponent<SlimeComponent>(slime).ExtractsProduced;
            Assert.That(entities.System<BloodstreamSystem>().TryAddToChemicals(slime,
                new Solution("XenobioJellyPurple", FixedPoint2.New(2))), Is.True);
        });

        await server.WaitRunTicks(150);
        await server.WaitAssertion(() =>
        {
            Assert.That(entities.GetComponent<SlimeComponent>(slime).ExtractsProduced, Is.GreaterThan(before),
                "The slime metabolises the steroid jelly.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task WolfgateVendorSellsLiveGreySlimes()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var protos = server.ResolveDependency<IPrototypeManager>();

        await server.WaitAssertion(() =>
        {
            var stock = protos.Index<Content.Shared.VendingMachines.VendingMachineInventoryPrototype>("WFWolfgateVendInventory");
            Assert.That(stock.StartingInventory.ContainsKey("MobSlimeXenobioBabyGrey"), Is.True);

            // A vendor spawns the stocked prototype next to itself; that has to leave a live grey slime.
            var slime = SpawnGrey(entities, map.GridCoords);
            Assert.That(entities.GetComponent<SlimeComponent>(slime).Breed.Id, Is.EqualTo("GreyMutation"));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task StarterCrateHoldsItsSlimes()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();

        EntityUid crate = default;
        await server.WaitPost(() => crate = entities.SpawnEntity("WFCrateXenobiologyStarter", map.GridCoords));
        await server.WaitRunTicks(5);
        await server.WaitAssertion(() =>
        {
            var containers = entities.System<Robust.Shared.Containers.SharedContainerSystem>();
            var slimes = entities.EntityQuery<SlimeComponent>().Select(s => s.Owner).ToList();
            Assert.That(slimes, Has.Count.EqualTo(2));
            Assert.That(slimes.All(s => containers.TryGetContainingContainer((s, null), out var c) && c.Owner == crate), Is.True,
                "The starter slimes stay in the crate until it is opened.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task BountyConsoleListsBountiesAndChecksItems()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var protos = server.ResolveDependency<IPrototypeManager>();

        await server.WaitAssertion(() =>
        {
            var console = entities.SpawnEntity("ComputerScienceXenobiologyBounty", map.GridCoords);
            var comp = entities.GetComponent<XenobiologyBountyConsoleComponent>(console);
            Assert.That(comp.Bounties, Has.Count.EqualTo(protos.EnumeratePrototypes<XenobiologyBountyPrototype>().Count()));

            var bag = entities.SpawnEntity("ClothingBackpack", map.GridCoords);
            var storage = entities.System<Content.Shared.Storage.EntitySystems.SharedStorageSystem>();
            for (var i = 0; i < 3; i++)
            {
                storage.Insert(bag, entities.SpawnEntity("GreySlimeExtract", map.GridCoords), out _, playSound: false);
            }

            var any = protos.Index<XenobiologyBountyPrototype>("BountyExtractAnyThree");
            var system = entities.System<XenobiologyBountyConsoleSystem>();
            Assert.That(system.IsBountyComplete(bag, any.Entries, out var handedIn), Is.True);
            Assert.That(handedIn, Has.Count.EqualTo(3));
        });

        await pair.CleanReturnAsync();
    }
}
