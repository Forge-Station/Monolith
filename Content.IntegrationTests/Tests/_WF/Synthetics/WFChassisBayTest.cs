#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Tests._WF.Wolfmed;
using Content.Server._HL.Silicons.Synths.Battery;
using Content.Server._WF.Synthetics;
using Content.Server._WF.Wolfmed.Life;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Station.Systems;
using Content.Shared._Shitmed.Body.Organ;
using Content.Shared._WF.Biotype;
using Content.Shared._WF.Synthetics;
using Content.Shared._WF.Wolfmed.Body;
using Content.Shared.Body.Organ;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.DoAfter;
using Content.Shared.Examine;
using Content.Shared.FixedPoint;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Movement.Components;
using Content.Shared.Preferences;
using Content.Shared.Verbs;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._WF.Synthetics;

/// <summary>The chassis bay: opened by verb, components pulled and seated by hand, refused to flesh.</summary>
[TestFixture]
public sealed class WFChassisBayTest : WolfmedGameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    /// <summary>Another crewmate opens a synthetic human's bay by verb; it shows on examine and exposes the torso.</summary>
    [Test]
    public async Task BayOpensAndClosesByVerbTest()
    {
        var map = await CreateTestMap();
        EntityUid body = default, user = default, torso = default;
        Dictionary<string, FixedPoint2> reach = new();

        await Server.WaitAssertion(() =>
        {
            (body, user) = SpawnPair(map);
            torso = Part(body, BodyPartType.Torso);
            reach = SEntMan.GetComponent<WolfmedBodyPartComponent>(torso).OrganReach
                .ToDictionary(pair => pair.Key.Id, pair => pair.Value);
            Assert.That(reach, Is.Not.Empty, "the chassis torso has no reach lines to expose.");
            RunVerb(body, user, "wf-chassis-bay-verb-open");
        });

        await RunSeconds(4);

        await Server.WaitAssertion(() =>
        {
            var lines = SEntMan.GetComponent<WolfmedBodyPartComponent>(torso).OrganReach;
            Assert.Multiple(() =>
            {
                Assert.That(SEntMan.GetComponent<WFChassisComponent>(body).BayOpen, Is.True, "the verb did not open the bay.");
                Assert.That(lines.Keys.Select(key => key.Id), Is.EquivalentTo(reach.Keys), "the open bay changed which types reach.");
                Assert.That(lines.Values, Has.All.EqualTo(FixedPoint2.Zero), "the open bay still shields its components.");
                Assert.That(Examine(body, user), Does.Contain("chassis bay is open"), "the open bay does not show on examine.");
            });

            RunVerb(body, user, "wf-chassis-bay-verb-close");
        });

        await RunSeconds(4);

        await Server.WaitAssertion(() =>
        {
            var lines = SEntMan.GetComponent<WolfmedBodyPartComponent>(torso).OrganReach;
            Assert.Multiple(() =>
            {
                Assert.That(SEntMan.GetComponent<WFChassisComponent>(body).BayOpen, Is.False, "the verb did not close the bay.");
                Assert.That(lines.ToDictionary(pair => pair.Key.Id, pair => pair.Value), Is.EquivalentTo(reach),
                    "closing the bay did not give the torso its reach lines back.");
                Assert.That(SEntMan.HasComponent<WFChassisBayExposedComponent>(torso), Is.False);
                Assert.That(Examine(body, user), Does.Not.Contain("chassis bay is open"));
            });
        });
    }

    /// <summary>
    /// An open bay slows the chassis. The speed handler is WFChassisPanelSystem's (Lane A), so this fails until that
    /// lands beside this system.
    /// </summary>
    [Test]
    public async Task OpenBaySlowsTest()
    {
        var map = await CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var (body, _) = SpawnPair(map);
            var movement = SEntMan.GetComponent<MovementSpeedModifierComponent>(body);
            var before = movement.SprintSpeedModifier;
            var chassis = SEntMan.GetComponent<WFChassisComponent>(body);
            SEntMan.System<WFChassisBaySystem>().SetBay((body, chassis), true);
            Assert.That(movement.SprintSpeedModifier, Is.LessThan(before), "an open bay does not slow the chassis.");
            SEntMan.System<WFChassisBaySystem>().SetBay((body, chassis), false);
            Assert.That(movement.SprintSpeedModifier, Is.EqualTo(before).Within(0.001f), "closing the bay left it slow.");
        });
    }

    /// <summary>The pump comes out into an empty hand and the chassis shuts down; seated again, it runs.</summary>
    [Test]
    public async Task PullAndSeatPumpTest()
    {
        var map = await CreateTestMap();
        EntityUid body = default, user = default, pump = default;

        await Server.WaitAssertion(() =>
        {
            (body, user) = SpawnPair(map);
            OpenBay(body);
            pump = SEntMan.System<SharedBodySystem>().GetBodyOrgans(body)
                .Single(organ => SEntMan.HasComponent<HeartComponent>(organ.Id)).Id;
            Assert.That(SEntMan.System<WolfmedShutdownSystem>().IsShutDown(body), Is.False);
            RunVerb(body, user, "wf-chassis-bay-verb-pull", pump);
        });

        await RunSeconds(3);

        await Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(SEntMan.System<SharedHandsSystem>().IsHolding(user, pump, out _), Is.True,
                    "the pulled pump is not in the puller's hand.");
                Assert.That(SEntMan.System<WolfmedShutdownSystem>().IsShutDown(body), Is.True,
                    "the chassis kept running without its pump.");
            });

            Assert.That(InteractUsing(user, pump, body, map.GridCoords), Is.True, "the pump did not start going back in.");
        });

        await RunSeconds(3);

        await Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(SEntMan.GetComponent<OrganComponent>(pump).Body, Is.EqualTo(body),
                    "the pump was not seated.");
                Assert.That(SEntMan.System<WolfmedShutdownSystem>().IsShutDown(body), Is.False,
                    "the chassis did not restart with its pump back.");
            });
        });
    }

    /// <summary>A synth's power cell comes out of its battery slot and goes back in.</summary>
    [Test]
    public async Task PullAndSeatSynthCellTest()
    {
        var map = await CreateTestMap();
        EntityUid body = default, user = default, cell = default;

        await Server.WaitAssertion(() =>
        {
            SEntMan.System<AtmosphereSystem>().SetMapAtmosphere(map.MapUid, false, WolfmedIvDripTest.StationAir());
            body = SEntMan.SpawnEntity("MobSynth", map.GridCoords);
            user = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            SEntMan.EnsureComponent<WFChassisComponent>(body);
            OpenBay(body);
        });

        // SynthBatteryPowerSystem seats the starting cell on its first update.
        await RunSeconds(1);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.System<SynthBatterySystem>().TryGetBattery(body, out var battery), Is.True,
                "the synth spawned without a cell.");
            cell = battery!.Value.Owner;
            Assert.That(SEntMan.System<WFChassisBaySystem>().GetComponents(body), Does.Contain(cell),
                "the cell is not offered as a component.");
            Assert.That(SEntMan.System<WFChassisBaySystem>().TryStartPull(body, cell, user), Is.True);
        });

        await RunSeconds(3);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.System<SynthBatterySystem>().TryGetBattery(body, out _), Is.False, "the cell is still in.");
            Assert.That(SEntMan.System<SharedHandsSystem>().IsHolding(user, cell, out _), Is.True);
            Assert.That(InteractUsing(user, cell, body, map.GridCoords), Is.True, "the cell did not start going back in.");
        });

        await RunSeconds(3);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.System<SynthBatterySystem>().TryGetBattery(body, out var battery), Is.True,
                "the cell was not seated.");
            Assert.That(battery!.Value.Owner, Is.EqualTo(cell));
        });
    }

    /// <summary>Seating refuses a shut bay and a taken slot, without starting anything.</summary>
    [Test]
    public async Task InstallRefusesClosedBayAndFullSlotTest()
    {
        var map = await CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var (body, user) = SpawnPair(map);
            var spare = SEntMan.SpawnEntity("OrganIPCPump", map.GridCoords);
            Assert.That(SEntMan.System<SharedHandsSystem>().TryPickupAnyHand(user, spare), Is.True);

            Assert.That(InteractUsing(user, spare, body, map.GridCoords), Is.True, "the closed bay did not answer.");
            Assert.That(DoAfters(user), Is.Zero, "seating started into a closed bay.");

            OpenBay(body);
            Assert.That(InteractUsing(user, spare, body, map.GridCoords), Is.True, "the open bay did not answer.");
            Assert.That(DoAfters(user), Is.Zero, "seating started into a slot that already has a pump.");
            Assert.That(SEntMan.GetComponent<OrganComponent>(spare).Body, Is.Null);
        });
    }

    /// <summary>Flesh has no bay: no verb, and the interactions refuse even with the component forced on.</summary>
    [Test]
    public async Task OrganicBodyHasNoBayTest()
    {
        var map = await CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            SEntMan.System<AtmosphereSystem>().SetMapAtmosphere(map.MapUid, false, WolfmedIvDripTest.StationAir());
            var body = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            var user = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            Assert.That(VerbTexts(body, user), Has.None.Contains("chassis bay"), "an organic body offers a bay verb.");

            SEntMan.EnsureComponent<WFChassisComponent>(body);
            Assert.That(VerbTexts(body, user), Has.None.Contains("chassis bay"), "an organic body offers a bay verb.");
            var bay = SEntMan.System<WFChassisBaySystem>();
            Assert.That(bay.TryStartToggle(body, user, true), Is.False, "an organic body's bay started opening.");

            var heart = SEntMan.System<SharedBodySystem>().GetBodyOrgans(body)
                .First(organ => SEntMan.HasComponent<HeartComponent>(organ.Id)).Id;
            Assert.That(bay.TryStartPull(body, heart, user), Is.False, "a heart started coming out of an organic body.");

            var spare = SEntMan.SpawnEntity("OrganIPCPump", map.GridCoords);
            Assert.That(SEntMan.System<SharedHandsSystem>().TryPickupAnyHand(user, spare), Is.True);
            InteractUsing(user, spare, body, map.GridCoords);
            Assert.That(DoAfters(user), Is.Zero, "seating started into an organic body.");
        });
    }

    /// <summary>A synthetic-biotype human and an organic crewmate beside it, on an aired map.</summary>
    private (EntityUid Body, EntityUid User) SpawnPair(TestMapData map)
    {
        SEntMan.System<AtmosphereSystem>().SetMapAtmosphere(map.MapUid, false, WolfmedIvDripTest.StationAir());
        var profile = HumanoidCharacterProfile.RandomWithSpecies("Human").WithBiotype(WFBiotype.Synthetic);
        var body = SEntMan.System<StationSpawningSystem>().SpawnPlayerMob(map.GridCoords, job: null, profile, station: null);
        var user = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
        // WFChassisPanelSystem puts this on every synthetic body; it is not in this lane.
        SEntMan.EnsureComponent<WFChassisComponent>(body);
        return (body, user);
    }

    private void OpenBay(EntityUid body) =>
        SEntMan.System<WFChassisBaySystem>().SetBay((body, SEntMan.GetComponent<WFChassisComponent>(body)), true);

    private EntityUid Part(EntityUid body, BodyPartType type) =>
        SEntMan.System<SharedBodySystem>().GetBodyChildrenOfType(body, type).First().Id;

    private List<string> VerbTexts(EntityUid target, EntityUid user) =>
        SEntMan.System<SharedVerbSystem>().GetLocalVerbs(target, user, typeof(AlternativeVerb))
            .Select(verb => verb.Text).ToList();

    /// <summary>Runs the alternative verb whose text is this loc key (with the item, for a pull).</summary>
    private void RunVerb(EntityUid target, EntityUid user, string key, EntityUid? item = null)
    {
        var loc = Server.ResolveDependency<ILocalizationManager>();
        var text = item is { } entity ? loc.GetString(key, ("item", entity)) : loc.GetString(key);
        var verb = SEntMan.System<SharedVerbSystem>().GetLocalVerbs(target, user, typeof(AlternativeVerb))
            .FirstOrDefault(verb => verb.Text == text);
        Assert.That(verb, Is.Not.Null, $"no \"{text}\" verb.");
        SEntMan.System<SharedVerbSystem>().ExecuteVerb(verb!, user, target, true);
    }

    private bool InteractUsing(EntityUid user, EntityUid used, EntityUid target, EntityCoordinates coords)
    {
        var interact = new InteractUsingEvent(user, used, target, coords);
        SEntMan.EventBus.RaiseLocalEvent(target, interact);
        return interact.Handled;
    }

    private int DoAfters(EntityUid user) =>
        SEntMan.TryGetComponent(user, out DoAfterComponent? doAfter) ? doAfter.DoAfters.Count : 0;

    private string Examine(EntityUid target, EntityUid examiner) =>
        SEntMan.System<ExamineSystemShared>().GetExamineText(target, examiner).ToMarkup();
}
