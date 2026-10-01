#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server._WF.Wolfmed.Autodoc;
using Content.Server._WF.Wolfmed.Medical;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Shared._WF.Wolfmed.Autodoc;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.FixedPoint;
using Content.Shared.Stacks;
using NUnit.Framework;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.Wolfmed;

/// <summary>
/// Playtest 4, IV: the pod's blood reservoir. A Bloodpack stack in its blood slot is transfused into a low occupant
/// at the reservoir's rate up to its target once the planner schedules it; with nothing loaded the pod says NO BLOOD
/// LOADED, shows it on the readout, and starts as soon as a stack goes in.
/// </summary>
[TestFixture]
[TestOf(typeof(AutodocSystem))]
public sealed class PodBloodTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: WolfmedTestBloodPod
  parent: WFMachineAutodoc
  suffix: test blood
  components:
  - type: Autodoc
    stepTime: 0.02
    bestStepTime: 0.02
    malfunctionChance: 0
  - type: ApcPowerReceiver
    needsPower: false

- type: entity
  id: WolfmedPodBloodTestHuman
  parent: MobHuman
  components:
  - type: Bloodstream
    bloodRefreshAmount: 0
";

    private AutodocSystem Autodoc => SEntMan.System<AutodocSystem>();

    private BloodstreamSystem Bloodstream => SEntMan.System<BloodstreamSystem>();

    /// <summary>A pod holding a human bled to <paramref name="blood"/>, their own regeneration off, and optionally a stack.</summary>
    private async Task<(Entity<AutodocComponent> Pod, EntityUid Body, EntityUid? Pack)> PodWith(float blood, int packs)
    {
        var map = await Pair.CreateTestMap();
        Entity<AutodocComponent> pod = default;
        EntityUid body = default;
        EntityUid? pack = null;

        await Server.WaitAssertion(() =>
        {
            SEntMan.EnsureComponent<Content.Server._Mono.Cleanup.CleanupImmuneComponent>(map.Grid);
            SEntMan.System<AtmosphereSystem>().SetMapAtmosphere(map.MapUid, false, WolfmedIvDripTest.StationAir());
            var uid = SEntMan.SpawnEntity("WolfmedTestBloodPod", map.GridCoords);
            pod = (uid, SEntMan.GetComponent<AutodocComponent>(uid));

            if (packs > 0)
            {
                pack = SEntMan.SpawnEntity(WolfmedIvDripSystem.PackPrototype, map.GridCoords);
                SEntMan.System<SharedStackSystem>().SetCount(pack.Value, packs);
                Assert.That(SEntMan.System<ItemSlotsSystem>().TryInsert(uid, WolfmedAutodocBloodComponent.SlotId,
                    pack.Value, null), Is.True, "the blood slot refused a Bloodpack stack.");
            }

            body = SEntMan.SpawnEntity("WolfmedPodBloodTestHuman", map.GridCoords);
            FixedPoint2 pool = SEntMan.GetComponent<BloodstreamComponent>(body).BloodMaxVolume;
            Bloodstream.TryModifyBloodLevel(body, FixedPoint2.New(-(1f - blood) * pool.Float()));
            Assert.That(Autodoc.TryInsert(pod, body), Is.True);
        });

        Assert.That(await SecondsUntil(5, () => Autodoc.IsPowered(pod)), Is.Not.Null, "the test pod never powered.");
        return (pod, body, pack);
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

    private AutodocBuiState? Readout(EntityUid pod) =>
        SEntMan.System<SharedUserInterfaceSystem>().TryGetUiState<AutodocBuiState>(pod, AutodocUiKey.Key, out var bui)
            ? bui
            : null;

    /// <summary>
    /// (5a) A human at 0.6 with a stack of five in the blood slot: the plan starts the transfusion, which runs at the
    /// reservoir's rate to its 0.95 target and stops there, spending exactly the packs the blood was worth.
    /// </summary>
    [Test]
    public async Task PodTransfusesALowOccupantFromItsPacksTest()
    {
        var (pod, body, pack) = await PodWith(0.6f, packs: 5);
        float start = 0f;
        FixedPoint2 pool = default;

        await Server.WaitAssertion(() =>
        {
            start = Bloodstream.GetBloodLevelPercentage(body);
            pool = SEntMan.GetComponent<BloodstreamComponent>(body).BloodMaxVolume;
            Assert.That(Autodoc.TryPlan(pod), Is.Zero, "a patient short only of blood got a surgery.");
            Assert.That(Autodoc.BloodRunning(pod), Is.True, "the plan did not start the blood reservoir.");
            Assert.That(Autodoc.BloodFault(pod), Is.False);
        });

        // 0.6 to 0.95 of a 300 u pool is 105 u; at 5 u/s, 21 s.
        var predicted = (0.95f - start) * pool.Float() / 5f;
        var took = await SecondsUntil(60, () => !Autodoc.BloodRunning(pod));
        TestContext.Out.WriteLine($"pod transfusion {start:F2} to its target took {took} s (predicted {predicted:F1} s).");
        Assert.That(took, Is.Not.Null, "the transfusion never finished.");
        Assert.That(took!.Value, Is.InRange(predicted - 1f, predicted + 3f));

        await RunSeconds(3);

        await Server.WaitAssertion(() =>
        {
            var level = Bloodstream.GetBloodLevelPercentage(body);
            var given = (level - start) * pool.Float();
            var blood = SEntMan.GetComponent<WolfmedAutodocBloodComponent>(pod);
            var spent = (5 - SEntMan.GetComponent<StackComponent>(pack!.Value).Count) * SEntMan.System<WolfmedIvDripSystem>().UnitsPerPack
                        - blood.PackOpened;
            TestContext.Out.WriteLine($"level {level:F3}, given {given:F1} u, spent {spent:F1} u.");
            Assert.Multiple(() =>
            {
                Assert.That(level, Is.EqualTo(0.95f).Within(0.005f), "the pod did not stop at its target.");
                Assert.That(spent, Is.EqualTo(given).Within(0.5f), "the packs spent do not match the blood given.");
                Assert.That(Autodoc.BloodRunning(pod), Is.False);
                Assert.That(Readout(pod)?.Reservoir.Any(row => row.Name.Contains("blood pack")), Is.True,
                    "the readout does not name what the blood slot holds.");
            });
        });
    }

    /// <summary>
    /// (5b) Nothing in the blood slot: the plan raises NO BLOOD LOADED once, on the voice and the readout, and gives
    /// nothing; a stack put in afterwards clears the fault and the transfusion starts on its own.
    /// </summary>
    [Test]
    public async Task EmptyReservoirSaysNoBloodLoadedTest()
    {
        var (pod, body, _) = await PodWith(0.6f, packs: 0);
        var level = 0f;

        await Server.WaitAssertion(() =>
        {
            level = Bloodstream.GetBloodLevelPercentage(body);
            Autodoc.TryPlan(pod);
            Assert.That(Autodoc.BloodFault(pod), Is.True, "an empty reservoir raised no fault.");
            Assert.That(Autodoc.BloodRunning(pod), Is.False);
            Autodoc.TryPlan(pod);
        });

        await RunSeconds(3);

        await Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(pod.Comp.VoiceEvents.GetValueOrDefault(AutodocVoiceEvent.NoBlood), Is.EqualTo(1),
                    "NO BLOOD LOADED was not said exactly once.");
                Assert.That(Readout(pod)?.Status, Does.Contain("NO BLOOD LOADED"), "the readout does not show the fault.");
                Assert.That(Readout(pod)?.Status, Does.Not.Contain("TRANSFUSING"), "the readout claims a transfusion.");
                Assert.That(Bloodstream.GetBloodLevelPercentage(body), Is.EqualTo(level).Within(0.001f),
                    "blood arrived from an empty reservoir.");
            });

            var pack = SEntMan.SpawnEntity(WolfmedIvDripSystem.PackPrototype,
                SEntMan.GetComponent<TransformComponent>(pod).Coordinates);
            Assert.That(SEntMan.System<ItemSlotsSystem>().TryInsert(pod.Owner, WolfmedAutodocBloodComponent.SlotId, pack, null),
                Is.True);
        });

        var started = await SecondsUntil(5, () => Autodoc.BloodRunning(pod));
        Assert.That(started, Is.Not.Null, "loading a stack did not start the waiting transfusion.");

        await RunSeconds(3);

        await Server.WaitAssertion(() =>
        {
            Assert.That(Autodoc.BloodFault(pod), Is.False, "the fault stayed up with blood loaded.");
            Assert.That(Bloodstream.GetBloodLevelPercentage(body), Is.GreaterThan(level + 0.03f), "no blood went in.");
            Assert.That(Readout(pod)?.Status, Does.Not.Contain("NO BLOOD LOADED"));
        });
    }

    /// <summary>
    /// Playtest 5: "infinite blood if you use two blood packs". A pack is spent from the stack the moment the pod opens
    /// it and what is left of it stays in the pod, so a stack taken out and put back cannot start over, and the opened
    /// pack keeps running with the slot empty. One pack, then another: two packs' worth, and then NO BLOOD LOADED.
    /// </summary>
    [Test]
    public async Task AnOpenedPackCannotBePutBackFullTest()
    {
        var (pod, body, pack) = await PodWith(0.6f, packs: 1);
        float start = 0f, perPack = 0f;
        FixedPoint2 pool = default;

        await Server.WaitAssertion(() =>
        {
            start = Bloodstream.GetBloodLevelPercentage(body);
            pool = SEntMan.GetComponent<BloodstreamComponent>(body).BloodMaxVolume;
            perPack = SEntMan.System<WolfmedIvDripSystem>().UnitsPerPack;
            Assert.That(Autodoc.TryPlan(pod), Is.Zero);
            Assert.That(Autodoc.BloodRunning(pod), Is.True, "the plan did not start the blood reservoir.");
        });

        // Two seconds at the reservoir's rate: the one pack is opened and a third of it given.
        await RunSeconds(2);

        await Server.WaitAssertion(() =>
        {
            var blood = SEntMan.GetComponent<WolfmedAutodocBloodComponent>(pod);
            var slots = SEntMan.System<ItemSlotsSystem>();
            Assert.Multiple(() =>
            {
                Assert.That(SEntMan.Deleted(pack!.Value), Is.True, "the opened pack was not spent from the stack.");
                Assert.That(slots.GetItemOrNull(pod.Owner, WolfmedAutodocBloodComponent.SlotId), Is.Null, "the slot still holds the spent stack.");
                Assert.That(blood.PackOpened, Is.InRange(1f, perPack - 1f), "what is left of the opened pack is not in the pod.");
                Assert.That(Autodoc.BloodRunning(pod), Is.True, "the opened pack stopped when its stack went.");
            });

            // The second pack goes in while the first is still running.
            var next = SEntMan.SpawnEntity(WolfmedIvDripSystem.PackPrototype, SEntMan.GetComponent<TransformComponent>(pod).Coordinates);
            SEntMan.System<SharedStackSystem>().SetCount(next, 1);
            Assert.That(slots.TryInsert(pod.Owner, WolfmedAutodocBloodComponent.SlotId, next, null), Is.True, "the blood slot refused the second pack.");
        });

        var took = await SecondsUntil(40, () => !Autodoc.BloodRunning(pod));
        Assert.That(took, Is.Not.Null, "the transfusion never stopped.");

        await Server.WaitAssertion(() =>
        {
            var level = Bloodstream.GetBloodLevelPercentage(body);
            var given = (level - start) * pool.Float();
            var blood = SEntMan.GetComponent<WolfmedAutodocBloodComponent>(pod);
            TestContext.Out.WriteLine($"two packs gave {given:F1} u; opened remainder {blood.PackOpened:F1} u.");
            Assert.Multiple(() =>
            {
                Assert.That(given, Is.EqualTo(2 * perPack).Within(1f), "two packs gave something other than two packs' worth.");
                Assert.That(level, Is.LessThan(0.95f), "two packs from 0.6 should not reach the target.");
                Assert.That(Autodoc.BloodFault(pod), Is.True, "with both packs spent the pod does not say NO BLOOD LOADED.");
            });
        });
    }
}
