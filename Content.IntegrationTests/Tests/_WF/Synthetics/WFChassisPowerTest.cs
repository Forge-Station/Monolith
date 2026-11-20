#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Tests._WF.Wolfmed;
using Content.Server._EinsteinEngines.Silicon.Charge;
using Content.Server._HL.Silicons.Synths.Battery;
using Content.Server._WF.Synthetics;
using Content.Server._WF.Wolfmed.Life;
using Content.Server.Power.EntitySystems;
using Content.Server.Station.Systems;
using Content.Shared._EinsteinEngines.Silicon.Components;
using Content.Shared._Onyx.Wounds;
using Content.Shared._WF.Biotype;
using Content.Shared._WF.Synthetics;
using Content.Shared._WF.Wolfmed.Consciousness;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.FixedPoint;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Movement.Components;
using Content.Shared.Power.Components;
using Content.Shared.Preferences;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WF.Synthetics;

/// <summary>
/// Chassis power: the cell pulled and seated through the bay on both kinds of chassis, low power in place of a
/// shutdown, and the sparks an overheating overclock throws.
/// </summary>
[TestFixture]
public sealed class WFChassisPowerTest : WolfmedGameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    private const string SyntheticHuman = "SyntheticHuman";
    private const string Synth = "MobSynth";

    /// <summary>
    /// Bay open, the cell comes out into an empty hand and the chassis drops to low power: up, crawling, one arm.
    /// Seated again, it is back to full speed on both arms.
    /// </summary>
    [TestCase(SyntheticHuman)]
    [TestCase(Synth)]
    public async Task PullAndSeatCellTest(string kind)
    {
        var map = await CreateTestMap();
        EntityUid body = default, user = default, cell = default;
        var rated = 0f;

        await Server.WaitAssertion(() =>
        {
            body = SpawnBody(kind, map.GridCoords);
            user = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
        });

        // A synth's starting cell is seated on its battery system's first update.
        await RunSeconds(1.5f);

        await Server.WaitAssertion(() =>
        {
            OpenBay(body);
            rated = Sprint(body);
            Assert.That(SEntMan.System<WFChassisPanelSystem>().TryGetCell(body, out var found), "no cell in the chassis.");
            cell = found!.Value.Owner;
            Assert.That(SEntMan.System<WFChassisBaySystem>().GetComponents(body), Does.Contain(cell),
                "the cell is not offered as a component.");

            var row = SEntMan.System<WFChassisPanelSystem>().BuildState(body).Organs.Single(o => o.Slot == "battery");
            Assert.That(row.Organ, Is.EqualTo(SEntMan.GetNetEntity(cell)));
            Assert.That(row.CanDisconnect, "the panel does not offer to disconnect the cell with the bay open.");

            // The panel's own message, carrying the cell, as the window sends it.
            var message = new WFChassisDisconnectOrganMessage(SEntMan.GetNetEntity(cell))
            {
                Actor = user,
                UiKey = WFChassisPanelUiKey.Key,
                Entity = SEntMan.GetNetEntity(body),
            };
            SEntMan.EventBus.RaiseLocalEvent(body, (object) message);
        });

        await RunSeconds(3);

        await Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(SEntMan.System<SharedHandsSystem>().IsHolding(user, cell, out _), Is.True,
                    "the pulled cell is not in the puller's hand.");
                AssertLowPower(body, rated);
                Assert.That(SEntMan.System<WFChassisPanelSystem>().BuildState(body).Status,
                    Is.EqualTo("wf-chassis-status-low-power"));
            });

            Assert.That(InteractUsing(user, cell, body, map.GridCoords), Is.True, "the cell did not start going back in.");
        });

        await RunSeconds(3);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.System<WFChassisPanelSystem>().TryGetCell(body, out var seated), "the cell was not seated.");
            Assert.Multiple(() =>
            {
                Assert.That(seated!.Value.Owner, Is.EqualTo(cell));
                Assert.That(SEntMan.HasComponent<WFLowPowerComponent>(body), Is.False, "a charged cell left it on low power.");
                Assert.That(Arms(body).Count(arm => arm.Comp.Enabled), Is.EqualTo(2), "power back, but not both arms.");
                Assert.That(Arms(body).Any(arm => SEntMan.HasComponent<WFIsolatedPartComponent>(arm)), Is.False);
                Assert.That(Sprint(body), Is.EqualTo(rated).Within(0.01f), "power back, but still slow.");
            });
        });
    }

    /// <summary>A cell run flat in place is low power too, and no dying actions come with it.</summary>
    [TestCase(SyntheticHuman)]
    [TestCase(Synth)]
    public async Task DrainedCellIsLowPowerTest(string kind)
    {
        var map = await CreateTestMap();
        EntityUid body = default;
        var rated = 0f;

        await Server.WaitAssertion(() => body = SpawnBody(kind, map.GridCoords));
        await RunSeconds(1.5f);

        await Server.WaitAssertion(() =>
        {
            rated = Sprint(body);
            Assert.That(SEntMan.System<WFChassisPanelSystem>().TryGetCell(body, out var cell), "no cell in the chassis.");
            SEntMan.System<BatterySystem>().SetCharge(cell!.Value.Owner, 0f, cell.Value.Comp);

            // The IPC charge loop skips a body with no mind; this is the call it would make.
            if (SEntMan.TryGetComponent(body, out SiliconComponent? silicon))
                SEntMan.System<SiliconChargeSystem>().UpdateChargeState(body, 0, silicon);
        });

        await RunSeconds(2);

        await Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                AssertLowPower(body, rated);
                Assert.That(SEntMan.HasComponent<Content.Server._WF.Wolfmed.Life.WolfmedDyingActionsComponent>(body),
                    Is.False, "low power granted the dying actions.");
                Assert.That(SEntMan.System<WFChassisPanelSystem>().SetOverclock(body, true, body), Is.False,
                    "overclocked on an empty cell.");
            });
        });
    }

    /// <summary>
    /// On low power the unpowered arm reads offline on the panel, not isolated, and the owner can route the power
    /// to it; after that the switch waits out its cooldown.
    /// </summary>
    [Test]
    public async Task SwitchPoweredArmTest()
    {
        var map = await CreateTestMap();
        EntityUid body = default;

        await Server.WaitAssertion(() =>
        {
            body = SpawnBody(SyntheticHuman, map.GridCoords);
            Assert.That(SEntMan.System<WFLowPowerSystem>().SetLowPower(body, true), "a synthetic body refused low power.");

            var low = SEntMan.GetComponent<WFLowPowerComponent>(body);
            var powered = low.PoweredArm!.Value;
            var other = Arms(body).Single(arm => arm.Owner != powered).Owner;
            var entry = SEntMan.System<WFChassisPanelSystem>().BuildState(body).Parts
                .Single(p => p.Part == SEntMan.GetNetEntity(other));
            Assert.Multiple(() =>
            {
                Assert.That(entry.Isolated, Is.False, "the unpowered arm reads as isolated by the user.");
                Assert.That(entry.Working, Is.False, "the unpowered arm reads as working.");
            });

            // Restoring it from the panel is refused in effect: it stays dark.
            SEntMan.System<WFChassisPanelSystem>().SetIsolated(body, other, false, body);
            Assert.That(SEntMan.GetComponent<BodyPartComponent>(other).Enabled, Is.False, "the panel restored an unpowered arm.");

            var lowPower = SEntMan.System<WFLowPowerSystem>();
            Assert.That(lowPower.TrySwitchArm(body, other), "the switch to the other arm was refused.");
            Assert.Multiple(() =>
            {
                Assert.That(SEntMan.GetComponent<BodyPartComponent>(other).Enabled, Is.True, "the switched-to arm is off.");
                Assert.That(SEntMan.GetComponent<BodyPartComponent>(powered).Enabled, Is.False, "the old arm still works.");
                Assert.That(lowPower.TrySwitchArm(body, powered), Is.False, "the switch ignored its cooldown.");
            });
        });

        await RunSeconds(3.5f);

        await Server.WaitAssertion(() =>
        {
            var low = SEntMan.GetComponent<WFLowPowerComponent>(body);
            var back = Arms(body).Single(arm => arm.Owner != low.PoweredArm).Owner;
            Assert.That(SEntMan.System<WFLowPowerSystem>().TrySwitchArm(body, back), "the cooldown never ended.");
            SEntMan.System<WFLowPowerSystem>().SetLowPower(body, false);
            Assert.That(Arms(body).Count(arm => arm.Comp.Enabled), Is.EqualTo(2), "leaving low power left an arm off.");
        });
    }

    /// <summary>
    /// Overclocked on a full cell, the chassis heats up, sparks, and shorts a limb before the heat cut-off. The torso
    /// starts at 20 heat, the start of the shorting band, so a minute of game time covers the band.
    /// </summary>
    [Test]
    public async Task OverclockSparksTest()
    {
        var map = await CreateTestMap();
        EntityUid body = default;
        var sparks = 0;
        var shorted = false;
        var startHeat = FixedPoint2.Zero;
        var maxHeat = FixedPoint2.Zero;

        void Count(Entity<MetaDataComponent> spawned)
        {
            if (spawned.Comp.EntityPrototype?.ID == WFChassisPanelSystem.SparkEffect.Id)
                sparks++;
        }

        await Server.WaitAssertion(() =>
        {
            body = SpawnBody(SyntheticHuman, map.GridCoords);
            var torso = SEntMan.System<SharedBodySystem>().GetBodyChildrenOfType(body, BodyPartType.Torso).First().Id;
            SEntMan.System<WoundSystem>().CreateOrMergeWound(torso, WFChassisPanelSystem.OverheatingWound, FixedPoint2.New(20));
            startHeat = TorsoHeat(body);
            Assert.That(SEntMan.System<WFChassisPanelSystem>().SetOverclock(body, true, body), "overclock refused.");
            SEntMan.EntityInitialized += Count;
        });

        try
        {
            for (var second = 0; second < 90; second++)
            {
                await RunSeconds(1);
                var running = true;
                await Server.WaitPost(() =>
                {
                    maxHeat = FixedPoint2.Max(maxHeat, TorsoHeat(body));
                    shorted |= SEntMan.System<SharedBodySystem>().GetBodyChildren(body)
                        .Any(part => SEntMan.System<WoundSystem>().GetWounds(part.Id)
                            .Any(w => w.Comp.Prototype == WFChassisPanelSystem.ShortCircuitWound));
                    running = SEntMan.GetComponent<WFChassisComponent>(body).Overclocked;
                });

                if (!running)
                    break;
            }
        }
        finally
        {
            await Server.WaitPost(() => SEntMan.EntityInitialized -= Count);
        }

        TestContext.Out.WriteLine($"Overclock: {sparks} sparks, heat {startHeat} to {maxHeat}, shorted {shorted}.");
        Assert.Multiple(() =>
        {
            Assert.That(sparks, Is.GreaterThan(0), "an overheating overclock threw no sparks.");
            Assert.That(shorted, "an overheating overclock never shorted a limb.");
            Assert.That(maxHeat, Is.GreaterThan(startHeat), "the overclock put no heat in.");
        });
    }

    private void AssertLowPower(EntityUid body, float rated)
    {
        Assert.That(SEntMan.HasComponent<WFLowPowerComponent>(body), "the chassis is not on low power.");
        Assert.That(SEntMan.System<WolfmedShutdownSystem>().IsShutDown(body), Is.False, "an empty cell shut it down.");
        Assert.That(SEntMan.GetComponent<WolfmedConsciousnessComponent>(body).State, Is.EqualTo(WolfmedConsciousness.Up),
            "low power took it off its feet.");
        Assert.That(Arms(body).Count(arm => arm.Comp.Enabled), Is.EqualTo(1), "not exactly one arm working.");
        Assert.That(Sprint(body) / rated, Is.LessThanOrEqualTo(0.3f), "low power is not slow enough.");
        Assert.That(Sprint(body), Is.GreaterThan(0f), "low power froze it in place.");
    }

    /// <summary>A synthetic-biotype human the way a station spawns one, or a plain mob.</summary>
    private EntityUid SpawnBody(string kind, EntityCoordinates coords)
    {
        if (kind != SyntheticHuman)
            return SEntMan.SpawnEntity(kind, coords);

        var profile = HumanoidCharacterProfile.RandomWithSpecies("Human").WithBiotype(WFBiotype.Synthetic);
        return SEntMan.System<StationSpawningSystem>().SpawnPlayerMob(coords, job: null, profile, station: null);
    }

    private void OpenBay(EntityUid body) =>
        SEntMan.System<WFChassisBaySystem>().SetBay((body, SEntMan.EnsureComponent<WFChassisComponent>(body)), true);

    private float Sprint(EntityUid body) => SEntMan.GetComponent<MovementSpeedModifierComponent>(body).SprintSpeedModifier;

    private List<Entity<BodyPartComponent>> Arms(EntityUid body) =>
        SEntMan.System<SharedBodySystem>().GetBodyChildrenOfType(body, BodyPartType.Arm)
            .Select(arm => new Entity<BodyPartComponent>(arm.Id, arm.Component))
            .ToList();

    private FixedPoint2 TorsoHeat(EntityUid body)
    {
        var torso = SEntMan.System<SharedBodySystem>().GetBodyChildrenOfType(body, BodyPartType.Torso).First().Id;
        return SEntMan.System<WoundSystem>().GetWounds(torso)
            .Where(w => w.Comp.Prototype == WFChassisPanelSystem.OverheatingWound)
            .Select(w => w.Comp.Severity)
            .DefaultIfEmpty(FixedPoint2.Zero)
            .Max();
    }

    private bool InteractUsing(EntityUid user, EntityUid used, EntityUid target, EntityCoordinates coords)
    {
        var interact = new InteractUsingEvent(user, used, target, coords);
        SEntMan.EventBus.RaiseLocalEvent(target, interact);
        return interact.Handled;
    }
}
