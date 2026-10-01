#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server.Atmos.Components;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Body.Components;
using Content.Server.Fluids.EntitySystems;
using Content.Server._WF.Wolfmed.Autodoc;
using Content.Server._WF.Wolfmed.Medical;
using Content.Shared._Onyx.Wounds;
using Content.Shared._Shitmed.Targeting;
using Content.Shared._WF.Wolfmed.Autodoc;
using Content.Shared._WF.Wolfmed.CCVar;
using Content.Shared._WF.Wolfmed.Reagents;
using Content.Shared._WF.Wolfmed.Surgery;
using Content.Shared.Atmos.Components;
using Content.Shared.Body.Part;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Fluids.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.IgnitionSource;
using Content.Shared.Interaction;
using Content.Shared.Item.ItemToggle;
using Content.Shared.StepTrigger.Components;
using Content.Shared.VendingMachines;
using NUnit.Framework;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._WF.Wolfmed.Scenarios;

/// <summary>
/// Playtest 3, IPC round 2: welding an IPC set its own spilled oil on fire. A chassis now runs on non-flammable
/// hydraulic fluid; the pack refills it by hand and in the pod, and oil is a foreign reagent both refuse.
/// </summary>
[TestFixture]
[TestOf(typeof(WolfmedFluidPackSystem))]
public sealed class WolfmedHydraulicFluidTest : GameTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: WolfmedHydraulicTestAutodoc
  parent: WFMachineAutodoc
  suffix: hydraulic test
  components:
  - type: Autodoc
    stepTime: 0.02
    bestStepTime: 0.02
    malfunctionChance: 0
  - type: ApcPowerReceiver
    needsPower: false

- type: entity
  id: WolfmedHydraulicTestOilPack
  parent: WFWolfmedHydraulicFluidPack
  suffix: oil
  components:
  - type: SolutionContainerManager
    solutions:
      pack:
        maxVol: 200
        reagents:
        - ReagentId: Oil
          Quantity: 200
";

    private const string Fluid = "WFWolfmedHydraulicFluid";

    /// <summary>A 3x3 steel room with a breathable mix: a grid with real atmospherics, so a hotspot can form.</summary>
    private static readonly ResPath Room = new("Maps/Test/Breathing/3by3-20oxy-80nit.yml");

    private readonly List<string> _notes = new();

    [TearDown]
    public void WriteNotes()
    {
        foreach (var line in _notes)
            TestContext.Out.WriteLine(line);
    }

    /// <summary>
    /// <c>HydraulicFluidTest</c>: an IPC bleeds a puddle of hydraulic fluid; a lit welder repairs its breach standing in
    /// it and nothing catches fire (the same welder over an oil puddle lights it); the puddle is slippery; the pack
    /// refills the chassis by hand and in the pod; an oil pack is refused by both, the pod with its reagent-ignored
    /// line; the pod never pushes hydraulic fluid into flesh.
    /// </summary>
    [Test]
    public async Task HydraulicFluidTest()
    {
        await OverrideCVar(Side.Server, WolfmedCVars.Consciousness, true);

        // The reagent and where it is stocked.
        await Server.WaitAssertion(() =>
        {
            var protos = Server.ResolveDependency<IPrototypeManager>();
            var fluid = protos.Index<ReagentPrototype>(Fluid);
            Assert.Multiple(() =>
            {
                Assert.That(fluid.Flammability, Is.Zero, "hydraulic fluid burns.");
                Assert.That(fluid.TileReactions, Is.Null.Or.Empty, "hydraulic fluid reacts with the tile it lands on.");
                Assert.That(fluid.SlipData, Is.Not.Null, "a hydraulic fluid spill is not a slip hazard.");
                Assert.That(protos.Index<ReagentPrototype>("Oil").Flammability, Is.GreaterThan(0),
                    "the control reagent no longer burns, so this test proves nothing.");

                foreach (var inventory in new[] { "WFWolfgateVendInventory", "NanoMedInventory", "CiviMedVendInventory" })
                {
                    Assert.That(protos.Index<VendingMachineInventoryPrototype>(inventory).StartingInventory
                        .ContainsKey("WFWolfmedHydraulicFluidPack"), Is.True, $"{inventory} does not stock the pack.");
                }

                var list = protos.Index<AutodocReagentsPrototype>("WFWolfmedAutodocReagents").Reagents;
                Assert.That(list.Any(e => e.Reagent == Fluid && e.Role == AutodocReagentRole.Fluid && e.Machine), Is.True,
                    "the pod does not list hydraulic fluid as a machine fluid.");
                Assert.That(list.Any(e => e.Reagent == "Oil"), Is.False, "the pod lists oil.");
            });
        });

        // Chemistry makes it: oil and silicon off the dispenser.
        await Server.WaitAssertion(() =>
        {
            var beaker = SEntMan.SpawnEntity("Beaker", MapCoordinates.Nullspace);
            var solutions = SEntMan.System<SharedSolutionContainerSystem>();
            Assert.That(solutions.TryGetSolution(beaker, "beaker", out var soln, out _), Is.True);
            solutions.TryAddReagent(soln!.Value, "Oil", FixedPoint2.New(10), out _);
            solutions.TryAddReagent(soln.Value, "Silicon", FixedPoint2.New(10), out _);
            Assert.That(soln.Value.Comp.Solution.GetTotalPrototypeQuantity(Fluid), Is.EqualTo(FixedPoint2.New(20)),
                "oil and silicon did not make hydraulic fluid.");
            SEntMan.DeleteEntity(beaker);
        });

        var (grid, center) = await LoadRoom();
        EntityUid ipc = default, welder = default, medic = default;
        var tile = new Vector2i(0, 0);

        await Server.WaitPost(() =>
        {
            ipc = SEntMan.SpawnEntity("MobIPC", center);
            medic = SEntMan.SpawnEntity("MobHuman", center);
        });
        await RunSeconds(1);

        // A breach in the torso, and a pool of what came out of it.
        await Server.WaitAssertion(() =>
        {
            var bloodstream = SEntMan.GetComponent<BloodstreamComponent>(ipc);
            Assert.That(bloodstream.BloodReagent.Id, Is.EqualTo(Fluid), "an IPC does not run on hydraulic fluid.");

            SEntMan.System<DamageableSystem>().TryChangeDamage(ipc, WolfmedScenario.Spec("Piercing", 40),
                targetPart: TargetBodyPart.Torso);
            new WolfmedScenario(SEntMan).Bloodstream.TryModifyBloodLevel(ipc, FixedPoint2.New(-60));
        });
        await RunSeconds(2);

        var torso = default(EntityUid);
        await Server.WaitAssertion(() =>
        {
            var s = new WolfmedScenario(SEntMan);
            torso = s.Part(ipc, BodyPartType.Torso);
            Assert.That(SEntMan.System<WolfmedSurgeryConditionSystem>().FindWound(torso, "WFWolfmedBreachWound"), Is.Not.Null,
                "the fixture has no breach to weld.");

            var puddle = PuddleOn(grid, tile);
            Assert.That(puddle, Is.Not.Null, "the chassis left no puddle.");
            var contents = PuddleReagents(puddle!.Value);
            _notes.Add($"puddle: {string.Join(", ", contents.Select(p => $"{p.Key} {p.Value}"))}");
            Assert.Multiple(() =>
            {
                Assert.That(contents.ContainsKey(Fluid), Is.True, "the puddle is not hydraulic fluid.");
                Assert.That(contents.ContainsKey("Oil"), Is.False, "the chassis still bleeds oil.");
                Assert.That(Atmos(grid).Tiles[tile].PuddleSolutionFlammability, Is.Zero, "the puddle made its tile flammable.");
                Assert.That(SEntMan.GetComponent<StepTriggerComponent>(puddle.Value).Active, Is.True,
                    "the puddle is not a slip hazard.");
            });

            // A medic lights a welder standing in it and starts on the torso.
            welder = SEntMan.SpawnEntity("Welder", center);
            Assert.That(SEntMan.System<SharedHandsSystem>().TryPickupAnyHand(medic, welder), Is.True);
            Assert.That(SEntMan.System<ItemToggleSystem>().TryActivate(welder, medic), Is.True);
            Assert.That(SEntMan.GetComponent<IgnitionSourceComponent>(welder).Ignited, Is.True,
                "the welder is not a live ignition source, so this test proves nothing.");
            SEntMan.GetComponent<TargetingComponent>(medic).Target = TargetBodyPart.Torso;
            var interact = new InteractUsingEvent(medic, welder, ipc, center);
            SEntMan.EventBus.RaiseLocalEvent(ipc, interact);
            Assert.That(interact.Handled, Is.True, "the welder did not start a repair.");
        });

        var hotspot = false;
        var burning = false;
        for (var second = 0; second < 20; second++)
        {
            await RunSeconds(1);
            await Server.WaitPost(() =>
            {
                hotspot |= SEntMan.System<AtmosphereSystem>().IsHotspotActive(grid, tile);
                burning |= OnFire(ipc) || OnFire(medic);
            });
        }

        await Server.WaitAssertion(() =>
        {
            var conditions = SEntMan.System<WolfmedSurgeryConditionSystem>();
            _notes.Add($"after the weld: breach {conditions.FindWound(torso, "WFWolfmedBreachWound")?.Comp.Severity}, " +
                       $"frame {conditions.FindWound(torso, "IpcMechanicalDamageWound")?.Comp.Severity}, hotspot {hotspot}, burning {burning}");
            Assert.Multiple(() =>
            {
                Assert.That(hotspot, Is.False, "the welder lit the chassis's puddle.");
                Assert.That(burning, Is.False, "somebody caught fire.");
                Assert.That(conditions.FindWound(torso, "WFWolfmedBreachWound"), Is.Null, "the repair did not close the breach.");
                Assert.That(conditions.FindWound(torso, "IpcMechanicalDamageWound"), Is.Null, "the repair did not finish.");
            });

            SEntMan.System<ItemToggleSystem>().TryDeactivate(welder, medic);
        });

        // The control: the same welder over a pool of oil does light it.
        var (oilGrid, oilCenter) = await LoadRoom();
        await Server.WaitPost(() =>
        {
            var oilMedic = SEntMan.SpawnEntity("MobHuman", oilCenter);
            var torch = SEntMan.SpawnEntity("Welder", oilCenter);
            SEntMan.System<SharedHandsSystem>().TryPickupAnyHand(oilMedic, torch);
            SEntMan.System<PuddleSystem>().TrySpillAt(oilCenter, new Solution("Oil", FixedPoint2.New(60)), out _, sound: false);
            SEntMan.System<ItemToggleSystem>().TryActivate(torch, oilMedic);
        });

        var oilLit = false;
        for (var second = 0; second < 5 && !oilLit; second++)
        {
            await RunSeconds(1);
            await Server.WaitPost(() => oilLit = SEntMan.System<AtmosphereSystem>().IsHotspotActive(oilGrid, tile));
        }

        Assert.That(oilLit, Is.True, "a lit welder over an oil puddle did not light it, so the fluid test proves nothing.");

        // The pack refills the chassis by hand.
        EntityUid pack = default, oilPack = default, refiller = default;
        var before = 0f;
        await Server.WaitAssertion(() =>
        {
            var s = new WolfmedScenario(SEntMan);
            var packs = SEntMan.System<WolfmedFluidPackSystem>();
            refiller = SEntMan.SpawnEntity("MobHuman", center);
            pack = SEntMan.SpawnEntity("WFWolfmedHydraulicFluidPack", center);
            Assert.That(SEntMan.System<SharedHandsSystem>().TryPickupAnyHand(refiller, pack), Is.True);
            before = s.Blood(ipc);
            Assert.That(before, Is.LessThan(0.9f), "the chassis is not short of fluid.");
            Assert.That(packs.TryStart((pack, SEntMan.GetComponent<WolfmedFluidPackComponent>(pack)), ipc, refiller), Is.True,
                "the pack would not start on a chassis short of its own fluid.");
        });
        await RunSeconds(8);

        await Server.WaitAssertion(() =>
        {
            var s = new WolfmedScenario(SEntMan);
            var left = PackVolume(pack);
            var gained = (s.Blood(ipc) - before) * s.Pool(ipc);
            _notes.Add($"pack: fluid {before:P0} -> {s.Blood(ipc):P0}, pack 200 -> {left}");
            Assert.Multiple(() =>
            {
                Assert.That(left, Is.LessThan(FixedPoint2.New(200)), "the pack gave nothing.");
                Assert.That(s.Blood(ipc), Is.GreaterThan(before), "the chassis was not refilled.");
                Assert.That(gained, Is.EqualTo((200 - left).Float()).Within(2f), "what left the pack did not go into the chassis.");
            });

            // An oil pack is refused by hand: the whole pack, nothing moves.
            oilPack = SEntMan.SpawnEntity("WolfmedHydraulicTestOilPack", center);
            var packs = SEntMan.System<WolfmedFluidPackSystem>();
            var oil = (oilPack, SEntMan.GetComponent<WolfmedFluidPackComponent>(oilPack));
            var level = s.Blood(ipc);
            Assert.Multiple(() =>
            {
                Assert.That(packs.GetRefusal(oil, ipc), Is.EqualTo("wolfmed-fluid-pack-wrong"));
                Assert.That(packs.TryTransfer(oil, ipc, refiller, out var moved), Is.False);
                Assert.That(moved, Is.EqualTo(FixedPoint2.Zero));
                Assert.That(s.Blood(ipc), Is.EqualTo(level), "oil went into the chassis by hand.");
            });
        });

        // The pod refuses the oil pack with its reagent-ignored line and uses a hydraulic pack.
        var map = await Pair.CreateTestMap();
        EntityUid human = default;
        Entity<AutodocComponent> pod = default, humanPod = default;
        var level = 0f;
        await Server.WaitAssertion(() =>
        {
            var s = new WolfmedScenario(SEntMan);
            var autodoc = SEntMan.System<AutodocSystem>();
            var slots = SEntMan.System<ItemSlotsSystem>();
            s.SetAir(map.MapUid, true);
            s.KeepGrid(map.Grid);

            SEntMan.System<SharedTransformSystem>().SetCoordinates(ipc, map.GridCoords);
            SEntMan.System<SharedTransformSystem>().SetCoordinates(oilPack, map.GridCoords);
            s.SetBlood(ipc, 0.5f);
            level = s.Blood(ipc);
            SEntMan.System<WoundSystem>().CreateOrMergeWound(s.Part(ipc, BodyPartType.Arm, BodyPartSymmetry.Left),
                "WFWolfmedDentWound", FixedPoint2.New(20));

            var uid = SEntMan.SpawnEntity("WolfmedHydraulicTestAutodoc", map.GridCoords);
            pod = (uid, SEntMan.GetComponent<AutodocComponent>(uid));
            Assert.That(slots.TryInsert(uid, AutodocComponent.ReservoirSlotIds[0], oilPack, null), Is.True,
                "the pod's reservoir does not take a fluid pack.");
            Assert.That(autodoc.TryInsert(pod, ipc), Is.True);

            human = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            s.SetBlood(human, 0.5f);
            var humanUid = SEntMan.SpawnEntity("WolfmedHydraulicTestAutodoc", map.GridCoords);
            humanPod = (humanUid, SEntMan.GetComponent<AutodocComponent>(humanUid));
            Assert.That(slots.TryInsert(humanUid, AutodocComponent.ReservoirSlotIds[0],
                SEntMan.SpawnEntity("WFWolfmedHydraulicFluidPack", map.GridCoords), null), Is.True);
            Assert.That(autodoc.TryInsert(humanPod, human), Is.True);
        });
        await Pair.RunTicksSync(10);

        await Server.WaitAssertion(() =>
        {
            var s = new WolfmedScenario(SEntMan);
            var autodoc = SEntMan.System<AutodocSystem>();
            Assert.Multiple(() =>
            {
                Assert.That(autodoc.NeedsTransfusion(ipc), Is.True, "the fixture chassis is not short of fluid.");
                Assert.That(autodoc.TryTransfuse(pod, ipc), Is.False, "the pod pumped an oil pack into a chassis.");
                Assert.That(s.Blood(ipc), Is.EqualTo(level).Within(0.001f), "oil went into the chassis in the pod.");
                Assert.That(PackReagent(oilPack, "Oil"), Is.EqualTo(FixedPoint2.New(200)), "the pod drew from the oil pack.");

                Assert.That(autodoc.TryTransfuse(humanPod, human), Is.False, "the pod pumped hydraulic fluid into flesh.");
                Assert.That(s.Blood(human), Is.EqualTo(0.5f).Within(0.01f), "a human was topped up with hydraulic fluid.");
            });

            Assert.That(autodoc.TryQueue(pod, "WFSurgeryWeldChassis", TargetBodyPart.LeftArm), Is.True);
            Assert.That(autodoc.TryStart(pod, null), Is.True);
        });
        await Pair.RunTicksSync(2);

        await Server.WaitAssertion(() =>
        {
            var s = new WolfmedScenario(SEntMan);
            var autodoc = SEntMan.System<AutodocSystem>();
            var ignored = Loc.GetString("wolfmed-autodoc-voice-reagent-ignored");
            Assert.That(pod.Comp!.LastLine == ignored || pod.Comp.VoiceQueue.Any(r => r.Line == "reagent-ignored"), Is.True,
                "the pod did not say it was ignoring the oil.");
            Assert.That(s.Blood(ipc), Is.EqualTo(level).Within(0.001f), "the pod's run pumped the oil.");

            // A hydraulic pack beside it is what the pod uses, and the oil stays where it is.
            var fresh = SEntMan.SpawnEntity("WFWolfmedHydraulicFluidPack", map.GridCoords);
            Assert.That(SEntMan.System<ItemSlotsSystem>().TryInsert(pod.Owner, AutodocComponent.ReservoirSlotIds[1], fresh, null), Is.True);
            Assert.That(autodoc.TryTransfuse(pod, ipc), Is.True, "the pod would not use a hydraulic pack on a chassis.");
            _notes.Add($"pod: chassis {level:P0} -> {s.Blood(ipc):P0}, hydraulic pack 200 -> {PackVolume(fresh)}, oil {PackReagent(oilPack, "Oil")}");
            Assert.Multiple(() =>
            {
                Assert.That(s.Blood(ipc), Is.GreaterThan(level + 0.1f), "the pod's transfusion did not refill the chassis.");
                Assert.That(PackVolume(fresh), Is.LessThan(FixedPoint2.New(200)));
                Assert.That(PackReagent(oilPack, "Oil"), Is.EqualTo(FixedPoint2.New(200)));
            });
        });
    }

    private async Task<(EntityUid Grid, EntityCoordinates Center)> LoadRoom()
    {
        EntityUid grid = default;
        await Server.WaitPost(() =>
        {
            SEntMan.System<SharedMapSystem>().CreateMap(out var mapId);
            Assert.That(SEntMan.System<MapLoaderSystem>().TryLoadGrid(mapId, Room, out var loaded), Is.True);
            grid = loaded!.Value.Owner;
            new WolfmedScenario(SEntMan).KeepGrid(grid);
        });
        await Pair.RunTicksSync(5);
        return (grid, new EntityCoordinates(grid, new Vector2(0.5f, 0.5f)));
    }

    private GridAtmosphereComponent Atmos(EntityUid grid) => SEntMan.GetComponent<GridAtmosphereComponent>(grid);

    private bool OnFire(EntityUid uid) =>
        SEntMan.TryGetComponent<FlammableComponent>(uid, out var flammable) && flammable.OnFire;

    private EntityUid? PuddleOn(EntityUid grid, Vector2i tile)
    {
        var maps = SEntMan.System<SharedMapSystem>();
        var gridComp = SEntMan.GetComponent<MapGridComponent>(grid);
        var query = SEntMan.EntityQueryEnumerator<PuddleComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.GridUid == grid && maps.TileIndicesFor(grid, gridComp, xform.Coordinates) == tile)
                return uid;
        }

        return null;
    }

    private Dictionary<string, FixedPoint2> PuddleReagents(EntityUid puddle)
    {
        var comp = SEntMan.GetComponent<PuddleComponent>(puddle);
        Assert.That(SEntMan.System<SharedSolutionContainerSystem>().TryGetSolution(puddle, comp.SolutionName, out _, out var solution),
            Is.True);
        return solution!.Contents.ToDictionary(r => r.Reagent.Prototype, r => r.Quantity);
    }

    private FixedPoint2 PackVolume(EntityUid pack) =>
        SEntMan.System<SharedSolutionContainerSystem>().TryGetSolution(pack, "pack", out _, out var solution)
            ? solution.Volume
            : FixedPoint2.Zero;

    private FixedPoint2 PackReagent(EntityUid pack, string reagent) =>
        SEntMan.System<SharedSolutionContainerSystem>().TryGetSolution(pack, "pack", out _, out var solution)
            ? solution.GetTotalPrototypeQuantity(reagent)
            : FixedPoint2.Zero;
}
