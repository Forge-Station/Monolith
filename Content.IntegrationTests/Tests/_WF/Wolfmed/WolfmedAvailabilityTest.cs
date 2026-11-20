#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server.Cargo.Systems;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reaction;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Construction.Components;
using Content.Shared.Construction.NodeEntities;
using Content.Shared.Construction.Prototypes;
using Content.Shared.Interaction;
using Content.Shared.Research.Prototypes;
using Content.Shared.Storage.Components;
using Content.Shared.VendingMachines;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Wolfmed;

/// <summary>
/// W7: everything Wolfmed added has to be obtainable in a normal round. A treatment that exists only in a
/// prototype file is a wound with no cure, so each new item is checked to be vended or printed, each new
/// reagent to have a reaction that makes it, and each fill W7 touched to actually hold what it declares.
/// </summary>
/// <remarks>
/// The spawn half is the second reason this test exists: <c>StorageFill</c> logs an error and drops the
/// entry when a kit is full, and an integration test fails on any error log. Putting the skin graft in the
/// burn kit is therefore checked here rather than discovered in a round - the same overflow is what
/// withdrew PROTO E's tourniquet from <c>MedkitAdvancedFilled</c> in WP12-9, until playtest 5 (ITEMS) widened
/// the medkit grid to 6x2 and put it back.
/// </remarks>
[TestFixture]
public sealed class WolfmedAvailabilityTest : GameTest
{
    /// <summary>
    /// Items Wolfmed added, or relies on, that a player has to be able to get hold of: vended, printed, or inside
    /// something that is (a pill in a vended canister, a drip in a vended flatpack).
    /// </summary>
    private static readonly string[] ObtainableItems =
    [
        "WFWolfmedSkinGraft",
        "WFSpaceacillinChemistryBottle",
        "WFWolfmedAntisepticSpray",
        "WFWolfmedSplint", // V5
        "WFWolfmedAnalgesicPill", // CONSC
        "WFWolfmedOpiateChemistryBottle", // CONSC
        "WFWolfmedStimPen", // CONSC
        "WFWolfmedAnalgesicPen", // playtest 1
        "WFWolfmedOpiatePen", // playtest 1
        // Playtest 5, ITEMS.
        "WFWolfmedNaloxonePen",
        "WFWolfmedHydraulicFluidPack",
        "Tourniquet",
        "WFWolfmedSuture", // playtest 5: the plain suture
        "WFWolfmedAnalgesicPillCanister",
        "WFWolfmedIbuprofenPill",
        "WFWolfmedIbuprofenPillCanister",
        "WFWolfmedKetorolacPill",
        "WFWolfmedKetorolacPillCanister",
        "WFWolfmedTramadolChemistryBottle",
        "WFWolfmedOxycodoneChemistryBottle",
        "WFWolfmedOsteogenPill",
        "WFWolfmedOsteogenPillCanister",
        "WFWolfmedOsteogenChemistryBottle",
        "WFMedkitSynthetic",
        "WFMedkitSyntheticFilled",
        "WFMachineAutodocFlatpack",
        "WFMachineAutodoc",
        "WFWolfmedIvDripFlatpack",
        "WFWolfmedIvDrip",
    ];

    /// <summary>ITEMS: each pill canister, the pill it holds, how many, and the reagent the pill carries.</summary>
    private static readonly (string Canister, string Pill, int Count, string Reagent)[] Canisters =
    [
        ("WFWolfmedAnalgesicPillCanister", "WFWolfmedAnalgesicPill", 7, "WFWolfmedAnalgesic"),
        ("WFWolfmedIbuprofenPillCanister", "WFWolfmedIbuprofenPill", 5, "Ibuprofen"),
        ("WFWolfmedKetorolacPillCanister", "WFWolfmedKetorolacPill", 5, "Ketorolac"),
        ("WFWolfmedOsteogenPillCanister", "WFWolfmedOsteogenPill", 5, "Osteogen"),
    ];

    /// <summary>ITEMS: each new chemistry bottle and the reagent it holds 30 u of.</summary>
    private static readonly (string Bottle, string Reagent)[] Bottles =
    [
        ("WFWolfmedTramadolChemistryBottle", "Tramadol"),
        ("WFWolfmedOxycodoneChemistryBottle", "Oxycodone"),
        ("WFWolfmedOsteogenChemistryBottle", "Osteogen"),
    ];

    /// <summary>ITEMS: each flatpack and what it unpacks into.</summary>
    private static readonly (string Flatpack, string Entity)[] Flatpacks =
    [
        ("WFMachineAutodocFlatpack", "WFMachineAutodoc"),
        ("WFWolfmedIvDripFlatpack", "WFWolfmedIvDrip"),
    ];

    /// <summary>Items reached by crafting rather than by a vendor or a lathe.</summary>
    private static readonly string[] CraftedItems =
    [
        "WFWolfmedSplintImprovised", // V5
        "WFWolfmedMakeshiftTourniquet", // playtest 5: torn from a jumpsuit
        "WFWolfmedMakeshiftSuture5", // playtest 5: a rod and a cloth make five
    ];

    /// <summary>Reagents Wolfmed added. Each needs a reaction chemistry can run.</summary>
    private static readonly string[] ObtainableReagents =
        ["Spaceacillin", "WFWolfmedAnalgesic", "WFWolfmedOpiate", "WFWolfmedStim"]; // CONSC

    /// <summary>
    /// Fills Wolfmed changed or relies on, spawned so an overflow fails here. Every entry each one declares is
    /// checked too, so a kit that fits all but its last item fails even if the named content made it in.
    /// </summary>
    private static readonly (string Entity, string Content)[] Fills =
    [
        ("MedkitBurnFilled", "WFWolfmedSkinGraft"),
        ("CrateMedicalSurgery", "WFWolfmedSkinGraft"),
        ("CrateMedicalSupplies", "WFSpaceacillinChemistryBottle"),
        ("MedkitBruteFilled", "WFWolfmedSplint"), // V5
        // Playtest 5, ITEMS.
        ("MedkitBruteFilled", "WFWolfmedOsteogenPillCanister"),
        ("MedkitFilled", "Tourniquet"),
        ("MedkitFilled", "WFWolfmedSplint"),
        ("MedkitFilled", "WFWolfmedAnalgesicPillCanister"),
        ("MedkitAdvancedFilled", "Tourniquet"),
        ("WFMedkitSyntheticFilled", "WFWolfmedHydraulicFluidPack"),
        // Playtest 5: more to keep someone alive in every trauma kit and medical belt.
        ("MedkitFilled", "WFWolfmedSuture"),
        ("MedkitBruteFilled", "Tourniquet"),
        ("MedkitAdvancedFilled", "EmergencyMedipen"),
        ("MedkitCombatFilled", "Tourniquet"),
        ("ClothingBeltMedicalFilled", "WFWolfmedSuture"),
        ("ClothingBeltMedicalEMTFilled", "WFWolfmedAnalgesicPen"),
    ];

    /// <summary>Reachability: some vending inventory or lathe recipe names every new item.</summary>
    [Test]
    public async Task EveryNewItemIsObtainableTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var prototypes = server.ResolveDependency<IPrototypeManager>();
        var factory = server.ResolveDependency<IComponentFactory>();

        await server.WaitAssertion(() =>
        {
            var reachable = prototypes.EnumeratePrototypes<VendingMachineInventoryPrototype>()
                .SelectMany(inventory => inventory.StartingInventory.Keys)
                .ToHashSet();

            reachable.UnionWith(prototypes.EnumeratePrototypes<LatheRecipePrototype>()
                .Where(recipe => recipe.Result != null)
                .Select(recipe => recipe.Result!.Value.Id));

            // ITEMS: what comes inside a reachable item is reachable: a canister's pills, a kit's contents, the
            // structure a flatpack unpacks into.
            var pending = new Queue<string>(reachable);
            while (pending.TryDequeue(out var id))
            {
                if (!prototypes.TryIndex<EntityPrototype>(id, out var proto))
                    continue;

                var inner = new List<string>();
                if (proto.TryGetComponent<StorageFillComponent>(out var fill, factory))
                {
                    inner.AddRange(fill.Contents
                        .Where(entry => entry.PrototypeId != null)
                        .Select(entry => entry.PrototypeId!.Value.Id));
                }

                if (proto.TryGetComponent<FlatpackComponent>(out var flatpack, factory) && flatpack.Entity is { } unpacked)
                    inner.Add(unpacked.Id);

                foreach (var next in inner)
                {
                    if (reachable.Add(next))
                        pending.Enqueue(next);
                }
            }

            Assert.Multiple(() =>
            {
                foreach (var item in ObtainableItems)
                {
                    Assert.That(prototypes.HasIndex<EntityPrototype>(item), Is.True, $"{item} does not exist.");
                    Assert.That(reachable, Does.Contain(item),
                        $"{item} is in no vending inventory, no lathe recipe and nothing either of them hands out: " +
                        "a player cannot get one, so whatever it treats has no treatment.");
                }

                // V5: the improvised splint is crafted, so its route is a construction recipe whose
                // target node spawns it.
                var crafted = prototypes.EnumeratePrototypes<ConstructionPrototype>()
                    .Select(recipe =>
                        prototypes.TryIndex<ConstructionGraphPrototype>(recipe.Graph, out var graph) &&
                        graph.Nodes.TryGetValue(recipe.TargetNode, out var node)
                            ? (node.Entity as StaticNodeEntity)?.Id
                            : null)
                    .Where(id => id != null)
                    .ToHashSet();

                foreach (var item in CraftedItems)
                {
                    Assert.That(prototypes.HasIndex<EntityPrototype>(item), Is.True, $"{item} does not exist.");
                    Assert.That(crafted, Does.Contain(item), $"no construction recipe builds {item}.");
                }
            });
        });
    }

    /// <summary>A new reagent needs a reaction chemistry can run.</summary>
    [Test]
    public async Task EveryNewReagentIsObtainableTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var prototypes = server.ResolveDependency<IPrototypeManager>();

        await server.WaitAssertion(() =>
        {
            var produced = prototypes.EnumeratePrototypes<ReactionPrototype>()
                .SelectMany(reaction => reaction.Products.Keys)
                .ToHashSet();

            Assert.Multiple(() =>
            {
                foreach (var reagent in ObtainableReagents)
                {
                    Assert.That(prototypes.HasIndex<ReagentPrototype>(reagent), Is.True, $"{reagent} does not exist.");
                    Assert.That(produced, Does.Contain(reagent),
                        $"no reaction makes {reagent}, so chemistry cannot supply it.");
                }
            });
        });
    }

    /// <summary>
    /// The fills spawn cleanly and hold what they say. An overflowing kit logs an error, which fails the
    /// test on its own; the content check catches a silently dropped entry.
    /// </summary>
    [Test]
    public async Task FillsContainWhatTheyDeclareTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var prototypes = server.ResolveDependency<IPrototypeManager>();
        var factory = server.ResolveDependency<IComponentFactory>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                foreach (var (id, content) in Fills)
                {
                    var spawned = entities.SpawnEntity(id, map.GridCoords);
                    var held = Held(entities, spawned);

                    Assert.That(held, Does.Contain(content),
                        $"{id} does not contain {content}: the fill overflowed or the entry was dropped.");

                    // ITEMS: every certain entry, not just the one named, so the last item in a full kit counts.
                    if (prototypes.Index<EntityPrototype>(id).TryGetComponent<StorageFillComponent>(out var fill, factory))
                    {
                        foreach (var entry in fill.Contents)
                        {
                            if (entry.PrototypeId is not { } entryId || entry.SpawnProbability < 1f || entry.GroupId != null)
                                continue;

                            Assert.That(held, Does.Contain(entryId.Id),
                                $"{id} does not contain {entryId.Id}: the fill overflowed or the entry was dropped.");
                        }
                    }

                    entities.DeleteEntity(spawned);
                }
            });
        });
    }

    /// <summary>ITEMS: each pill canister spawns full of its pill, and each pill carries its reagent.</summary>
    [Test]
    public async Task PillCanistersSpawnFullTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var containers = entities.System<SharedContainerSystem>();
            var solutions = entities.System<SharedSolutionContainerSystem>();
            Assert.Multiple(() =>
            {
                foreach (var (canister, pill, count, reagent) in Canisters)
                {
                    var spawned = entities.SpawnEntity(canister, map.GridCoords);
                    var pills = containers.GetAllContainers(spawned)
                        .SelectMany(container => container.ContainedEntities)
                        .Where(entity => entities.GetComponent<MetaDataComponent>(entity).EntityPrototype?.ID == pill)
                        .ToList();

                    Assert.That(pills, Has.Count.EqualTo(count), $"{canister} should hold {count} {pill}.");
                    foreach (var held in pills)
                    {
                        Assert.That(solutions.TryGetSolution(held, "food", out _, out var solution), Is.True,
                            $"{pill} has no food solution.");
                        Assert.That(solution?.GetTotalPrototypeQuantity(reagent).Float() ?? 0f, Is.GreaterThan(0f),
                            $"{pill} carries no {reagent}.");
                    }

                    entities.DeleteEntity(spawned);
                }

                foreach (var (bottle, reagent) in Bottles)
                {
                    var spawned = entities.SpawnEntity(bottle, map.GridCoords);
                    Assert.That(solutions.TryGetSolution(spawned, "drink", out _, out var solution), Is.True,
                        $"{bottle} has no drink solution.");
                    Assert.That(solution?.GetTotalPrototypeQuantity(reagent).Float() ?? 0f, Is.EqualTo(30f),
                        $"{bottle} should hold 30 u of {reagent}.");
                    entities.DeleteEntity(spawned);
                }
            });
        });
    }

    /// <summary>
    /// ITEMS: each flatpack unpacks with a multitool into its structure, and costs at least what that structure
    /// appraises at, so buying one to sell what it builds never pays.
    /// </summary>
    [Test]
    public async Task FlatpacksUnpackTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var prototypes = server.ResolveDependency<IPrototypeManager>();
        var map = await Pair.CreateTestMap();

        foreach (var (flatpack, expected) in Flatpacks)
        {
            EntityUid pack = default;
            await server.WaitAssertion(() =>
            {
                pack = entities.SpawnEntity(flatpack, map.GridCoords);
                var user = entities.SpawnEntity(null, map.GridCoords);
                var tool = entities.SpawnEntity("Multitool", map.GridCoords);
                var unpack = new InteractUsingEvent(user, tool, pack, map.GridCoords);
                entities.EventBus.RaiseLocalEvent(pack, unpack);
                Assert.That(unpack.Handled, Is.True, $"a multitool did not unpack {flatpack}.");
                entities.DeleteEntity(tool);
                entities.DeleteEntity(user);
            });

            await server.WaitRunTicks(2);
            await server.WaitAssertion(() =>
            {
                var built = EntityUid.Invalid;
                var query = entities.EntityQueryEnumerator<MetaDataComponent, TransformComponent>();
                while (query.MoveNext(out var uid, out var meta, out var xform))
                {
                    if (meta.EntityPrototype?.ID == expected && xform.GridUid == map.Grid.Owner)
                        built = uid;
                }

                Assert.Multiple(() =>
                {
                    Assert.That(built, Is.Not.EqualTo(EntityUid.Invalid), $"unpacking {flatpack} did not build {expected}.");
                    Assert.That(entities.Deleted(pack), Is.True, $"{flatpack} was not used up.");
                    if (built.IsValid())
                    {
                        var pricing = entities.System<PricingSystem>();
                        var cost = pricing.GetEstimatedPrice(prototypes.Index<EntityPrototype>(flatpack));
                        Assert.That(pricing.GetPrice(built), Is.LessThanOrEqualTo(cost),
                            $"{expected} appraises above the {flatpack} that builds it.");
                    }
                });

                // Clear the tile for the next one.
                if (built.IsValid())
                    entities.DeleteEntity(built);
            });
        }
    }

    /// <summary>The prototype ids of everything directly inside an entity's containers.</summary>
    private static List<string> Held(IEntityManager entities, EntityUid owner)
    {
        var held = new List<string>();
        foreach (var container in entities.System<SharedContainerSystem>().GetAllContainers(owner))
        {
            foreach (var entity in container.ContainedEntities)
            {
                if (entities.GetComponent<MetaDataComponent>(entity).EntityPrototype?.ID is { } prototype)
                    held.Add(prototype);
            }
        }

        return held;
    }
}
