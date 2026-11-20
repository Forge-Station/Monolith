#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server._WF.Wolfmed.Autodoc;
using Content.Shared._Onyx.Wounds;
using Content.Shared._Shitmed.Targeting;
using Content.Shared._WF.Wolfmed.Autodoc;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Wolfmed;

/// <summary>
/// Playtest 5: "the autodoc won't fix its own mistakes without being removed and reinserted". What a run makes on the
/// occupant is marked the pod's own so the planner does not read a suture as new work; once the run is over the marks
/// come off and the next plan sees the leftovers as the patient's, a bounded number of times per occupant.
/// </summary>
[TestFixture]
[TestOf(typeof(AutodocSystem))]
public sealed class WolfmedPodLeftoversTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: WolfmedLeftoversTestPod
  parent: WFMachineAutodoc
  suffix: test leftovers
  components:
  - type: Autodoc
    stepTime: 0.02
    bestStepTime: 0.02
    malfunctionChance: 0
    podWoundGrace: 0.5
  - type: ApcPowerReceiver
    needsPower: false
";

    [Test]
    public async Task LeftoversArePlannedAfterTheRunABoundedNumberOfTimesTest()
    {
        var map = await Pair.CreateTestMap();
        Entity<AutodocComponent> pod = default;
        EntityUid body = default;
        var wounds = new List<EntityUid>();
        var autodoc = SEntMan.System<AutodocSystem>();

        await Server.WaitAssertion(() =>
        {
            var uid = SEntMan.SpawnEntity("WolfmedLeftoversTestPod", map.GridCoords);
            pod = (uid, SEntMan.GetComponent<AutodocComponent>(uid));
            body = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            var slash = SProtoMan.Index<DamageTypePrototype>("Slash");
            SEntMan.System<DamageableSystem>().TryChangeDamage(body, new DamageSpecifier(slash, FixedPoint2.New(40)),
                origin: null, targetPart: TargetBodyPart.Torso);
            var torso = SEntMan.System<SharedBodySystem>().GetBodyChildrenOfType(body, BodyPartType.Torso).Single().Id;
            wounds.AddRange(SEntMan.System<WoundSystem>().GetWounds(torso).Select(w => w.Owner));
            Assert.That(wounds, Is.Not.Empty);
            Assert.That(autodoc.TryInsert(pod, body), Is.True);
            TestContext.Out.WriteLine("plan: " + string.Join(", ", autodoc.Plan(pod, body).Select(e => $"{e.Surgery.Id} {e.Part}")));
            Assert.That(PlansBleeding(autodoc, pod, body), Is.True, "a bleeding slash does not list the bleeding step.");

            // As the pod leaves it after a run: its own, so the wound steps pass it by.
            foreach (var wound in wounds)
                SEntMan.EnsureComponent<WolfmedPodWoundComponent>(wound);
            Assert.That(PlansBleeding(autodoc, pod, body), Is.False, "a wound marked the pod's own was planned.");
            autodoc.ScheduleLeftoverRelease(pod);
        });

        await RunSeconds(1.5f);

        await Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(wounds.Any(wound => SEntMan.HasComponent<WolfmedPodWoundComponent>(wound)), Is.False, "a leftover is still marked the pod's own.");
                Assert.That(SEntMan.GetComponent<AutodocComponent>(pod).LeftoverPasses, Is.EqualTo(1));
                Assert.That(PlansBleeding(autodoc, pod, body), Is.True, "the leftover is not planned after the run.");
            });

            // The bound: past the limit the marks stay, so a run that always leaves something cannot go on for ever.
            var comp = SEntMan.GetComponent<AutodocComponent>(pod);
            comp.LeftoverPasses = comp.LeftoverPassLimit;
            foreach (var wound in wounds)
                SEntMan.EnsureComponent<WolfmedPodWoundComponent>(wound);
            autodoc.ScheduleLeftoverRelease(pod);
        });

        await RunSeconds(1.5f);

        await Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(wounds.All(wound => SEntMan.HasComponent<WolfmedPodWoundComponent>(wound)), Is.True, "the pass limit did not hold.");
                Assert.That(PlansBleeding(autodoc, pod, body), Is.False);
            });
        });
    }

    /// <summary>Whether the plan closes the torso's bleeding: the step the pod's own wounds are hidden from.</summary>
    private static bool PlansBleeding(AutodocSystem autodoc, Entity<AutodocComponent> pod, EntityUid body) =>
        autodoc.Plan(pod, body).Any(entry => entry.Surgery.Id == "WFSurgeryStopBleeding" && entry.Part == TargetBodyPart.Torso);
}
