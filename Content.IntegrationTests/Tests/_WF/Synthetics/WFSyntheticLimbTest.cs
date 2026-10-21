#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Tests._WF.Wolfmed;
using Content.Server._WF.Synthetics;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Station.Systems;
using Content.Shared._Onyx.Wounds;
using Content.Shared._Shitmed.Targeting;
using Content.Shared._WF.Biotype;
using Content.Shared._WF.Synthetics;
using Content.Shared._WF.Wolfmed.Body;
using Content.Shared._WF.Wolfmed.Compat;
using Content.Shared._WF.Wolfmed.Wounds;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.DoAfter;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Preferences;
using Robust.Shared.GameObjects;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._WF.Synthetics;

/// <summary>Lego limbs: chassis limbs come off sooner than flesh and click back on by hand, loose until coiled.</summary>
[TestFixture]
public sealed class WFSyntheticLimbTest : WolfmedGameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    /// <summary>
    /// A detached arm clicked back on by a crewmate is attached, loose and slow, and its hand (which falls off with it)
    /// goes back on the arm the same way; a cable coil repairs the coupling.
    /// </summary>
    [Test]
    public async Task ReattachByHandThenCoilTest()
    {
        var map = await CreateTestMap();
        EntityUid body = default, user = default, arm = default, hand = default;

        await Server.WaitAssertion(() =>
        {
            body = SpawnSynthetic(map);
            user = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            arm = Part(body, BodyPartType.Arm, BodyPartSymmetry.Left);
            hand = Part(body, BodyPartType.Hand, BodyPartSymmetry.Left);
            Assert.That(SEntMan.System<WolfmedBodySystem>().TryDetachPart(arm), Is.True);
            Assert.That(SEntMan.System<SharedHandsSystem>().TryPickupAnyHand(user, arm), Is.True);
            Assert.That(InteractUsing(user, arm, body, map), Is.True, "the arm did not start going back on.");
            Assert.That(DoAfters(user), Is.GreaterThan(0), "no do-after for the reattach.");
        });

        await RunSeconds(6);

        await Server.WaitAssertion(() =>
        {
            var graph = SEntMan.System<SharedBodySystem>();
            Assert.Multiple(() =>
            {
                Assert.That(graph.BodyHasChild(body, arm), Is.True, "the arm was not reattached.");
                Assert.That(SEntMan.GetComponent<BodyPartComponent>(arm).Enabled, Is.True, "the reattached arm is off.");
                Assert.That(SEntMan.HasComponent<WFLooseLimbComponent>(arm), Is.True, "the arm is not marked loose.");
                Assert.That(Loose(arm), Is.Not.Null, "no loose-coupling wound on the arm.");
                Assert.That(SEntMan.System<WolfmedWoundTraitSystem>().TryGetLimbPenalty(arm, false, out var modifier),
                    Is.True, "the loose arm carries no penalty.");
                Assert.That(modifier, Is.GreaterThan(1f), "the loose arm is not slower to use.");
            });

            // Shitmed drops a detached limb's children on their own, so the hand comes off as a second item.
            Assert.That(graph.BodyHasChild(body, hand), Is.False);
            Assert.That(SEntMan.System<SharedHandsSystem>().TryPickupAnyHand(user, hand), Is.True);
            Assert.That(InteractUsing(user, hand, body, map), Is.True, "the hand did not start going back on.");
        });

        await RunSeconds(6);

        await Server.WaitAssertion(() =>
        {
            var graph = SEntMan.System<SharedBodySystem>();
            Assert.Multiple(() =>
            {
                Assert.That(graph.BodyHasChild(body, hand), Is.True, "the hand was not reattached.");
                Assert.That(graph.GetParentPartOrNull(hand), Is.EqualTo(arm), "the hand went somewhere other than its arm.");
                Assert.That(SEntMan.GetComponent<HandsComponent>(body).Hands, Has.Count.EqualTo(2),
                    "the reattached hand cannot hold anything.");
            });

            var coil = SEntMan.SpawnEntity("CableApcStack", map.GridCoords);
            Assert.That(SEntMan.System<SharedHandsSystem>().TryPickupAnyHand(user, coil), Is.True);
            SEntMan.GetComponent<TargetingComponent>(user).Target = TargetBodyPart.LeftArm;
            var interact = new AfterInteractEvent(user, coil, body, map.GridCoords, true);
            SEntMan.EventBus.RaiseLocalEvent(coil, interact);
            Assert.That(interact.Handled, Is.True, "the coil did not start on the loose arm.");
        });

        await RunSeconds(20);

        await Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(Loose(arm), Is.Null, "the coil left the coupling loose.");
                Assert.That(SEntMan.HasComponent<WFLooseLimbComponent>(arm), Is.False, "the repaired arm is still marked loose.");
            });
        });
    }

    /// <summary>A synthetic body refuses a flesh arm; a flesh body refuses a chassis arm. Neither starts.</summary>
    [Test]
    public async Task ReattachRefusesWrongPartsTest()
    {
        var map = await CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var synthetic = SpawnSynthetic(map);
            var organic = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            var user = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            var detach = SEntMan.System<WolfmedBodySystem>();
            var hands = SEntMan.System<SharedHandsSystem>();

            var fleshArm = Part(organic, BodyPartType.Arm, BodyPartSymmetry.Left);
            Assert.That(detach.TryDetachPart(fleshArm), Is.True);
            Assert.That(detach.TryDetachPart(Part(synthetic, BodyPartType.Arm, BodyPartSymmetry.Left)), Is.True);
            Assert.That(hands.TryPickupAnyHand(user, fleshArm), Is.True);
            Assert.That(InteractUsing(user, fleshArm, synthetic, map), Is.True, "the synthetic body ignored a flesh arm.");
            Assert.That(DoAfters(user), Is.Zero, "a flesh arm started going onto a chassis.");

            // The component is forced on: a flesh body never gets it, and the gate is the biotype regardless.
            SEntMan.EnsureComponent<WFChassisComponent>(organic);
            var chassisArm = SEntMan.SpawnEntity("LeftArmIPC", map.GridCoords);
            Assert.That(hands.TryPickupAnyHand(user, chassisArm), Is.True);
            InteractUsing(user, chassisArm, organic, map);
            Assert.That(DoAfters(user), Is.Zero, "a chassis arm started going onto flesh.");
        });
    }

    /// <summary>Chassis limbs come off sooner than flesh: every threshold is below the organic one.</summary>
    [Test]
    public async Task ChassisLimbsSeverSoonerTest()
    {
        var map = await CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var organic = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            var bodies = new[] { SpawnSynthetic(map), SEntMan.SpawnEntity("MobSynth", map.GridCoords) };
            Assert.Multiple(() =>
            {
                foreach (var type in new[] { BodyPartType.Arm, BodyPartType.Hand, BodyPartType.Leg, BodyPartType.Foot })
                {
                    var flesh = Thresholds(Part(organic, type, BodyPartSymmetry.Left));
                    Assert.That(flesh, Is.Not.Empty, $"the organic {type} has no thresholds to compare with.");
                    foreach (var body in bodies)
                    {
                        var chassis = Thresholds(Part(body, type, BodyPartSymmetry.Left));
                        Assert.That(chassis.Keys, Is.EquivalentTo(flesh.Keys),
                            $"{SEntMan.ToPrettyString(body)}'s {type} severs on different damage types.");
                        foreach (var (damage, line) in flesh)
                        {
                            Assert.That(chassis.GetValueOrDefault(damage), Is.LessThan(line),
                                $"{SEntMan.ToPrettyString(body)}'s {type} is no easier to sever with {damage}.");
                        }
                    }
                }
            });
        });
    }

    private EntityUid SpawnSynthetic(TestMapData map)
    {
        SEntMan.System<AtmosphereSystem>().SetMapAtmosphere(map.MapUid, false, WolfmedIvDripTest.StationAir());
        var profile = HumanoidCharacterProfile.RandomWithSpecies("Human").WithBiotype(WFBiotype.Synthetic);
        var body = SEntMan.System<StationSpawningSystem>().SpawnPlayerMob(map.GridCoords, job: null, profile, station: null);
        // WFChassisPanelSystem puts this on every synthetic body; it is not in this lane.
        SEntMan.EnsureComponent<WFChassisComponent>(body);
        return body;
    }

    private EntityUid Part(EntityUid body, BodyPartType type, BodyPartSymmetry side) =>
        SEntMan.System<SharedBodySystem>().GetBodyChildrenOfType(body, type, symmetry: side).First().Id;

    private Dictionary<string, float> Thresholds(EntityUid part) =>
        SEntMan.GetComponent<WolfmedBodyPartComponent>(part).AmputationThresholds
            .ToDictionary(pair => pair.Key.Id, pair => pair.Value.Float());

    private Entity<WoundComponent>? Loose(EntityUid part)
    {
        foreach (var wound in SEntMan.System<WoundSystem>().GetWounds(part))
        {
            if (wound.Comp.Prototype == WFSyntheticLimbSystem.LooseCouplingWound)
                return wound;
        }

        return null;
    }

    private bool InteractUsing(EntityUid user, EntityUid used, EntityUid target, TestMapData map)
    {
        var interact = new InteractUsingEvent(user, used, target, map.GridCoords);
        SEntMan.EventBus.RaiseLocalEvent(target, interact);
        return interact.Handled;
    }

    private int DoAfters(EntityUid user) =>
        SEntMan.TryGetComponent(user, out DoAfterComponent? doAfter) ? doAfter.DoAfters.Count : 0;
}
