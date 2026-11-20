#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Pair;
using Content.Server._Shitmed.Medical.Surgery;
using Content.Server.Atmos.Components;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Station.Systems;
using Content.Server.Temperature.Components;
using Content.Shared._Onyx.Wounds;
using Content.Shared._Shitmed.Medical.Surgery;
using Content.Shared._Shitmed.Targeting;
using Content.Shared._WF.Biotype;
using Content.Shared._WF.Wolfmed.Body;
using Content.Shared._WF.Wolfmed.Consciousness;
using Content.Shared._WF.Wolfmed.Hud;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Eye.Blinding.Components;
using Content.Shared.Eye.Blinding.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Item.ItemToggle;
using Content.Shared.Preferences;
using Content.Shared.Standing;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Wolfmed;

/// <summary>
/// Synthetic balance pass: every way a chassis gets hurt has a repair a crewmate can do, and a synthetic biotype
/// spawns on its feet.
/// </summary>
[TestFixture]
public sealed class WolfmedSynthRepairTest : WolfmedGameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    /// <summary>Reported: a synthetic human flopped to the floor on spawn, with the downed HUD up.</summary>
    [TestCase("Human")]
    [TestCase("Vulpkanin")]
    public async Task SyntheticBiotypeSpawnsStandingTest(string species)
    {
        var map = await CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            SEntMan.System<AtmosphereSystem>().SetMapAtmosphere(map.MapUid, false, WolfmedIvDripTest.StationAir());
            var profile = HumanoidCharacterProfile.RandomWithSpecies(species).WithBiotype(WFBiotype.Synthetic);
            var mob = SEntMan.System<StationSpawningSystem>().SpawnPlayerMob(map.GridCoords, job: null, profile, station: null);

            Assert.Multiple(() =>
            {
                Assert.That(SEntMan.GetComponent<WolfmedConsciousnessComponent>(mob).State,
                    Is.EqualTo(WolfmedConsciousness.Up), "the converted body spawned downed.");
                Assert.That(SEntMan.System<StandingStateSystem>().IsDown(mob), Is.False, "the converted body spawned lying down.");
            });
        });
    }

    /// <summary>Reported: welder flashes blurred a synth's optics, the eye organ read 100% and nothing fixed them.</summary>
    [TestCase("MobIPC")]
    [TestCase("MobSynth")]
    public async Task GlassRepairsFlashedOpticsTest(string mobId)
    {
        var map = await CreateTestMap();
        EntityUid body = default;

        await Server.WaitAssertion(() =>
        {
            SEntMan.System<AtmosphereSystem>().SetMapAtmosphere(map.MapUid, false, WolfmedIvDripTest.StationAir());
            body = SEntMan.SpawnEntity(mobId, map.GridCoords);
            SEntMan.System<BlindableSystem>().AdjustEyeDamage((body, null), 6);
            var eyes = Eyes(body);
            Assert.That(eyes.Comp.Health, Is.LessThan(eyes.Comp.MaxHealth), "eye damage does not show on the eyes organ.");
            Assert.That(eyes.Comp.Health, Is.GreaterThan(FixedPoint2.Zero), "eye damage destroyed the eyes organ.");
        });

        await RunSeconds(1);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<WolfmedSyntheticHudComponent>(body).Faults.Select(f => f.Line),
                Does.Contain("wolfmed-synthetic-line-optics"), "the readout does not say the optics are damaged.");

            var user = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            var glass = SEntMan.SpawnEntity("SheetGlass", map.GridCoords);
            Assert.That(SEntMan.System<SharedHandsSystem>().TryPickupAnyHand(user, glass), Is.True);
            var interact = new AfterInteractEvent(user, glass, body, map.GridCoords, true);
            SEntMan.EventBus.RaiseLocalEvent(glass, interact);
            Assert.That(interact.Handled, Is.True, "the glass did not start on the optics.");
        });

        await RunSeconds(5);

        await Server.WaitAssertion(() =>
        {
            var eyes = Eyes(body);
            Assert.Multiple(() =>
            {
                Assert.That(SEntMan.GetComponent<BlindableComponent>(body).EyeDamage, Is.Zero, "the glass left the optics damaged.");
                Assert.That(eyes.Comp.Health, Is.EqualTo(eyes.Comp.MaxHealth), "the eyes organ did not get its health back.");
            });
        });
    }

    /// <summary>The eye surgery lists for flash damage alone and clears the blur.</summary>
    [Test]
    public async Task HealEyesClearsFlashDamageTest()
    {
        var map = await CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var body = SEntMan.SpawnEntity("MobIPC", map.GridCoords);
            var user = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            SEntMan.System<BlindableSystem>().AdjustEyeDamage((body, null), 4);
            var head = Part(body, BodyPartType.Head);
            var surgery = SEntMan.System<SurgerySystem>();
            Assert.That(surgery.WolfmedSurgeryValid(body, head, "WFSurgeryHealEyes"), Is.True,
                "no eye surgery for flashed optics.");

            RunStep(surgery, user, body, head, new List<EntityUid>(), "WFSurgeryStepHealEyes");
            Assert.That(SEntMan.GetComponent<BlindableComponent>(body).EyeDamage, Is.Zero, "the eye surgery left the blur.");
        });
    }

    /// <summary>The welder closed the chassis wound but left its Cold and Caustic damage, which nothing else reached.</summary>
    [TestCase("MobIPC", "Cold")]
    [TestCase("MobIPC", "Caustic")]
    [TestCase("MobSynth", "Caustic")]
    public async Task WelderRepairsColdAndCausticTest(string mobId, string type)
    {
        var map = await CreateTestMap();
        EntityUid leg = default;

        await Server.WaitAssertion(() =>
        {
            var body = SEntMan.SpawnEntity(mobId, map.GridCoords);
            var user = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            foreach (var uid in new[] { body, user })
            {
                SEntMan.RemoveComponent<BarotraumaComponent>(uid);
                SEntMan.RemoveComponent<TemperatureComponent>(uid);
            }

            leg = Part(body, BodyPartType.Leg);
            var damage = new DamageSpecifier(SProtoMan.Index<DamageTypePrototype>(type), 30);
            Assert.That(SEntMan.System<WoundDamageRoutingSystem>()
                .TryApplyPartDamage(body, leg, damage, ignoreResistances: true), Is.True);

            var welder = SEntMan.SpawnEntity("Welder", map.GridCoords);
            Assert.That(SEntMan.System<SharedHandsSystem>().TryPickupAnyHand(user, welder), Is.True);
            Assert.That(SEntMan.System<ItemToggleSystem>().TryActivate(welder, user), Is.True);
            SEntMan.GetComponent<TargetingComponent>(user).Target = TargetBodyPart.LeftLeg;
            var interact = new InteractUsingEvent(user, welder, body, map.GridCoords);
            SEntMan.EventBus.RaiseLocalEvent(body, interact);
            Assert.That(interact.Handled, Is.True, "the welder did not start.");
        });

        await RunSeconds(10);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<DamageableComponent>(leg).TotalDamage, Is.EqualTo(FixedPoint2.Zero),
                $"{type} damage is left on the leg.");
        });
    }

    /// <summary>A servo can be replaced from the outside with a coil, without opening the limb.</summary>
    [Test]
    public async Task ServoReplacedFromOutsideTest()
    {
        var map = await CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var body = SEntMan.SpawnEntity("MobIPC", map.GridCoords);
            var user = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            var leg = Part(body, BodyPartType.Leg);
            var wounds = SEntMan.System<WoundSystem>();
            Assert.That(wounds.CreateOrMergeWound(leg, "WFWolfmedServoDamageWound", FixedPoint2.New(20)), Is.Not.Null);

            var surgery = SEntMan.System<SurgerySystem>();
            var singleton = surgery.GetSingleton("WFSurgeryReplaceServo");
            Assert.That(singleton, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(SEntMan.GetComponent<SurgeryComponent>(singleton!.Value).Requirement, Is.Null,
                    "a servo still needs the limb cut open.");
                Assert.That(surgery.WolfmedSurgeryValid(body, leg, "WFSurgeryReplaceServo"), Is.True);
            });

            var coil = SEntMan.SpawnEntity("CableApcStack", map.GridCoords);
            RunStep(surgery, user, body, leg, new List<EntityUid> { coil }, "WFSurgeryStepReplaceServo");
            Assert.That(wounds.GetWounds(leg).Select(w => w.Comp.Prototype.Id), Has.None.EqualTo("WFWolfmedServoDamageWound"),
                "the servo is still damaged.");
        });
    }

    /// <summary>Shock on a synth left Cellular damage that only nanites, which nothing makes, could repair.</summary>
    [Test]
    public async Task SynthShockLeavesNoCellularTest()
    {
        var map = await CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var body = SEntMan.SpawnEntity("MobSynth", map.GridCoords);
            var torso = Part(body, BodyPartType.Torso);
            var shock = new DamageSpecifier(SProtoMan.Index<DamageTypePrototype>("Shock"), 30);
            Assert.That(SEntMan.System<WoundDamageRoutingSystem>()
                .TryApplyPartDamage(body, torso, shock, ignoreResistances: true), Is.True);
            Assert.That(SEntMan.GetComponent<DamageableComponent>(body).Damage.DamageDict.GetValueOrDefault("Cellular"),
                Is.EqualTo(FixedPoint2.Zero), "shock still deals a synth Cellular damage.");
        });
    }

    private EntityUid Part(EntityUid body, BodyPartType type) =>
        SEntMan.System<SharedBodySystem>().GetBodyChildrenOfType(body, type).First().Id;

    private Entity<WolfmedOrganComponent> Eyes(EntityUid body)
    {
        var organ = SEntMan.System<SharedBodySystem>().GetBodyOrgans(body).First(o => o.Component.SlotId == "eyes").Id;
        return (organ, SEntMan.GetComponent<WolfmedOrganComponent>(organ));
    }

    private void RunStep(SurgerySystem surgery, EntityUid user, EntityUid body, EntityUid part, List<EntityUid> tools,
        string step)
    {
        var singleton = surgery.GetSingleton(step);
        Assert.That(singleton, Is.Not.Null, $"{step} has no singleton.");
        var ev = new SurgeryStepEvent(user, body, part, tools, singleton!.Value);
        SEntMan.EventBus.RaiseLocalEvent(singleton.Value, ref ev);
    }
}
