#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server.Atmos.EntitySystems;
using Content.Server._WF.Wolfmed.Consciousness;
using Content.Shared._Onyx.Wounds;
using Content.Shared._WF.Wolfmed.CCVar;
using Content.Shared._WF.Wolfmed.Consciousness;
using Content.Shared._WF.Wolfmed.Reagents;
using Content.Shared.ActionBlocker;
using Content.Shared.Actions;
using Content.Shared.Atmos.Components;
using Content.Shared.Body.Components;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Movement.Components;
using Content.Shared.Stunnable;
using NUnit.Framework;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.Wolfmed.Scenarios;

/// <summary>
/// Playtest 2: "I stood in fire and my hands stopped working, I couldn't crawl at all." A human in a 10-stack
/// fire, sampled every 5 s for two minutes. A Downed body can always crawl, use its carried items on itself
/// and Call for help, however burned (plan §2.2), until it is Unconscious or its parts are actually gone.
/// </summary>
[TestFixture]
[TestOf(typeof(WolfmedDownedSystem))]
public sealed class WolfmedFireHelplessnessTest : GameTest
{
    private static readonly (string Name, BodyPartType Type, BodyPartSymmetry Symmetry)[] Limbs =
    [
        ("LA", BodyPartType.Arm, BodyPartSymmetry.Left),
        ("RA", BodyPartType.Arm, BodyPartSymmetry.Right),
        ("LH", BodyPartType.Hand, BodyPartSymmetry.Left),
        ("RH", BodyPartType.Hand, BodyPartSymmetry.Right),
        ("LL", BodyPartType.Leg, BodyPartSymmetry.Left),
        ("RL", BodyPartType.Leg, BodyPartSymmetry.Right),
        ("LF", BodyPartType.Foot, BodyPartSymmetry.Left),
        ("RF", BodyPartType.Foot, BodyPartSymmetry.Right),
    ];

    private async Task Pin()
    {
        await OverrideCVar(Side.Server, WolfmedCVars.Consciousness, true);
        await OverrideCVar(Side.Server, WolfmedCVars.ConsciousnessPainDown, 0.95f);
        await OverrideCVar(Side.Server, WolfmedCVars.ConsciousnessPainOut, 1.4f);
        await OverrideCVar(Side.Server, WolfmedCVars.ConsciousnessBloodDown, 0.5f);
        await OverrideCVar(Side.Server, WolfmedCVars.ConsciousnessBloodOut, 0.35f);
        await OverrideCVar(Side.Server, WolfmedCVars.PainFaintSeconds, 20f);
        await OverrideCVar(Side.Server, WolfmedCVars.PainFaintRise, 40f);
        await OverrideCVar(Side.Server, WolfmedCVars.PainFaintCooldown, 50f);
        await OverrideCVar(Side.Server, WolfmedCVars.AmbientPartCapFraction, 0.8f);
        await OverrideCVar(Side.Server, WolfmedCVars.BurnFluidRate, 1f);
        await OverrideCVar(Side.Server, WolfmedCVars.CharEscalation, 1f);
        await OverrideCVar(Side.Server, WolfmedCVars.PainkillerAbsorbSeconds, 4f);
    }

    private bool HasCallAction(EntityUid body) =>
        SEntMan.System<SharedActionsSystem>().GetActions(body).Any(action =>
            SEntMan.GetComponent<MetaDataComponent>(action.Id).EntityPrototype?.ID ==
            WolfmedCallForHelpSystem.CallAction.Id);

    private EntityUid? Limb(EntityUid body, BodyPartType type, BodyPartSymmetry symmetry) =>
        SEntMan.System<SharedBodySystem>().GetBodyChildren(body)
            .Where(p => p.Component.PartType == type && p.Component.Symmetry == symmetry)
            .Select(p => (EntityUid?) p.Id)
            .FirstOrDefault();

    private float Severity(EntityUid part, string prototype) =>
        SEntMan.System<WoundSystem>().GetWounds(part)
            .Where(w => w.Comp.Prototype.Id == prototype)
            .Sum(w => w.Comp.Severity.Float());

    /// <summary>One row: state, movement, hands, and per limb "enabled/functionality stored burn/char".</summary>
    private sealed record Sample(
        int Second,
        WolfmedConsciousness State,
        WolfmedCause Cause,
        float Walk,
        bool CanMove,
        bool Stunned,
        int Hands,
        bool CanInteract,
        bool CallForHelp,
        int Legs,
        int CrumbledHands,
        string Limbs);

    private Sample Record(int second, EntityUid body)
    {
        var consc = SEntMan.GetComponent<WolfmedConsciousnessComponent>(body);
        var move = SEntMan.GetComponent<MovementSpeedModifierComponent>(body);
        var blocker = SEntMan.System<ActionBlockerSystem>();
        var hands = SEntMan.GetComponentOrNull<HandsComponent>(body);
        var functionality = SEntMan.System<BodyPartFunctionalitySystem>();

        var limbs = new StringBuilder();
        var crumbled = 0;
        foreach (var (name, type, symmetry) in Limbs)
        {
            if (Limb(body, type, symmetry) is not { } part)
            {
                limbs.Append($"{name}:gone ");
                if (type == BodyPartType.Hand)
                    crumbled++;
                continue;
            }

            var bodyPart = SEntMan.GetComponent<BodyPartComponent>(part);
            var stored = SEntMan.GetComponent<Content.Shared.Damage.DamageableComponent>(part).TotalDamage.Float();
            var state = functionality.GetState(part) switch
            {
                BodyPartFunctionalityState.Functional => "F",
                BodyPartFunctionalityState.Impaired => "I",
                BodyPartFunctionalityState.Disabled => "D",
                _ => "U",
            };
            limbs.Append($"{name}:{(bodyPart.Enabled ? "on" : "OFF")}/{state} {stored:0} " +
                         $"b{Severity(part, "BurnWound"):0} c{Severity(part, "WFWolfmedCharringWound"):0} ");
        }

        return new Sample(second, consc.State, consc.Cause, move.CurrentWalkSpeed, blocker.CanMove(body),
            SEntMan.HasComponent<StunnedComponent>(body), hands?.Hands.Count ?? 0, blocker.CanInteract(body, null), HasCallAction(body),
            SEntMan.GetComponent<BodyComponent>(body).LegEntities.Count, crumbled, limbs.ToString().TrimEnd());
    }

    /// <summary>
    /// The 120 s fire. At every Downed sample the body moves (speed above 0, CanMove), keeps a usable hand
    /// for each hand it still has, and has Call for help; at 60 s an analgesic pen picked up from the floor
    /// injects the body it is used on.
    /// </summary>
    [Test]
    public async Task FireHelplessnessTest()
    {
        await Pin();
        var map = await Pair.CreateTestMap();
        var s = new WolfmedScenario(SEntMan);
        EntityUid body = default;

        await Server.WaitPost(() =>
        {
            s.SetAir(map.MapUid, true);
            s.KeepGrid(map.Grid);
            body = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
        });
        await RunSeconds(2);

        var normalWalk = 0f;
        await Server.WaitPost(() =>
        {
            normalWalk = SEntMan.GetComponent<MovementSpeedModifierComponent>(body).CurrentWalkSpeed;
            SEntMan.System<FlammableSystem>().SetFireStacks(body, 10, ignite: true);
        });

        var samples = new List<Sample>();
        bool? penPicked = null;
        var penTier = WolfmedPainReliefTier.None;
        EntityUid pen = default;

        for (var second = 0; second <= 120; second += 5)
        {
            var now = second;
            await Server.WaitPost(() =>
            {
                samples.Add(Record(now, body));
                if (now != 60)
                    return;

                pen = SEntMan.SpawnEntity("WFWolfmedAnalgesicPen", SEntMan.GetComponent<TransformComponent>(body).Coordinates);
                penPicked = SEntMan.System<SharedHandsSystem>().TryPickupAnyHand(body, pen);
                if (penPicked == true)
                    SEntMan.System<SharedInteractionSystem>().UseInHandInteraction(body, pen);
            });

            if (now == 60)
            {
                await RunSeconds(1);
                await Server.WaitPost(() => penTier = SEntMan.System<WolfmedPainReliefSystem>().GetTier(body));
                await RunSeconds(4);
            }
            else if (now < 120)
            {
                await RunSeconds(5);
            }
        }

        TestContext.Out.WriteLine($"FireHelplessness: normal walk {normalWalk:0.00}; pen picked up {penPicked}, " +
                                  $"tier after 1 s {penTier}.");
        TestContext.Out.WriteLine("t | state/cause | walk | CanMove | stunned | hands | CanInteract | call | legs | limbs");
        foreach (var row in samples)
        {
            TestContext.Out.WriteLine($"{row.Second} | {row.State}/{row.Cause} | {row.Walk:0.000} | {row.CanMove} | {row.Stunned} | " +
                                      $"{row.Hands} | {row.CanInteract} | {row.CallForHelp} | {row.Legs} | {row.Limbs}");
        }

        Assert.Multiple(() =>
        {
            foreach (var row in samples.Where(r => r.State == WolfmedConsciousness.Downed))
            {
                Assert.That(row.Walk, Is.GreaterThan(0f), $"{row.Second} s: a Downed body could not crawl.");
                // The pain shock's 2 s stun is the one thing allowed to hold a Downed body still for a moment.
                Assert.That(row.CanMove || row.Stunned, Is.True, $"{row.Second} s: a Downed body was blocked from moving.");
                Assert.That(row.Hands, Is.EqualTo(2 - row.CrumbledHands),
                    $"{row.Second} s: a hand that is still attached stopped working.");
                Assert.That(row.CallForHelp, Is.True, $"{row.Second} s: a Downed body had no Call for help.");
            }

            Assert.That(samples.Any(r => r.State == WolfmedConsciousness.Downed), Is.True,
                "the fire never Downed the body; the scenario tests nothing.");
            Assert.That(penPicked, Is.True, "the body could not pick up the pen at 60 s.");
            Assert.That(penTier, Is.Not.EqualTo(WolfmedPainReliefTier.None), "the pen did not inject its user at 60 s.");
        });
    }
}
