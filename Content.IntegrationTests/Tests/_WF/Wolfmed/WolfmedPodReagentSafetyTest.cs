#nullable enable
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._WF.Wolfmed.Autodoc;
using Content.Shared._WF.Wolfmed.Autodoc;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.FixedPoint;
using NUnit.Framework;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.Wolfmed;

/// <summary>
/// Playtest 5, "when fixing a brain the patient gets poison damage": the pod never pushes a reagent past its safe
/// line in the blood. An analgesic that poisons from 20 units stops at 18 however much the dose asks for, and a second
/// push on top of it pushes nothing.
/// </summary>
[TestFixture]
[TestOf(typeof(AutodocSystem))]
public sealed class WolfmedPodReagentSafetyTest : GameTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: WolfmedSafetyTestPod
  parent: WFMachineAutodoc
  suffix: test reagent safety
  components:
  - type: ApcPowerReceiver
    needsPower: false

- type: entity
  id: WolfmedTestAnalgesicBeaker
  parent: Beaker
  suffix: analgesic
  components:
  - type: SolutionContainerManager
    solutions:
      beaker:
        maxVol: 50
        reagents:
        - ReagentId: WFWolfmedAnalgesic
          Quantity: 50
";

    [Test]
    public async Task PodStopsAtTheSafeLineTest()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var autodoc = SEntMan.System<AutodocSystem>();
            var solutions = SEntMan.System<SharedSolutionContainerSystem>();
            var uid = SEntMan.SpawnEntity("WolfmedSafetyTestPod", map.GridCoords);
            var pod = (uid, SEntMan.GetComponent<AutodocComponent>(uid));
            var beaker = SEntMan.SpawnEntity("WolfmedTestAnalgesicBeaker", map.GridCoords);
            Assert.That(SEntMan.System<ItemSlotsSystem>().TryInsert(uid, AutodocComponent.ReservoirSlotIds[0], beaker, null), Is.True);
            var body = SEntMan.SpawnEntity("MobHuman", map.GridCoords);

            Assert.That(autodoc.PushReagent(pod, body, AutodocReagentRole.Anaesthetic, 30f), Is.True, "the pod pushed nothing.");
            Assert.That(InBlood(solutions, body), Is.EqualTo(FixedPoint2.New(18)), "the pod pushed past the analgesic's safe line.");

            Assert.That(autodoc.PushReagent(pod, body, AutodocReagentRole.Anaesthetic, 30f), Is.False, "a second push on a full patient pushed something.");
            Assert.That(InBlood(solutions, body), Is.EqualTo(FixedPoint2.New(18)));
            Assert.That(solutions.TryGetSolution(beaker, "beaker", out _, out var left) && left!.Volume == FixedPoint2.New(32), Is.True,
                "the beaker did not keep what the patient could not take.");
        });
    }

    private static FixedPoint2 InBlood(SharedSolutionContainerSystem solutions, EntityUid body) =>
        solutions.TryGetSolution(body, "chemicals", out _, out var chemicals)
            ? chemicals!.GetTotalPrototypeQuantity("WFWolfmedAnalgesic")
            : FixedPoint2.Zero;
}
