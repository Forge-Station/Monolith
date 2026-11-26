#nullable enable
using System.Linq;
using Content.IntegrationTests.Tests._WF.Wolfmed;
using Content.Server._WF.Synthetics;
using Content.Server.Power.EntitySystems;
using Content.Server.Station.Systems;
using Content.Shared._Onyx.Wounds;
using Content.Shared._Shitmed.Targeting;
using Content.Shared._WF.Biotype;
using Content.Shared._WF.Synthetics;
using Content.Shared._WF.Wolfmed.Consciousness;
using Content.Shared._WF.Wolfmed.Hud;
using Content.Shared.Actions;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Movement.Components;
using Content.Shared.Power.Components;
using Content.Shared.Preferences;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WF.Synthetics;

/// <summary>The chassis panel: who gets it, and what isolate, detach, reboot and overclock do to the body.</summary>
[TestFixture]
public sealed class WFChassisPanelTest : WolfmedGameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    private const string SyntheticHuman = "SyntheticHuman";

    /// <summary>A synthetic-biotype human the way a station spawns one, or a plain mob.</summary>
    private EntityUid SpawnBody(string kind, EntityCoordinates coords)
    {
        if (kind != SyntheticHuman)
            return SEntMan.SpawnEntity(kind, coords);

        var profile = HumanoidCharacterProfile.RandomWithSpecies("Human").WithBiotype(WFBiotype.Synthetic);
        return SEntMan.System<StationSpawningSystem>().SpawnPlayerMob(coords, job: null, profile, station: null);
    }

    [TestCase(SyntheticHuman)]
    [TestCase("MobIPC")]
    public async Task SyntheticGetsPanelTest(string kind)
    {
        var map = await CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var body = SpawnBody(kind, map.GridCoords);
            var ui = SEntMan.System<UserInterfaceSystem>();
            var granted = SEntMan.GetComponent<ActionsComponent>(body).Actions;

            Assert.Multiple(() =>
            {
                Assert.That(SEntMan.HasComponent<WFChassisComponent>(body), "no chassis on a synthetic body.");
                Assert.That(SEntMan.TryGetComponent(body, out WFChassisPanelActionsComponent? actions) &&
                            actions.Action is { } action && SEntMan.EntityExists(action) &&
                            granted.Contains(action),
                    "the panel action was not granted.");
                Assert.That(ui.HasUi(body, WFChassisPanelUiKey.Key), "the panel UI is not registered.");
            });
        });
    }

    [Test]
    public async Task OrganicGetsNoPanelTest()
    {
        var map = await CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var body = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            Assert.Multiple(() =>
            {
                Assert.That(SEntMan.HasComponent<WFChassisComponent>(body), Is.False, "an organic body got a chassis.");
                Assert.That(SEntMan.HasComponent<WFChassisPanelActionsComponent>(body), Is.False,
                    "an organic body got the panel action.");
            });
        });
    }

    /// <summary>An isolated leg puts no pain on the body and stays off through damage; restoring gives both back.</summary>
    [TestCase(SyntheticHuman)]
    [TestCase("MobIPC")]
    public async Task IsolateZeroesPainTest(string kind)
    {
        var map = await CreateTestMap();
        EntityUid body = default;
        EntityUid leg = default;
        var hurt = FixedPoint2.Zero;

        await Server.WaitAssertion(() =>
        {
            body = SpawnBody(kind, map.GridCoords);
            leg = Part(body, BodyPartType.Leg, BodyPartSymmetry.Left);
            Assert.That(SEntMan.System<WoundDamageRoutingSystem>()
                .TryApplyPartDamage(body, leg, Spec("Slash", 40), ignoreResistances: true));

            var pain = SEntMan.System<PainSystem>();
            hurt = pain.GetPain(body);
            Assert.That(pain.GetRawPain(leg), Is.GreaterThan(FixedPoint2.Zero), "40 Slash put no pain on the leg.");

            Assert.That(SEntMan.System<WFChassisPanelSystem>().SetIsolated(body, leg, true, body), "isolate refused.");
            Assert.Multiple(() =>
            {
                Assert.That(pain.GetPain(body), Is.EqualTo(OtherPartsPain(body, leg)),
                    "the body still carries the isolated leg's pain.");
                Assert.That(pain.GetPain(body), Is.LessThan(hurt), "isolating the leg took no pain off the body.");
                Assert.That(SEntMan.GetComponent<BodyPartFunctionalityComponent>(leg).State,
                    Is.EqualTo(BodyPartFunctionalityState.Disabled), "the isolated leg does not read Disabled.");
                Assert.That(SEntMan.GetComponent<BodyPartComponent>(leg).Enabled, Is.False, "the isolated leg is enabled.");
            });

            Assert.That(SEntMan.System<WoundDamageRoutingSystem>()
                .TryApplyPartDamage(body, leg, Spec("Blunt", 2), ignoreResistances: true));
            Assert.Multiple(() =>
            {
                Assert.That(SEntMan.GetComponent<BodyPartComponent>(leg).Enabled, Is.False,
                    "a damage change switched the isolated leg back on.");
                Assert.That(pain.GetRawPain(leg), Is.EqualTo(FixedPoint2.Zero), "the isolated leg felt the new hit.");
            });
        });

        await RunSeconds(1);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<WolfmedSyntheticHudComponent>(body).Faults.Select(f => f.Line),
                Does.Contain("wolfmed-synthetic-line-isolated"), "the readout does not say the leg is isolated.");

            var pain = SEntMan.System<PainSystem>();
            var isolated = pain.GetPain(body);
            Assert.That(SEntMan.System<WFChassisPanelSystem>().SetIsolated(body, leg, false, body), "restore refused.");
            Assert.Multiple(() =>
            {
                Assert.That(pain.GetPain(body), Is.GreaterThan(isolated), "restoring the leg brought no pain back.");
                Assert.That(SEntMan.GetComponent<BodyPartComponent>(leg).Enabled, Is.True, "the restored leg is off.");
                Assert.That(SEntMan.GetComponent<BodyPartFunctionalityComponent>(leg).State,
                    Is.Not.EqualTo(BodyPartFunctionalityState.Disabled), "the restored leg still reads Disabled.");
                Assert.That(SEntMan.HasComponent<WFIsolatedPartComponent>(leg), Is.False);
            });
        });
    }

    /// <summary>Both legs isolated drops the body; one back stands it up.</summary>
    [Test]
    public async Task IsolateBothLegsDownsTest()
    {
        var map = await CreateTestMap();
        EntityUid body = default;
        EntityUid left = default;

        await Server.WaitAssertion(() =>
        {
            body = SpawnBody(SyntheticHuman, map.GridCoords);
            left = Part(body, BodyPartType.Leg, BodyPartSymmetry.Left);
            var right = Part(body, BodyPartType.Leg, BodyPartSymmetry.Right);
            var panel = SEntMan.System<WFChassisPanelSystem>();
            Assert.That(panel.SetIsolated(body, left, true, body));
            Assert.That(panel.SetIsolated(body, right, true, body));
        });

        await RunSeconds(1);

        await Server.WaitAssertion(() =>
        {
            var consciousness = SEntMan.GetComponent<WolfmedConsciousnessComponent>(body);
            Assert.Multiple(() =>
            {
                Assert.That(consciousness.State, Is.EqualTo(WolfmedConsciousness.Downed), "two isolated legs left the body up.");

                // Playtest: the panel has to stay reachable from the floor, or a downed chassis can never restore a leg.
                var open = new WFOpenChassisPanelActionEvent { Performer = body };
                SEntMan.EventBus.RaiseLocalEvent(body, open);
                Assert.That(SEntMan.System<SharedUserInterfaceSystem>().IsUiOpen(body, WFChassisPanelUiKey.Key),
                    "a downed chassis could not open its panel.");
                Assert.That(consciousness.Cause, Is.EqualTo(WolfmedCause.Legs), "the body is down for some other reason.");
            });
            Assert.That(SEntMan.System<WFChassisPanelSystem>().SetIsolated(body, left, false, body));
        });

        await RunSeconds(3);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<WolfmedConsciousnessComponent>(body).State,
                Is.EqualTo(WolfmedConsciousness.Up), "restoring a leg did not stand the body up.");
        });
    }

    /// <summary>Detaching from the panel drops the limb whole, with no stump on the body.</summary>
    [Test]
    public async Task DetachDropsLimbWithoutStumpTest()
    {
        var map = await CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var body = SpawnBody(SyntheticHuman, map.GridCoords);
            var arm = Part(body, BodyPartType.Arm, BodyPartSymmetry.Left);
            var torso = Part(body, BodyPartType.Torso, BodyPartSymmetry.None);
            var panel = SEntMan.System<WFChassisPanelSystem>();
            Assert.That(panel.SetIsolated(body, arm, true, body));
            Assert.That(panel.TryDetach(body, torso, body), Is.False, "the panel detached the torso.");
            Assert.That(panel.TryDetach(body, arm, body), "detach refused.");

            var stump = SEntMan.GetComponent<WoundHostComponent>(body).DismembermentWound;
            Assert.Multiple(() =>
            {
                Assert.That(SEntMan.GetComponent<BodyPartComponent>(arm).Body, Is.Null, "the arm is still on the body.");
                Assert.That(SEntMan.System<SharedContainerSystem>().IsEntityInContainer(arm), Is.False,
                    "the arm did not land on the floor.");
                Assert.That(SEntMan.HasComponent<WFIsolatedPartComponent>(arm), Is.False,
                    "the arm left still isolated.");
                Assert.That(SEntMan.System<WoundSystem>().GetWounds(torso).Select(w => w.Comp.Prototype),
                    Does.Not.Contain(stump), "detaching left a dismemberment wound.");

                // Playtest: the state threw on an empty slot, so the panel froze on its last good picture.
                var state = panel.BuildState(body);
                Assert.That(state.Parts.Single(p => p.Slot == TargetBodyPart.LeftArm).Attached, Is.False,
                    "the panel still lists the detached arm as attached.");
                Assert.That(state.Parts.Single(p => p.Slot == TargetBodyPart.LeftHand).Attached, Is.False,
                    "the panel lists the hand that left with the arm as attached.");
                Assert.That(state.Parts, Has.Count.EqualTo(10), "a detached limb dropped its slot from the panel.");
            });
        });
    }

    /// <summary>A reboot holds the frame down, then clears every part's pain.</summary>
    [Test]
    public async Task RebootClearsPainTest()
    {
        var map = await CreateTestMap();
        EntityUid body = default;
        var hurt = FixedPoint2.Zero;
        var charge = 0f;
        var before = FixedPoint2.Zero;

        await Server.WaitAssertion(() =>
        {
            body = SpawnBody(SyntheticHuman, map.GridCoords);
            var leg = Part(body, BodyPartType.Leg, BodyPartSymmetry.Left);
            Assert.That(SEntMan.System<WoundDamageRoutingSystem>()
                .TryApplyPartDamage(body, leg, Spec("Slash", 40), ignoreResistances: true));
            hurt = SEntMan.System<PainSystem>().GetPain(body);
            Assert.That(hurt, Is.GreaterThan(FixedPoint2.Zero), "40 Slash put no pain on the body.");

            var panel = SEntMan.System<WFChassisPanelSystem>();
            Assert.That(panel.TryGetCell(body, out var cell), "no cell found on the chassis.");
            charge = cell!.Value.Comp.CurrentCharge;
            Assert.That(panel.TryReboot(body, body), "reboot refused.");
            Assert.That(panel.TryReboot(body, body), Is.False, "a second reboot started on top of the first.");
            Assert.That(cell.Value.Comp.CurrentCharge, Is.LessThan(charge - cell.Value.Comp.MaxCharge * 0.09f),
                "the reboot spent no charge.");
        });

        await RunSeconds(1);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<WFChassisRebootComponent>(body), "the reboot ended early.");
            Assert.That(SEntMan.GetComponent<WolfmedSyntheticHudComponent>(body).Shutdown,
                "the readout is not on standby during the reboot.");
            before = SEntMan.System<PainSystem>().GetPain(body);
            Assert.That(before, Is.GreaterThan(FixedPoint2.Zero));
        });

        await RunSeconds(3.5f);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<WFChassisRebootComponent>(body), Is.False, "the reboot never finished.");
            Assert.That(SEntMan.System<PainSystem>().GetPain(body), Is.LessThan(before * 0.5f),
                "the reboot did not clear the chassis's pain.");
            Assert.That(SEntMan.GetComponent<WolfmedSyntheticHudComponent>(body).Shutdown, Is.False,
                "the readout stayed on standby after the reboot.");
        });
    }

    /// <summary>Overclocking speeds the chassis up, heats it, and drops out by itself on a low cell.</summary>
    [Test]
    public async Task OverclockSpeedAndLowChargeTest()
    {
        var map = await CreateTestMap();
        EntityUid body = default;
        var rated = 0f;

        await Server.WaitAssertion(() =>
        {
            body = SpawnBody(SyntheticHuman, map.GridCoords);
            var panel = SEntMan.System<WFChassisPanelSystem>();
            rated = SEntMan.GetComponent<MovementSpeedModifierComponent>(body).SprintSpeedModifier;
            Assert.That(panel.SetOverclock(body, true, body), "overclock refused.");
            Assert.That(SEntMan.GetComponent<MovementSpeedModifierComponent>(body).SprintSpeedModifier,
                Is.GreaterThan(rated), "overclocking did not speed the chassis up.");
        });

        // The tick is due at 5 s and polled every 0.5 s, so 5.5 s was a coin toss in a pooled run.
        await RunSeconds(7f);

        await Server.WaitAssertion(() =>
        {
            var torso = Part(body, BodyPartType.Torso, BodyPartSymmetry.None);
            Assert.That(SEntMan.System<WoundSystem>().GetWounds(torso).Select(w => w.Comp.Prototype.Id),
                Does.Contain(WFChassisPanelSystem.OverheatingWound.Id), "overclocking put no heat in the torso.");

            Assert.That(SEntMan.System<WFChassisPanelSystem>().TryGetCell(body, out var cell));
            SEntMan.System<BatterySystem>().SetCharge(cell!.Value.Owner, cell.Value.Comp.MaxCharge * 0.1f, cell.Value.Comp);
        });

        await RunSeconds(1);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<WFChassisComponent>(body).Overclocked, Is.False,
                "overclock stayed on with the cell at 10%.");
            Assert.That(SEntMan.GetComponent<MovementSpeedModifierComponent>(body).SprintSpeedModifier,
                Is.LessThanOrEqualTo(rated), "the overclock speed outlived the overclock.");
        });
    }

    /// <summary>The panel lists every part slot in order and every component slot by name.</summary>
    [Test]
    public async Task StateListsPartsAndOrgansTest()
    {
        var map = await CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var body = SpawnBody(SyntheticHuman, map.GridCoords);
            var state = SEntMan.System<WFChassisPanelSystem>().BuildState(body);

            Assert.That(state.Parts.Select(p => p.Slot), Is.EqualTo(new[]
            {
                TargetBodyPart.Head, TargetBodyPart.Torso, TargetBodyPart.LeftArm, TargetBodyPart.RightArm,
                TargetBodyPart.LeftHand, TargetBodyPart.RightHand, TargetBodyPart.LeftLeg, TargetBodyPart.RightLeg,
                TargetBodyPart.LeftFoot, TargetBodyPart.RightFoot,
            }));
            Assert.That(state.Parts.All(p => p.Attached && p.Working && p.Part != null), "an intact chassis lists a part off.");
            Assert.That(state.Parts.Where(p => p.Slot is TargetBodyPart.Head or TargetBodyPart.Torso).Any(p => p.CanDetach),
                Is.False, "the panel offers to detach the head or torso.");

            var slots = state.Organs.Select(o => o.Slot).ToList();
            Assert.That(slots, Is.SupersetOf(new[] { "eyes", "posbrain", "pump", "battery" }));
            Assert.Multiple(() =>
            {
                foreach (var organ in state.Organs.Where(o => o.Organ != null))
                    Assert.That(organ.Name, Is.Not.Empty, $"the {organ.Slot} slot has no name.");

                var battery = state.Organs.First(o => o.Slot == "battery");
                Assert.That(battery.Organ, Is.Not.Null, "the cell is not listed.");
                Assert.That(battery.Health, Is.GreaterThan(0f), "the cell reads no charge.");
                Assert.That(state.Organs.Where(o => o.Vital).Select(o => o.Slot), Is.EquivalentTo(new[] { "posbrain", "pump" }));
                Assert.That(state.Status, Is.EqualTo("wf-chassis-status-nominal"));
            });
        });
    }

    /// <summary>The panel's messages and a mechanic's multitool reach the same controls.</summary>
    [Test]
    public async Task MessagesAndMultitoolLinkTest()
    {
        var map = await CreateTestMap();
        EntityUid body = default;
        EntityUid mechanic = default;

        await Server.WaitAssertion(() =>
        {
            body = SpawnBody(SyntheticHuman, map.GridCoords);
            var leg = Part(body, BodyPartType.Leg, BodyPartSymmetry.Right);
            var message = new WFChassisIsolatePartMessage(SEntMan.GetNetEntity(leg), true)
            {
                Actor = body,
                UiKey = WFChassisPanelUiKey.Key,
                Entity = SEntMan.GetNetEntity(body),
            };
            SEntMan.EventBus.RaiseLocalEvent(body, (object) message);
            Assert.That(SEntMan.HasComponent<WFIsolatedPartComponent>(leg), "the isolate message did nothing.");

            mechanic = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            var tool = SEntMan.SpawnEntity("Multitool", map.GridCoords);
            Assert.That(SEntMan.System<SharedHandsSystem>().TryPickupAnyHand(mechanic, tool));
            SEntMan.System<SharedInteractionSystem>().InteractUsing(mechanic, tool, body, map.GridCoords);
        });

        await RunSeconds(3);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.System<UserInterfaceSystem>().IsUiOpen(body, WFChassisPanelUiKey.Key, mechanic),
                "the multitool did not link the mechanic into the panel.");
        });
    }

    private FixedPoint2 OtherPartsPain(EntityUid body, EntityUid except)
    {
        var total = FixedPoint2.Zero;
        foreach (var (part, _) in SEntMan.System<SharedBodySystem>().GetBodyChildren(body))
        {
            if (part != except && SEntMan.TryGetComponent(part, out PainComponent? pain))
                total += pain.Value;
        }

        return total;
    }

    private EntityUid Part(EntityUid body, BodyPartType type, BodyPartSymmetry symmetry) =>
        SEntMan.System<SharedBodySystem>().GetBodyChildrenOfType(body, type, symmetry: symmetry).First().Id;

    private DamageSpecifier Spec(string type, float amount) =>
        new(SProtoMan.Index<DamageTypePrototype>(type), FixedPoint2.New(amount));
}
