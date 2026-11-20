#nullable enable
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._WF.Wolfmed.Autodoc;
using Content.Server._WF.Wolfmed.Consciousness;
using Content.Server._WF.Wolfmed.Life;
using Content.Shared._Shitmed.Body.Organ;
using Content.Shared._Shitmed.Targeting;
using Content.Shared._WF.Wolfmed.Autodoc;
using Content.Shared._WF.Wolfmed.CCVar;
using Content.Shared._WF.Wolfmed.Consciousness;
using Content.Shared.Atmos.Rotting;
using Content.Shared.Bed.Sleep;
using Content.Shared.Body.Systems;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Wolfmed;

/// <summary>
/// M1a package C, the autodoc rows of plan §7.2: a pain faint is Critical now, but it is seconds long, so the
/// pod keeps anaesthetising through it and does not sound its Critical alarm for it. The pod refuses a rotten
/// corpse and a heartless body in the hand defibrillator's words.
/// </summary>
[TestFixture]
[TestOf(typeof(AutodocSystem))]
public sealed class WolfmedAutodocFaintTest : GameTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: WolfmedFaintTestAutodoc
  parent: WFMachineAutodoc
  suffix: faint test
  components:
  - type: Autodoc
    stepTime: 0.02
    bestStepTime: 0.02
    malfunctionChance: 0
    autoPlanInterval: 0.2
  - type: ApcPowerReceiver
    needsPower: false

- type: entity
  id: WolfmedFaintTestOpiateJug
  parent: Jug
  suffix: opiate
  components:
  - type: SolutionContainerManager
    solutions:
      beaker:
        maxVol: 200
        reagents:
        - ReagentId: WFWolfmedOpiate
          Quantity: 100
";

    private async Task Pin()
    {
        await OverrideCVar(Side.Server, WolfmedCVars.Consciousness, true);
        await OverrideCVar(Side.Server, WolfmedCVars.ConsciousnessPainOut, 1.4f);
        await OverrideCVar(Side.Server, WolfmedCVars.PainFaintSeconds, 20f);
    }

    private Entity<AutodocComponent> Pod(TestMapData map)
    {
        var pod = SEntMan.SpawnEntity("WolfmedFaintTestAutodoc", map.GridCoords);
        return (pod, SEntMan.GetComponent<AutodocComponent>(pod));
    }

    /// <summary>Two broken legs: about 200 summed pain, past the 189 faint line.</summary>
    private void BreakLegs(EntityUid body)
    {
        var spec = new DamageSpecifier { DamageDict = { [new ProtoId<DamageTypePrototype>("Blunt")] = FixedPoint2.New(60) } };
        // Playtest 4: a leg holds 90 pain at most, so the two legs alone no longer reach the 189 faint line; the torso
        // makes up the rest.
        foreach (var target in new[] { TargetBodyPart.LeftLeg, TargetBodyPart.RightLeg, TargetBodyPart.Torso })
            SEntMan.System<DamageableSystem>().TryChangeDamage(body, spec, origin: null, targetPart: target);
    }

    /// <summary>
    /// A fainted occupant is anaesthetised: the pod used to skip every Critical body, so a patient out for a
    /// few seconds from pain woke up on the table.
    /// </summary>
    [Test]
    public async Task FaintedOccupantIsAnaesthetisedTest()
    {
        await Pin();
        var map = await Pair.CreateTestMap();
        Entity<AutodocComponent> pod = default;
        EntityUid body = default;

        await Server.WaitAssertion(() =>
        {
            var autodoc = SEntMan.System<AutodocSystem>();
            body = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            BreakLegs(body);

            pod = Pod(map);
            Assert.That(SEntMan.System<ItemSlotsSystem>().TryInsert(pod.Owner, AutodocComponent.ReservoirSlotIds[0],
                SEntMan.SpawnEntity("WolfmedFaintTestOpiateJug", map.GridCoords), null), Is.True);
            Assert.That(autodoc.TryInsert(pod, body), Is.True);
        });
        await RunTicksSync(10);

        await Server.WaitAssertion(() =>
        {
            var autodoc = SEntMan.System<AutodocSystem>();
            Assert.That(SEntMan.System<WolfmedConsciousnessSystem>().InFaint(body), Is.True,
                "the fixture needs a fainted patient.");

            foreach (var target in new[] { TargetBodyPart.LeftLeg, TargetBodyPart.RightLeg })
                autodoc.TryQueue(pod, "WFSurgeryMendFracture", target);

            Assert.That(autodoc.TryStart(pod, null), Is.True);
        });
        await RunTicksSync(30);

        await Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(pod.Comp!.AnaestheticGiven, Is.True, "the pod gave a fainted patient no anaesthetic.");
                Assert.That(SEntMan.HasComponent<ForcedSleepingComponent>(body), Is.True,
                    "a fainted patient was left to wake up on the table.");
            });
        });
    }

    /// <summary>
    /// <see cref="AutodocAlarm.Critical"/> is for somebody who is really out: not for a faint, which ends by
    /// itself in seconds. Anything else holding the body under still sounds it.
    /// </summary>
    [Test]
    public async Task FaintDoesNotSoundTheCriticalAlarmTest()
    {
        await Pin();
        var map = await Pair.CreateTestMap();

        EntityUid body = default;
        await Server.WaitAssertion(() =>
        {
            body = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            Assert.That(SEntMan.System<AutodocSystem>().TryInsert(Pod(map), body), Is.True);
            BreakLegs(body);
        });

        // The fractures' pain reaches the body over the next ticks.
        await RunTicksSync(10);

        await Server.WaitAssertion(() =>
        {
            var autodoc = SEntMan.System<AutodocSystem>();
            Assert.That(SEntMan.System<MobStateSystem>().IsCritical(body), Is.True, "the faint is not Critical.");
            Assert.That(SEntMan.GetComponent<WolfmedConsciousnessComponent>(body).Cause, Is.EqualTo(WolfmedCause.PainFaint));
            Assert.That(autodoc.GetAlarm(body), Is.Not.EqualTo(AutodocAlarm.Critical), "a faint sounded the Critical alarm.");

            SEntMan.System<WolfmedConsciousnessSystem>().SetExternalPressure(body, "test", 1f);
            Assert.That(autodoc.GetAlarm(body), Is.EqualTo(AutodocAlarm.Critical),
                "a body held under by something else stopped sounding the alarm.");
        });
    }

    /// <summary>
    /// The pod asks the same refusal the paddles do (plan §7.2): a rotten corpse and a body with no heart are
    /// refused, in the hand defibrillator's own words, and the pod never charges.
    /// </summary>
    [Test]
    public async Task PodRefusesRotAndHeartlessBodiesTest()
    {
        await Pin();
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var autodoc = SEntMan.System<AutodocSystem>();
            var life = SEntMan.System<WolfmedLifeSystem>();
            var slots = SEntMan.System<ItemSlotsSystem>();

            // Rotten: dead, and rot has set in.
            var rotten = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            Assert.That(life.Kill(rotten), Is.True);
            SEntMan.EnsureComponent<RottingComponent>(rotten);
            var first = Pod(map);
            Assert.That(slots.TryInsert(first.Owner, AutodocComponent.ModuleSlotId,
                SEntMan.SpawnEntity("WFAutodocDefibModule", map.GridCoords), null), Is.True);
            Assert.That(autodoc.TryInsert(first, rotten), Is.True);
            Assert.That(autodoc.TryDefibrillateOccupant(first, rotten), Is.False);
            Assert.Multiple(() =>
            {
                Assert.That(first.Comp.DefibBlocked, Is.EqualTo(WolfmedRevivalSystem.Rotten),
                    "the pod did not refuse a rotten corpse in the paddles' words.");
                Assert.That(first.Comp.DefibAttempt, Is.Zero, "the pod charged for a rotten corpse.");
            });

            // Heartless: the heart out of the chest stops it, and no shock is worth giving.
            var heartless = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            var body = SEntMan.System<SharedBodySystem>();
            var heart = body.GetBodyOrgans(heartless).First(organ => SEntMan.HasComponent<HeartComponent>(organ.Id)).Id;
            Assert.That(body.RemoveOrgan(heart), Is.True);
            Assert.That(life.InArrest(heartless), Is.True);

            var second = Pod(map);
            Assert.That(slots.TryInsert(second.Owner, AutodocComponent.ModuleSlotId,
                SEntMan.SpawnEntity("WFAutodocDefibModule", map.GridCoords), null), Is.True);
            Assert.That(autodoc.TryInsert(second, heartless), Is.True);
            Assert.That(autodoc.TryDefibrillateOccupant(second, heartless), Is.False);
            Assert.Multiple(() =>
            {
                Assert.That(second.Comp.DefibBlocked, Is.EqualTo(WolfmedRevivalSystem.NoHeart),
                    "the pod did not refuse a heartless body in the paddles' words.");
                Assert.That(second.Comp.DefibAttempt, Is.Zero, "the pod charged for a heartless body.");
            });
        });
    }
}
