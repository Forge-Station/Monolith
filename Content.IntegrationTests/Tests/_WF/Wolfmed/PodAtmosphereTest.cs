#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Pair;
using Content.Server._WF.Wolfmed.Autodoc;
using Content.Server._WF.Wolfmed.Life;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Server.Temperature.Components;
using Content.Shared._Shitmed.Targeting;
using Content.Shared._WF.Wolfmed.Autodoc;
using Content.Shared.Atmos;
using Content.Shared.Body.Part;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.DoAfter;
using Content.Shared.Emag.Components;
using Content.Shared.Emag.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Item.ItemToggle;
using Content.Shared.Power.EntitySystems;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Wolfmed;

/// <summary>
/// Playtest 3: the autodoc's own atmosphere. A sealed pod (an occupant under the lid, powered, an intact hull) keeps
/// its patient breathing and at a normal temperature in vacuum or in a hot room; losing power or the hull hands them
/// the room's air until it comes back; a welder closes a breach; and the marker changes nothing about surgery.
/// </summary>
[TestFixture]
[TestOf(typeof(AutodocSystem))]
public sealed class PodAtmosphereTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: WolfmedTestAirPod
  parent: WFMachineAutodoc
  suffix: test air
  components:
  - type: Autodoc
    stepTime: 0.02
    bestStepTime: 0.02
    malfunctionChance: 0
  - type: ApcPowerReceiver
    needsPower: false
";

    /// <summary>A human's surface in ordinary air sits near its 310 K normal; this band is "normal" for these tests.</summary>
    private const float NormalLow = 300f;

    private const float NormalHigh = 320f;

    /// <summary>"Within a few breaths": the respirator's cycle is 2 s and it counts three short ones as suffocating.</summary>
    private const int SuffocateWithin = 45;

    private const int RecoverWithin = 60;

    private readonly List<string> _notes = new();

    private void Note(string line) => _notes.Add(line);

    [TearDown]
    public void WriteNotes()
    {
        foreach (var line in _notes)
            TestContext.Out.WriteLine(line);
    }

    private AutodocSystem Autodoc => SEntMan.System<AutodocSystem>();

    private WolfmedBreathingSystem Breathing => SEntMan.System<WolfmedBreathingSystem>();

    private WolfmedBodyTemperatureSystem BodyTemperature => SEntMan.System<WolfmedBodyTemperatureSystem>();

    /// <summary>
    /// A pod with a human lying in it on a fresh map whose air is <paramref name="air"/>, or vacuum for null. The map
    /// is kept from Mono's grid cleanup, which would otherwise delete a minute-long test's grid and everyone on it.
    /// </summary>
    private async Task<(TestMapData Map, Entity<AutodocComponent> Pod, EntityUid Body)> PodIn(GasMixture? air)
    {
        var map = await Pair.CreateTestMap();
        Entity<AutodocComponent> pod = default;
        EntityUid body = default;

        await Server.WaitAssertion(() =>
        {
            var atmos = SEntMan.System<AtmosphereSystem>();
            if (air == null)
                atmos.SetMapAtmosphere(map.MapUid, true, GasMixture.SpaceGas);
            else
                atmos.SetMapAtmosphere(map.MapUid, false, air);

            SEntMan.EnsureComponent<Content.Server._Mono.Cleanup.CleanupImmuneComponent>(map.Grid);
            var uid = SEntMan.SpawnEntity("WolfmedTestAirPod", map.GridCoords);
            pod = (uid, SEntMan.GetComponent<AutodocComponent>(uid));
            body = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            Assert.That(Autodoc.TryInsert(pod, body), Is.True);
        });

        // The power net writes Powered on its own half-second tick, so the pod is not sealed the instant it spawns.
        Assert.That(await SecondsUntil(5, () => Autodoc.IsPowered(pod)), Is.Not.Null, "the test pod never powered.");
        return (map, pod, body);
    }

    /// <summary>Station air's moles at a temperature.</summary>
    private static GasMixture AirAt(float kelvin)
    {
        var moles = new float[Atmospherics.AdjustedNumberOfGases];
        moles[(int) Gas.Oxygen] = 21.824779f;
        moles[(int) Gas.Nitrogen] = 82.10312f;
        return new GasMixture(moles, kelvin);
    }

    /// <summary>A body out in the same air as the pod, far off the grid, to show the room is what the test says.</summary>
    private async Task<EntityUid> Control(TestMapData map)
    {
        EntityUid control = default;
        await Server.WaitPost(() =>
            control = SEntMan.SpawnEntity("MobHuman", new MapCoordinates(new Vector2(30f, 30f), map.MapId)));
        return control;
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

    private float Surface(EntityUid body) => SEntMan.GetComponent<TemperatureComponent>(body).CurrentTemperature;

    private FixedPoint2 Damage(EntityUid uid, string type) =>
        SEntMan.GetComponent<DamageableComponent>(uid).Damage.DamageDict.TryGetValue(type, out var amount)
            ? amount
            : FixedPoint2.Zero;

    private string DamageText(EntityUid uid)
    {
        var parts = new List<string>();
        foreach (var (type, amount) in SEntMan.GetComponent<DamageableComponent>(uid).Damage.DamageDict)
        {
            if (amount > FixedPoint2.Zero)
                parts.Add($"{type} {amount}");
        }

        return parts.Count == 0 ? "none" : string.Join(", ", parts);
    }

    /// <summary>What the pod's window was last told about the seal.</summary>
    private WolfmedAutodocSeal? Readout(EntityUid pod) =>
        SEntMan.System<SharedUserInterfaceSystem>().TryGetUiState<AutodocBuiState>(pod, AutodocUiKey.Key, out var bui)
            ? bui.Seal
            : null;

    private string ReadoutText(EntityUid pod) =>
        Readout(pod) is { } seal
            ? Server.ResolveDependency<ILocalizationManager>().GetString(WolfmedAutodocSealText.Readout(seal))
            : string.Empty;

    /// <summary>
    /// (1) A sealed pod in vacuum: after a minute the occupant has not suffocated, has taken no damage and is at a
    /// normal temperature, while a body out in the same vacuum suffocates and freezes. The pod's air is the fixed mix,
    /// and whatever the occupant breathes out into it is gone by the next read. The marker leaves with the body.
    /// </summary>
    [Test]
    public async Task SealedPodInVacuumTest()
    {
        var (map, pod, body) = await PodIn(null);
        var control = await Control(map);

        // Whatever the vacuum did in the half second before the power net's first tick is not the sealed pod's doing.
        var sealedDamage = FixedPoint2.Zero;
        await Server.WaitAssertion(() =>
        {
            Assert.That(Autodoc.GetSeal(pod), Is.EqualTo(WolfmedAutodocSeal.Sealed), "a powered pod with a patient.");
            Assert.That(SEntMan.HasComponent<WolfmedAutodocOccupantComponent>(body), Is.True, "no marker on insertion.");
            sealedDamage = SEntMan.GetComponent<DamageableComponent>(body).TotalDamage;
            Note($"sealed in vacuum, at the seal: {DamageText(body)}");
        });

        await RunSeconds(60);

        await Server.WaitAssertion(() =>
        {
            var respirator = SEntMan.GetComponent<RespiratorComponent>(body);
            var levels = BodyTemperature.GetLevels(body);
            Note($"sealed in vacuum, 60 s: surface {Surface(body):0.0} K, core {BodyTemperature.GetCore(body):0.0} K, " +
                 $"saturation {respirator.Saturation:0.00}, damage {DamageText(body)}; " +
                 $"control surface {Surface(control):0.0} K, asphyxiation {Damage(control, "Asphyxiation")}");

            Assert.Multiple(() =>
            {
                Assert.That(Breathing.IsSuffocating(body), Is.False, "the sealed occupant is suffocating.");
                Assert.That(respirator.SuffocationCycles, Is.Zero, "the sealed occupant missed a breath.");
                Assert.That(SEntMan.GetComponent<DamageableComponent>(body).TotalDamage, Is.LessThanOrEqualTo(sealedDamage),
                    "vacuum reached the sealed occupant (pressure, cold or air).");
                Assert.That(Surface(body), Is.InRange(NormalLow, NormalHigh), "the occupant's temperature is not normal.");
                Assert.That(levels.ColdDown, Is.Zero, "the core registers cold.");
                Assert.That(Readout(pod), Is.EqualTo(WolfmedAutodocSeal.Sealed));
                Assert.That(ReadoutText(pod), Is.EqualTo("SEALED"));

                Assert.That(Breathing.IsSuffocating(control), Is.True, "the control in the same vacuum is breathing.");
                Assert.That(Surface(control), Is.LessThan(Surface(body) - 50f), "the control in the same vacuum is warm.");
            });

            // Exhaled gas is scrubbed back to the fixed mix on the next read.
            var atmosphere = SEntMan.GetComponent<WolfmedAutodocAtmosphereComponent>(pod);
            atmosphere.Air.AdjustMoles(Gas.CarbonDioxide, 5f);
            var inhale = new InhaleLocationEvent();
            SEntMan.EventBus.RaiseLocalEvent(body, ref inhale);
            Assert.That(inhale.Gas, Is.SameAs(atmosphere.Air), "the occupant does not breathe the pod's air.");
            Assert.Multiple(() =>
            {
                Assert.That(atmosphere.Air.GetMoles(Gas.CarbonDioxide), Is.Zero, "the exhaled gas was not scrubbed.");
                Assert.That(atmosphere.Air.Pressure, Is.EqualTo(Atmospherics.OneAtmosphere).Within(0.01f));
                Assert.That(atmosphere.Air.Temperature, Is.EqualTo(Atmospherics.T20C).Within(0.01f));
                Assert.That(atmosphere.Air.Volume, Is.EqualTo(400f));
                Assert.That(atmosphere.Air.GetMoles(Gas.Oxygen) / atmosphere.Air.TotalMoles, Is.EqualTo(0.21f).Within(0.001f));
            });

            Assert.That(Autodoc.TryEject(pod, force: true), Is.True);
            Assert.That(SEntMan.HasComponent<WolfmedAutodocOccupantComponent>(body), Is.False,
                "the marker stayed on the body after the eject.");
        });
    }

    /// <summary>
    /// (2) Power cut: within a few breaths the occupant is suffocating and the readout says UNSEALED; power back and
    /// they breathe again.
    /// </summary>
    [Test]
    public async Task PowerCutUnsealsThePodTest()
    {
        var (_, pod, body) = await PodIn(null);
        await RunSeconds(10);

        await Server.WaitAssertion(() =>
        {
            Assert.That(Breathing.IsSuffocating(body), Is.False, "sealed and suffocating before the cut.");
            SEntMan.System<SharedPowerReceiverSystem>().SetNeedsPower(pod, true);
        });

        var cut = await SecondsUntil(5, () => !Autodoc.IsPowered(pod));
        Assert.That(cut, Is.Not.Null, "the pod never lost its power.");

        await Server.WaitAssertion(() =>
        {
            Assert.That(Autodoc.GetSeal(pod), Is.EqualTo(WolfmedAutodocSeal.Unpowered));
            Assert.That(Readout(pod), Is.EqualTo(WolfmedAutodocSeal.Unpowered), "the readout was not told.");
            Assert.That(ReadoutText(pod), Does.StartWith("UNSEALED"));
        });

        var suffocating = await SecondsUntil(SuffocateWithin, () => Breathing.IsSuffocating(body));
        Note($"power cut: suffocating after {suffocating} s");
        Assert.That(suffocating, Is.Not.Null, $"no suffocation within {SuffocateWithin} s of the power going.");

        await Server.WaitPost(() => SEntMan.System<SharedPowerReceiverSystem>().SetNeedsPower(pod, false));
        var restored = await SecondsUntil(5, () => Autodoc.IsPowered(pod));
        Assert.That(restored, Is.Not.Null, "the pod never got its power back.");

        await Server.WaitAssertion(() =>
        {
            Assert.That(Autodoc.GetSeal(pod), Is.EqualTo(WolfmedAutodocSeal.Sealed));
            Assert.That(Readout(pod), Is.EqualTo(WolfmedAutodocSeal.Sealed));
        });

        var recovered = await SecondsUntil(RecoverWithin, () =>
            !Breathing.IsSuffocating(body) && SEntMan.GetComponent<RespiratorComponent>(body).SuffocationCycles == 0);
        Note($"power back: breathing again after {recovered} s");
        Assert.That(recovered, Is.Not.Null, $"still suffocating {RecoverWithin} s after the power came back.");
    }

    /// <summary>
    /// (3) Damaged past the breakage threshold: the hull is breached, the pod says so and the readout shows the fault,
    /// the occupant suffocates as in (2), and a welder repair seals the pod again.
    /// </summary>
    [Test]
    public async Task BrokenPodUnsealsUntilWeldedTest()
    {
        var (map, pod, body) = await PodIn(null);
        EntityUid user = default;

        await Server.WaitAssertion(() =>
        {
            Assert.That(Autodoc.GetSeal(pod), Is.EqualTo(WolfmedAutodocSeal.Sealed));
            var blunt = new DamageSpecifier(SProtoMan.Index<DamageTypePrototype>("Blunt"), FixedPoint2.New(110));
            SEntMan.System<DamageableSystem>().TryChangeDamage(pod, blunt, ignoreResistances: true);

            Assert.That(SEntMan.EntityExists(pod), Is.True, "110 damage destroyed the pod; the breach is below that.");
            var locale = Server.ResolveDependency<ILocalizationManager>();
            Assert.Multiple(() =>
            {
                Assert.That(SEntMan.GetComponent<WolfmedAutodocAtmosphereComponent>(pod).Broken, Is.True);
                Assert.That(Autodoc.GetSeal(pod), Is.EqualTo(WolfmedAutodocSeal.Breached));
                Assert.That(Readout(pod), Is.EqualTo(WolfmedAutodocSeal.Breached));
                Assert.That(ReadoutText(pod), Is.EqualTo("HULL BREACH: OUTSIDE ATMOSPHERE"));
                Assert.That(pod.Comp.LastLine, Is.EqualTo(locale.GetString("wolfmed-autodoc-voice-hull-breach")),
                    "the pod did not say the hull was breached.");
                Assert.That(SEntMan.System<SharedAppearanceSystem>()
                    .TryGetData<bool>(pod, WolfmedAutodocAtmosphereVisuals.Breached, out var breached) && breached,
                    Is.True, "the sprite was not told.");
            });

            // The welder's user stands at the pod: kept out of the vacuum's pressure and cold, and not breathing, so
            // nothing damages them into dropping the do-after.
            user = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            SEntMan.RemoveComponent<Content.Server.Atmos.Components.BarotraumaComponent>(user);
            SEntMan.RemoveComponent<TemperatureComponent>(user);
            SEntMan.RemoveComponent<RespiratorComponent>(user);

            var examine = SEntMan.System<Content.Shared.Examine.ExamineSystemShared>().GetExamineText(pod, user);
            Assert.That(examine.ToString(), Does.Contain("breached"), "examine does not say the hull is breached.");
        });

        var suffocating = await SecondsUntil(SuffocateWithin, () => Breathing.IsSuffocating(body));
        Note($"hull breach: suffocating after {suffocating} s");
        Assert.That(suffocating, Is.Not.Null, $"no suffocation within {SuffocateWithin} s of the breach.");

        Content.Shared.DoAfter.DoAfter? repair = null;
        await Server.WaitAssertion(() =>
        {
            var welder = SEntMan.SpawnEntity("Welder", map.GridCoords);
            Assert.That(SEntMan.System<SharedHandsSystem>().TryPickupAnyHand(user, welder), Is.True);
            Assert.That(SEntMan.System<ItemToggleSystem>().TryActivate(welder, user), Is.True);
            var interact = new InteractUsingEvent(user, welder, pod, map.GridCoords);
            SEntMan.EventBus.RaiseLocalEvent(pod.Owner, interact);
            Assert.That(interact.Handled, Is.True, "nothing took the welder to the pod.");
            foreach (var doAfter in SEntMan.GetComponent<DoAfterComponent>(user).DoAfters.Values)
                repair = doAfter;
            Assert.That(repair, Is.Not.Null, "the welder started no repair.");
        });

        var welded = await SecondsUntil(15, () => repair!.Completed || repair.Cancelled);
        Assert.That(welded, Is.Not.Null, "the repair never finished.");

        await Server.WaitAssertion(() =>
        {
            Assert.That(repair!.Cancelled, Is.False, "the repair was interrupted.");
            Assert.Multiple(() =>
            {
                Assert.That(SEntMan.GetComponent<WolfmedAutodocAtmosphereComponent>(pod).Broken, Is.False,
                    "the weld did not close the breach.");
                Assert.That(Autodoc.GetSeal(pod), Is.EqualTo(WolfmedAutodocSeal.Sealed));
                Assert.That(Readout(pod), Is.EqualTo(WolfmedAutodocSeal.Sealed));
                Assert.That(SEntMan.System<SharedAppearanceSystem>()
                    .TryGetData<bool>(pod, WolfmedAutodocAtmosphereVisuals.Breached, out var breached) && breached,
                    Is.False, "the sprite still shows the breach.");
            });
        });

        var recovered = await SecondsUntil(RecoverWithin, () =>
            !Breathing.IsSuffocating(body) && SEntMan.GetComponent<RespiratorComponent>(body).SuffocationCycles == 0);
        Note($"welded after {welded} s; breathing again after {recovered} s");
        Assert.That(recovered, Is.Not.Null, $"still suffocating {RecoverWithin} s after the weld.");
    }

    /// <summary>(4) A sealed pod in 400 K air: the occupant stays at a normal temperature while a body outside heats.</summary>
    [Test]
    public async Task SealedPodInHotAirTest()
    {
        var (map, pod, body) = await PodIn(AirAt(400f));
        var control = await Control(map);

        await RunSeconds(60);

        await Server.WaitAssertion(() =>
        {
            var levels = BodyTemperature.GetLevels(body);
            Note($"sealed in 400 K air, 60 s: surface {Surface(body):0.0} K, core {BodyTemperature.GetCore(body):0.0} K, " +
                 $"heat {Damage(body, "Heat")}; control surface {Surface(control):0.0} K, core " +
                 $"{BodyTemperature.GetCore(control):0.0} K, heat {Damage(control, "Heat")}");

            Assert.Multiple(() =>
            {
                Assert.That(Autodoc.GetSeal(pod), Is.EqualTo(WolfmedAutodocSeal.Sealed));
                Assert.That(Surface(body), Is.InRange(NormalLow, NormalHigh), "the occupant's temperature is not normal.");
                Assert.That(BodyTemperature.GetCore(body) ?? 0f, Is.InRange(NormalLow, NormalHigh), "the core moved.");
                Assert.That(levels.HeatDown, Is.Zero, "the core registers heat.");
                Assert.That(Damage(body, "Heat"), Is.EqualTo(FixedPoint2.Zero), "the hot room burned the occupant.");

                Assert.That(Surface(control), Is.GreaterThan(NormalHigh), "the control in the same air stayed cool.");
            });
        });
    }

    /// <summary>(5) No regression from the marker: an organic queue runs to QUEUE COMPLETE inside the sealed pod.</summary>
    [Test]
    public async Task OrganicQueueCompletesInsideTheSealedPodTest()
    {
        var map = await Pair.CreateTestMap();
        Entity<AutodocComponent> pod = default;
        EntityUid body = default;

        await Server.WaitAssertion(() =>
        {
            SEntMan.System<AtmosphereSystem>().SetMapAtmosphere(map.MapUid, true, GasMixture.SpaceGas);
            body = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            var blunt = new DamageSpecifier(SProtoMan.Index<DamageTypePrototype>("Blunt"), FixedPoint2.New(60));
            SEntMan.System<DamageableSystem>().TryChangeDamage(body, blunt, origin: null, targetPart: TargetBodyPart.LeftLeg);

            var uid = SEntMan.SpawnEntity("WolfmedTestAirPod", map.GridCoords);
            pod = (uid, SEntMan.GetComponent<AutodocComponent>(uid));
            Assert.That(Autodoc.TryInsert(pod, body), Is.True);
        });

        Assert.That(await SecondsUntil(5, () => Autodoc.IsPowered(pod)), Is.Not.Null, "the test pod never powered.");

        await Server.WaitAssertion(() =>
        {
            Assert.That(Autodoc.GetSeal(pod), Is.EqualTo(WolfmedAutodocSeal.Sealed));
            Assert.That(Autodoc.TryQueue(pod, "WFSurgeryMendFracture", TargetBodyPart.LeftLeg), Is.True,
                "the fixture needs a fracture to mend.");
            Assert.That(Autodoc.TryStart(pod, null), Is.True);
        });

        await RunTicksSync(240);

        await Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(pod.Comp!.Queue, Is.Empty, $"the queue did not empty (state {pod.Comp.State}).");
                Assert.That(pod.Comp.State, Is.EqualTo(AutodocState.Complete));
                Assert.That(Autodoc.GetSeal(pod), Is.EqualTo(WolfmedAutodocSeal.Sealed), "the run broke the seal.");
                Assert.That(Autodoc.IsSealedIn(body), Is.True);
                Assert.That(Breathing.IsSuffocating(body), Is.False, "the occupant suffocated during the run.");
            });
        });
    }

    /// <summary>
    /// The emag rules make an emagged pod hostile, so it vents its patient on purpose: the readout says so and the
    /// occupant's breath falls through to the room.
    /// </summary>
    [Test]
    public async Task EmaggedPodVentsItsPatientTest()
    {
        var (map, pod, body) = await PodIn(null);

        await Server.WaitAssertion(() =>
        {
            Assert.That(Autodoc.GetSeal(pod), Is.EqualTo(WolfmedAutodocSeal.Sealed));

            // What EmagSystem.DoEmagEffect does: raise the event, then leave EmaggedComponent behind.
            var emag = new GotEmaggedEvent(SEntMan.SpawnEntity("MobHuman", map.GridCoords), EmagType.Interaction);
            SEntMan.EventBus.RaiseLocalEvent(pod.Owner, ref emag);
            SEntMan.EnsureComponent<EmaggedComponent>(pod.Owner).EmagType = EmagType.Interaction;
            Autodoc.UpdateUi(pod);

            var inhale = new InhaleLocationEvent();
            SEntMan.EventBus.RaiseLocalEvent(body, ref inhale);
            Assert.Multiple(() =>
            {
                Assert.That(Autodoc.GetSeal(pod), Is.EqualTo(WolfmedAutodocSeal.Vented));
                Assert.That(ReadoutText(pod), Is.EqualTo("UNSEALED: VENTING"));
                Assert.That(Autodoc.IsSealedIn(body), Is.False);
                Assert.That(inhale.Gas, Is.Null, "the emagged pod still hands out its own air.");
            });
        });
    }
}
