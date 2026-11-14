#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server._WF.Wolfmed.Autodoc;
using Content.Server._WF.Wolfmed.Wounds;
using Content.Server.Atmos.EntitySystems;
using Content.Shared._Onyx.Wounds;
using Content.Shared._Shitmed.Targeting;
using Content.Shared._WF.Wolfmed.Autodoc;
using Content.Shared._WF.Wolfmed.Wounds;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using NUnit.Framework;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.Wolfmed;

/// <summary>
/// Playtest 5: "won't fix infections even with antibiotics". An infected occupant is dosed out of the reservoir's
/// antibiotic beside the queue, the way the blood reservoir runs; with nothing usable loaded the pod says NO ANTIBIOTIC
/// LOADED, shows it on the readout, and starts as soon as a beaker goes in.
/// </summary>
[TestFixture]
[TestOf(typeof(AutodocSystem))]
public sealed class PodAntibioticsTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: WolfmedTestAntibioticPod
  parent: WFMachineAutodoc
  suffix: test antibiotics
  components:
  - type: Autodoc
    stepTime: 0.02
    bestStepTime: 0.02
    malfunctionChance: 0
  - type: ApcPowerReceiver
    needsPower: false

- type: entity
  id: WolfmedTestSpaceacillinBeaker
  parent: Beaker
  suffix: spaceacillin
  components:
  - type: SolutionContainerManager
    solutions:
      beaker:
        maxVol: 50
        reagents:
        - ReagentId: Spaceacillin
          Quantity: 50
";

    private AutodocSystem Autodoc => SEntMan.System<AutodocSystem>();

    [TestCase(true)]
    [TestCase(false)]
    public async Task PodDosesAnInfectedOccupantOutOfItsReservoirTest(bool loaded)
    {
        var map = await Pair.CreateTestMap();
        Entity<AutodocComponent> pod = default;
        EntityUid body = default, wound = default;
        var before = 0f;

        await Server.WaitAssertion(() =>
        {
            SEntMan.EnsureComponent<Content.Server._Mono.Cleanup.CleanupImmuneComponent>(map.Grid);
            SEntMan.System<AtmosphereSystem>().SetMapAtmosphere(map.MapUid, false, WolfmedIvDripTest.StationAir());
            var uid = SEntMan.SpawnEntity("WolfmedTestAntibioticPod", map.GridCoords);
            pod = (uid, SEntMan.GetComponent<AutodocComponent>(uid));
            if (loaded)
                LoadBeaker(pod);

            body = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            var slash = SProtoMan.Index<DamageTypePrototype>("Slash");
            SEntMan.System<DamageableSystem>().TryChangeDamage(body, new DamageSpecifier(slash, FixedPoint2.New(20)),
                origin: null, targetPart: TargetBodyPart.Torso);
            var torso = SEntMan.System<SharedBodySystem>().GetBodyChildrenOfType(body, BodyPartType.Torso).Single().Id;
            wound = SEntMan.System<WoundSystem>().GetWounds(torso).First().Owner;

            // A dirty wound left too long: spreading, so one dose does not clear it and the course has to keep going.
            var infection = SEntMan.System<WolfmedInfectionSystem>();
            infection.Contaminate(wound);
            for (var batch = 0; batch < 600 && infection.GetStage(wound) < WolfmedInfectionStage.Spreading; batch++)
                infection.Update(5f);
            Assert.That(infection.GetStage(wound), Is.GreaterThanOrEqualTo(WolfmedInfectionStage.Spreading), "the wound never got infected enough.");
            Assert.That(infection.HasInfection(body), Is.True);
            before = SEntMan.GetComponent<WolfmedInfectionComponent>(wound).Progress;

            Assert.That(Autodoc.TryInsert(pod, body), Is.True);
        });

        Assert.That(await SecondsUntil(5, () => Autodoc.IsPowered(pod)), Is.Not.Null, "the test pod never powered.");

        await Server.WaitAssertion(() =>
        {
            Autodoc.TryPlan(pod);
            var comp = SEntMan.GetComponent<AutodocComponent>(pod);
            if (loaded)
            {
                Assert.That(Autodoc.AntibioticsRunning(pod), Is.True, "the plan did not start the course.");
                Assert.That(Autodoc.AntibioticFault(pod), Is.False);
                return;
            }

            Assert.Multiple(() =>
            {
                Assert.That(Autodoc.AntibioticsRunning(pod), Is.False);
                Assert.That(Autodoc.AntibioticFault(pod), Is.True, "an infected occupant with no antibiotic loaded raised no fault.");
                Assert.That(comp.VoiceEvents.GetValueOrDefault(AutodocVoiceEvent.NoAntibiotic), Is.EqualTo(1), "NO ANTIBIOTIC LOADED was not said once.");
            });

            // The beaker arrives: the fault clears on the next look and the course starts.
            LoadBeaker(pod);
        });

        await RunSeconds(3);

        await Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(Autodoc.AntibioticFault(pod), Is.False, "the fault did not clear.");
                Assert.That(SEntMan.GetComponent<WolfmedAutodocAntibioticComponent>(pod).Given, Is.GreaterThan(0f), "no dose went in.");
                Assert.That(SEntMan.System<SharedSolutionContainerSystem>().TryGetSolution(body, "chemicals", out _, out var chemicals) &&
                            chemicals.ContainsPrototype("Spaceacillin"), Is.True, "the dose is not in the bloodstream.");
            });
        });

        // Every unit metabolised takes progress off the wound; the course stops by itself once nothing is infected.
        var cured = await SecondsUntil(60, () => !SEntMan.System<WolfmedInfectionSystem>().HasInfection(body));
        Assert.That(cured, Is.Not.Null, "the course never cleared the infection.");
        await RunSeconds(2);

        await Server.WaitAssertion(() =>
        {
            var after = SEntMan.TryGetComponent(wound, out WolfmedInfectionComponent? infection) ? infection.Progress : 0f;
            TestContext.Out.WriteLine($"wound infection {before:F1} before the course, {after:F1} once cleared after {cured} s.");
            Assert.Multiple(() =>
            {
                Assert.That(after, Is.LessThan(before), "the course did not bring the infection down.");
                Assert.That(Autodoc.AntibioticsRunning(pod), Is.False, "the course did not stop once the infection was gone.");
                Assert.That(Autodoc.AntibioticFault(pod), Is.False);
            });
        });
    }

    private void LoadBeaker(Entity<AutodocComponent> pod)
    {
        var beaker = SEntMan.SpawnEntity("WolfmedTestSpaceacillinBeaker", SEntMan.GetComponent<TransformComponent>(pod).Coordinates);
        Assert.That(SEntMan.System<ItemSlotsSystem>().TryInsert(pod.Owner, AutodocComponent.ReservoirSlotIds[0], beaker, null),
            Is.True, "the reservoir refused the beaker.");
    }

    /// <summary>Runs a second at a time until the condition holds; the seconds it took, or null past the limit.</summary>
    private async Task<int?> SecondsUntil(int limit, Func<bool> condition)
    {
        for (var second = 1; second <= limit; second++)
        {
            await RunSeconds(1);
            var met = false;
            await Server.WaitPost(() => met = condition());
            if (met)
                return second;
        }

        return null;
    }
}
