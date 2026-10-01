#nullable enable
using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests._WF.Wolfmed.Scenarios;
using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Server._WF.Wolfmed.Life;
using Content.Server.Temperature.Components;
using Content.Shared._Onyx.Body.Systems;
using Content.Shared._Onyx.Wounds;
using Content.Shared._Shitmed.Body.Organ;
using Content.Shared._WF.Wolfmed.Body;
using Content.Shared._WF.Wolfmed.CCVar;
using Content.Shared._WF.Wolfmed.Consciousness;
using Content.Shared._WF.Wolfmed.Life;
using Content.Shared.Body.Organ;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Rejuvenate;
using Content.Shared.Traits.Assorted;
using NUnit.Framework;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Timing;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Wolfmed;

/// <summary>
/// BRAIN: the heart and the oxygenation clock. Nothing here is a damage total, nothing here is permanent,
/// and death is ordinary <see cref="MobState.Dead"/> with a way back out of it.
/// </summary>
[TestFixture]
[TestOf(typeof(WolfmedLifeSystem))]
public sealed class WolfmedBrainTest : GameTest
{
    /// <summary>Blood at or under 30% stops the heart, and only a defibrillator starts it again.</summary>
    [Test]
    public async Task BloodLossStopsTheHeartTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var life = entities.System<WolfmedLifeSystem>();
            var mobState = entities.System<MobStateSystem>();
            var body = entities.SpawnEntity("MobHuman", map.GridCoords);

            life.Tick(body, 1f);
            Assert.That(life.InArrest(body), Is.False, "a healthy body started in arrest.");

            Bleed(entities, body, 0.25f);
            life.Tick(body, 1f);

            Assert.Multiple(() =>
            {
                Assert.That(life.InArrest(body), Is.True, "a body at a quarter of its blood kept a pulse.");
                Assert.That(mobState.IsCritical(body), Is.True);
                Assert.That(life.BleedFactor(body), Is.EqualTo(0.25f), "bleeding kept its pressure with no pulse.");
                Assert.That(entities.HasComponent<UnrevivableComponent>(body), Is.False,
                    "Wolfmed marked a body unrevivable.");
            });

            // Blood back, then the paddles. Blood alone is not enough; the heart has to be restarted.
            entities.System<BloodstreamSystem>().TryModifyBloodLevel(body, FixedPoint2.New(400));
            life.Tick(body, 1f);
            Assert.That(life.InArrest(body), Is.True, "restoring the blood restarted the heart by itself.");

            var revival = entities.System<WolfmedRevivalSystem>();
            revival.ForcedRoll = 0f;
            Assert.That(revival.TryDefibrillate(body, out _), Is.True);
            revival.ForcedRoll = null;

            Assert.Multiple(() =>
            {
                Assert.That(life.InArrest(body), Is.False);
                Assert.That(mobState.IsDead(body), Is.False);
            });
        });
    }

    /// <summary>A heart out of the chest is arrest. Putting it back with blood behind it is a way out.</summary>
    [Test]
    public async Task HeartRemovalStopsTheHeartTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var life = entities.System<WolfmedLifeSystem>();
            var graph = entities.System<SharedBodySystem>();
            var body = entities.SpawnEntity("MobHuman", map.GridCoords);

            var heart = Organ<HeartComponent>(entities, body);
            var torso = graph.GetBodyChildrenOfType(body, BodyPartType.Torso).First().Id;
            Assert.That(graph.RemoveOrgan(heart), Is.True);
            Assert.That(life.InArrest(body), Is.True, "a body with its heart in a tray kept a pulse.");
            Assert.That(entities.System<MobStateSystem>().IsDead(body), Is.False,
                "a missing heart killed the patient outright instead of stopping it.");

            Assert.That(graph.InsertOrgan(torso, heart, "heart"), Is.True);
            life.Tick(body, 1f);
            Assert.That(life.InArrest(body), Is.False,
                "a heart back in the chest with full blood did not restart.");
        });
    }

    /// <summary>
    /// The clock. Untreated the brain runs dry and the organ dies; under CPR the same wall time leaves it
    /// alive; cold slows it further still.
    /// </summary>
    [Test]
    public async Task ArrestClockRunsOutTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();
        EntityUid untreated = default;

        await server.WaitAssertion(() =>
        {
            var life = entities.System<WolfmedLifeSystem>();
            untreated = entities.SpawnEntity("MobHuman", map.GridCoords);
            Assert.That(life.StartArrest(untreated, "test"), Is.True);

            // 180 s of a stopped heart is the whole tank (playtest 5; was 120).
            Run(life, untreated, 180);
            Assert.That(life.GetOxygenation(untreated), Is.EqualTo(0f).Within(0.01f));
            Assert.That(life.GetBrainActivity(untreated), Is.LessThan(1f),
                "an unoxygenated brain took no damage.");
            Assert.That(entities.System<MobStateSystem>().IsDead(untreated), Is.False,
                "the brain was destroyed inside the first three minutes.");

            // Under CPR the same wall time barely moves it.
            var cpr = entities.SpawnEntity("MobHuman", map.GridCoords);
            Assert.That(life.StartArrest(cpr, "test"), Is.True);
            Assert.That(entities.System<WolfmedRevivalSystem>().StartCpr(cpr, TimeSpan.FromMinutes(30)), Is.True);
            Run(life, cpr, 180);
            Assert.Multiple(() =>
            {
                Assert.That(life.GetOxygenation(cpr), Is.GreaterThan(0.6f), "CPR stopped buying time.");
                Assert.That(life.GetBrainActivity(cpr), Is.EqualTo(1f), "CPR let the brain take damage.");
            });

            // Cold is even better: a tenth of the drain below 20 C.
            var cold = entities.SpawnEntity("MobHuman", map.GridCoords);
            entities.GetComponent<TemperatureComponent>(cold).CurrentTemperature = 280f;
            Assert.That(life.StartArrest(cold, "test"), Is.True);
            Run(life, cold, 180);
            Assert.That(life.GetOxygenation(cold), Is.GreaterThan(life.GetOxygenation(cpr)),
                "a cold body did not keep its brain longer than a warm one under CPR.");

            // M2 (P24): the tissue is protected as well as the oxygen, so the cold brain has lost less of itself.
            Assert.That(life.GetBrainActivity(cold), Is.GreaterThanOrEqualTo(life.GetBrainActivity(untreated)),
                "a cold brain lost more tissue than a warm one.");
        });

        await server.WaitAssertion(() =>
        {
            // The rest of the way: the organ reaches zero and the brain is dead (about 450 s in, playtest 5).
            var life = entities.System<WolfmedLifeSystem>();
            Run(life, untreated, 300);
        });

        // OrganHealthSystem turns a destroyed brain into MobState.Dead on its own tick.
        await Pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(entities.System<MobStateSystem>().IsDead(untreated), Is.True,
                    "the oxygenation clock never killed anybody.");
                Assert.That(entities.HasComponent<UnrevivableComponent>(untreated), Is.False,
                    "brain death marked the body unrevivable.");
            });
        });
    }

    /// <summary>
    /// Brain death is not the end of it. The paddles refuse a destroyed brain, the surgery puts it back, and
    /// then the same paddles work. M1a: the patient comes back on the post-shock oxygenation, and with their
    /// blood in them that is Downed at once, not unconscious.
    /// </summary>
    [Test]
    public async Task BrainRepairMakesADeadBodyDefibrillatableTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();
        EntityUid body = default;

        await server.WaitAssertion(() =>
        {
            var life = entities.System<WolfmedLifeSystem>();
            body = entities.SpawnEntity("MobHuman", map.GridCoords);
            var brain = life.GetBrainOrgan(body);
            Assert.That(brain, Is.Not.Null);
            entities.System<OrganHealthSystem>().SetHealth(brain!.Value, FixedPoint2.Zero);
        });

        await Pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var life = entities.System<WolfmedLifeSystem>();
            var revival = entities.System<WolfmedRevivalSystem>();
            var mobState = entities.System<MobStateSystem>();
            Assert.That(mobState.IsDead(body), Is.True, "a destroyed brain did not kill the patient.");

            // Playtest 5: the paddles no longer wait for a surgeon. The brain comes back at the revive floor and the
            // patient with it: blurred, slurring, stuttering and dropping things until the brain is repaired.
            revival.ForcedRoll = 0f;
            Assert.That(revival.TryDefibrillate(body, out var line), Is.True, $"the paddles refused a destroyed brain: {line}");
            Assert.That(line, Is.EqualTo("wolfmed-defib-success"));
            revival.ForcedRoll = null;

            var floor = server.ResolveDependency<IConfigurationManager>().GetCVar(WolfmedCVars.ReviveBrainFloor);
            Assert.Multiple(() =>
            {
                Assert.That(life.GetBrainActivity(body), Is.EqualTo(floor).Within(0.01f), "the brain did not come back at the revive floor.");
                Assert.That(entities.TryGetComponent(body, out Content.Shared._WF.Wolfmed.Wounds.WolfmedConcussionComponent? concussion) &&
                            concussion.Stutter && concussion.Drop && concussion.Blur > 0f, Is.True,
                    "a revived destroyed brain did not leave the patient blurred, stuttering and clumsy.");
            });

            // The surgery is still the only thing that raises organ health, and it clears the damage.
            life.RepairBrain(body);
            Assert.That(life.GetBrainActivity(body), Is.EqualTo(1f));
            Assert.That(entities.HasComponent<WolfmedBrainTraumaComponent>(body), Is.True,
                "a repaired brain came back with no trauma.");

            var restored = server.ResolveDependency<IConfigurationManager>().GetCVar(WolfmedCVars.PostShockOxygenation);
            Assert.Multiple(() =>
            {
                Assert.That(mobState.IsDead(body), Is.False, "the revived body stayed dead.");
                Assert.That(life.GetOxygenation(body), Is.EqualTo(restored).Within(0.001f),
                    "the shock did not leave the post-shock oxygenation.");
                Assert.That(entities.GetComponent<WolfmedConsciousnessComponent>(body).State,
                    Is.EqualTo(WolfmedConsciousness.Downed), "a revived body with its blood did not come round Downed.");
                Assert.That(mobState.IsCritical(body), Is.False);
                Assert.That(life.InArrest(body), Is.False);
            });

            // Playtest 5 (after the oxygenation read, since a tick refills it): the trauma stutters and blurs, and nothing of it lingers once it ends.
            Assert.That(entities.TryGetComponent(body, out Content.Shared._WF.Wolfmed.Wounds.WolfmedConcussionComponent? trauma) && trauma.Stutter,
                Is.True, "brain trauma did not stutter the patient.");
            entities.GetComponent<WolfmedBrainTraumaComponent>(body).Ends = server.ResolveDependency<IGameTiming>().CurTime;
            life.Tick(body, 1f);
            Assert.That(entities.HasComponent<WolfmedBrainTraumaComponent>(body), Is.False, "the trauma did not end on time.");
            Assert.That(entities.HasComponent<Content.Shared._WF.Wolfmed.Wounds.WolfmedConcussionComponent>(body), Is.False,
                "the stutter and blur outlived the trauma.");
        });
    }

    /// <summary>
    /// Playtest 5, "I have been fully healed and I stutter and have bad eyesight": a concussion whose sources are gone
    /// clears itself on the concussion system's own tick, whatever path forgot to recompute it.
    /// </summary>
    [Test]
    public async Task StaleConcussionClearsItselfTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();
        EntityUid body = default;

        await server.WaitAssertion(() =>
        {
            body = entities.SpawnEntity("MobHuman", map.GridCoords);
            var stale = entities.EnsureComponent<Content.Shared._WF.Wolfmed.Wounds.WolfmedConcussionComponent>(body);
            stale.Stutter = true;
            stale.Blur = 2f;
        });

        await Pair.RunSeconds(3);

        await server.WaitAssertion(() =>
            Assert.That(entities.HasComponent<Content.Shared._WF.Wolfmed.Wounds.WolfmedConcussionComponent>(body), Is.False,
                "a concussion with no source behind it never cleared."));
    }

    /// <summary>
    /// Blood under the defibrillator's floor (M1a: 25%) is a refusal whatever the brain says, and the refusal
    /// names the units. A beating heart is refused too: a shock is not indicated.
    /// </summary>
    [Test]
    public async Task DefibrillatorNeedsBloodAndABrainTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var life = entities.System<WolfmedLifeSystem>();
            var revival = entities.System<WolfmedRevivalSystem>();
            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            Bleed(entities, body, 0.2f);
            life.Tick(body, 1f);
            Assert.That(life.InArrest(body), Is.True);

            revival.ForcedRoll = 0f;
            Assert.That(revival.TryDefibrillate(body, out var line), Is.False);
            Assert.That(line, Is.EqualTo(WolfmedRevivalSystem.NoBlood));

            // 20% of a 300 u pool: 45 u to the 35% post-shock target, 90 u to 50%.
            var text = revival.LocalizeLine(body, line);
            Assert.That(text, Does.Contain("≈ 45 u").And.Contain("≈ 90 u"), $"the refusal names no units: {text}");

            var healthy = entities.SpawnEntity("MobHuman", map.GridCoords);
            Assert.That(revival.TryDefibrillate(healthy, out var pulse), Is.False, "the paddles restarted a beating heart.");
            Assert.That(pulse, Is.EqualTo(WolfmedRevivalSystem.PulsePresent));
            revival.ForcedRoll = null;
        });
    }

    /// <summary>A head torn off takes the brain with it, and that is death the moment it lands.</summary>
    [Test]
    public async Task BrainLeavingTheBodyIsDeathTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var graph = entities.System<SharedBodySystem>();
            var mobState = entities.System<MobStateSystem>();

            var debrained = entities.SpawnEntity("MobHuman", map.GridCoords);
            var brain = entities.System<WolfmedLifeSystem>().GetBrainOrgan(debrained);
            Assert.That(brain, Is.Not.Null);
            Assert.That(graph.RemoveOrgan(brain!.Value), Is.True);
            Assert.That(mobState.IsDead(debrained), Is.True, "a body with its brain in a jar kept living.");
            Assert.That(entities.HasComponent<UnrevivableComponent>(debrained), Is.False);
        });
    }

    /// <summary>
    /// The canary. A wound host with no brain at all is a test fixture, not a corpse: nothing polls for a
    /// missing brain, so it lives exactly as long as anything else does.
    /// </summary>
    [Test]
    public async Task BrainlessWoundHostKeepsLivingTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();
        EntityUid body = default;

        await server.WaitAssertion(() =>
        {
            body = entities.SpawnEntity("WolfmedBrainlessBody", map.GridCoords);
            var life = entities.System<WolfmedLifeSystem>();
            for (var second = 0; second < 30; second++)
                life.Tick(body, 10f);

            Assert.Multiple(() =>
            {
                Assert.That(entities.System<MobStateSystem>().IsDead(body), Is.False,
                    "a wound host with no brain was killed by the life system.");
                Assert.That(life.InArrest(body), Is.False, "a brainless body went into cardiac arrest.");
            });
        });

        await Pair.RunTicksSync(30);

        await server.WaitAssertion(() =>
        {
            Assert.That(entities.System<MobStateSystem>().IsDead(body), Is.False,
                "a wound host with no brain died on the real tick.");
        });
    }

    /// <summary>
    /// Real suffocation drains the brain, and putting the air back lets it fill again (M1a, plan §4.3).
    /// Asphyxiation on a body that is breathing is bookkeeping: it drains nothing before, and it does not
    /// hold the refill back after.
    /// </summary>
    [Test]
    public async Task SuffocationDrainsAndRecoversTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();
        var scenario = new WolfmedScenario(entities);
        EntityUid body = default;
        var starved = 0f;

        await server.WaitAssertion(() =>
        {
            var life = entities.System<WolfmedLifeSystem>();
            scenario.SetAir(map.MapUid, true);
            body = entities.SpawnEntity("MobHuman", map.GridCoords);

            entities.System<DamageableSystem>().TryChangeDamage(body, Spec("Asphyxiation", 150), ignoreResistances: true);
            Run(life, body, 60);
            Assert.That(life.GetOxygenation(body), Is.EqualTo(1f).Within(0.001f),
                "Asphyxiation on a breathing patient drained the brain.");

            scenario.SetAir(map.MapUid, false);
        });

        await WaitForBreathing(scenario, body, suffocating: true);

        await server.WaitAssertion(() =>
        {
            var life = entities.System<WolfmedLifeSystem>();
            Run(life, body, 60);
            starved = life.GetOxygenation(body);
            Assert.That(starved, Is.LessThan(0.8f), "suffocation cost the brain nothing.");
            scenario.SetAir(map.MapUid, true);
        });

        await WaitForBreathing(scenario, body, suffocating: false);

        await server.WaitAssertion(() =>
        {
            var life = entities.System<WolfmedLifeSystem>();
            Assert.That(scenario.Damage(body, "Asphyxiation"), Is.GreaterThan(FixedPoint2.Zero),
                "the damage healed before the check; this proves nothing about leftover Asphyxiation.");
            var breathing = life.GetOxygenation(body);
            Run(life, body, 60);
            Assert.That(life.GetOxygenation(body), Is.GreaterThan(breathing),
                "the brain never refilled once the patient was breathing again.");
        });
    }

    /// <summary>Real time until the respirator is, or is no longer, suffocating.</summary>
    private async Task WaitForBreathing(WolfmedScenario scenario, EntityUid body, bool suffocating)
    {
        var done = false;
        for (var second = 0; second < 30 && !done; second++)
        {
            await Pair.RunSeconds(1);
            await Pair.Server.WaitPost(() => done = scenario.Breathing.IsSuffocating(body) == suffocating);
        }

        Assert.That(done, Is.True, suffocating ? "the respirator never ran short." : "the respirator never recovered.");
    }

    /// <summary>Rejuvenate undoes an arrest, the clock and the organ damage behind it.</summary>
    [Test]
    public async Task RejuvenateClearsTheClockTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var life = entities.System<WolfmedLifeSystem>();
            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            Assert.That(life.StartArrest(body, "test"), Is.True);
            Run(life, body, 200);

            entities.EventBus.RaiseLocalEvent(body, new RejuvenateEvent());

            Assert.Multiple(() =>
            {
                Assert.That(life.InArrest(body), Is.False);
                Assert.That(life.GetOxygenation(body), Is.EqualTo(1f));
                Assert.That(life.GetBrainActivity(body), Is.EqualTo(1f));
            });
        });
    }

    /// <summary>
    /// A machine body has no clock. Losing the pump shuts it down, which is not death; losing the positronic
    /// brain is.
    /// </summary>
    [Test]
    public async Task MechanicalShutdownAndDeathTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();
        EntityUid ipc = default;

        await server.WaitAssertion(() =>
        {
            var graph = entities.System<SharedBodySystem>();
            var shutdown = entities.System<WolfmedShutdownSystem>();
            var life = entities.System<WolfmedLifeSystem>();
            ipc = entities.SpawnEntity("MobIPC", map.GridCoords);

            Assert.That(shutdown.IsMechanical(ipc), Is.True, "the IPC fixture is not mechanical.");
            Assert.That(life.GetBrain(ipc), Is.Null, "a chassis is running an oxygenation clock.");

            // The cell going flat is the other half of it, and it is not death either.
            entities.EventBus.RaiseLocalEvent(ipc, new Content.Server._EinsteinEngines.Silicon.Death
                .SiliconChargeDeathEvent(ipc, null, ipc));
            Assert.That(shutdown.IsShutDown(ipc), Is.True, "a chassis with a flat cell kept running.");
            entities.EventBus.RaiseLocalEvent(ipc, new Content.Server._EinsteinEngines.Silicon.Death
                .SiliconChargeAliveEvent(ipc, null, ipc));
            Assert.That(shutdown.IsShutDown(ipc), Is.False, "power came back and the chassis stayed down.");

            var pump = Organ<HeartComponent>(entities, ipc);
            Assert.That(graph.RemoveOrgan(pump), Is.True);

            Assert.Multiple(() =>
            {
                Assert.That(shutdown.IsShutDown(ipc), Is.True, "a chassis with no pump kept running.");
                Assert.That(entities.System<MobStateSystem>().IsDead(ipc), Is.False,
                    "a missing pump killed the chassis instead of shutting it down.");
                Assert.That(life.InArrest(ipc), Is.False, "a chassis went into cardiac arrest.");
            });
        });

        await server.WaitAssertion(() =>
        {
            // The positronic brain is a brain: destroying it is death, on the same organ path.
            var life = entities.System<WolfmedLifeSystem>();
            var brain = life.GetBrainOrgan(ipc);
            Assert.That(brain, Is.Not.Null, "the positronic brain carries no organ health.");
            entities.System<OrganHealthSystem>().SetHealth(brain!.Value, FixedPoint2.Zero);
        });

        await Pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(entities.System<MobStateSystem>().IsDead(ipc), Is.True,
                "a destroyed positronic brain did not kill the chassis.");

            // M2 (plan §7.2, OD10): core repair and the restart button, which reads the pump as well as the core.
            var life = entities.System<WolfmedLifeSystem>();
            var revival = entities.System<WolfmedRevivalSystem>();
            life.RepairBrain(ipc);
            Assert.That(entities.HasComponent<WolfmedBrainTraumaComponent>(ipc), Is.False, "a repaired core carries brain trauma.");
            Assert.That(revival.GetRestartRefusal(ipc), Is.EqualTo(WolfmedRevivalSystem.RestartNoPump),
                "the restart does not refuse a chassis with its pump out.");
        });
    }

    /// <summary>
    /// A body with no oxygenation clock still leaves arrest when its pump is back. Nothing puts a chassis
    /// into arrest today, but the way out must not depend on the clock: a body that got there any other way
    /// would otherwise wait forever for a defibrillator.
    /// </summary>
    [Test]
    public async Task ArrestEndsWithoutAClockTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var life = entities.System<WolfmedLifeSystem>();
            var graph = entities.System<SharedBodySystem>();
            var ipc = entities.SpawnEntity("MobIPC", map.GridCoords);
            Assert.That(life.GetBrain(ipc), Is.Null, "the chassis fixture runs an oxygenation clock.");

            var pump = Organ<HeartComponent>(entities, ipc);
            var torso = graph.GetBodyChildrenOfType(ipc, BodyPartType.Torso).First().Id;
            Assert.That(graph.RemoveOrgan(pump), Is.True);

            var arrest = entities.AddComponent<WolfmedCardiacArrestComponent>(ipc);
            arrest.Cause = "heart";
            Assert.That(life.InArrest(ipc), Is.True);

            life.Tick(ipc, 1f);
            Assert.That(life.InArrest(ipc), Is.True, "arrest ended with the pump still out of the chassis.");

            Assert.That(graph.InsertOrgan(torso, pump, "pump"), Is.True);
            Assert.That(life.InArrest(ipc), Is.False,
                "the pump was back with fluid behind it and the chassis stayed arrested.");
        });
    }

    /// <summary>
    /// A head torn off is death for flesh and for a chassis alike. An IPC keeps its positronic brain in the
    /// torso, so nothing leaves with the head: the head is a vital part, and that is the rule that has to
    /// carry it.
    /// </summary>
    [Test]
    public async Task DecapitationKillsTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var amputation = entities.System<AmputationSystem>();
            var life = entities.System<WolfmedLifeSystem>();
            var mobState = entities.System<MobStateSystem>();

            var ipc = entities.SpawnEntity("MobIPC", map.GridCoords);
            Assert.That(life.HasBrain(ipc), Is.True, "the chassis fixture has no positronic brain.");
            Assert.That(amputation.TryAmputate(ipc, Part(entities, ipc, BodyPartType.Head)), Is.True);

            Assert.Multiple(() =>
            {
                Assert.That(mobState.IsDead(ipc), Is.True, "a decapitated chassis kept running.");
                Assert.That(life.HasBrain(ipc), Is.True,
                    "the positronic brain went with the head, so this proves nothing about the vital rule.");
            });

            // The control: flesh dies on the brain leaving with the head, and still must.
            var human = entities.SpawnEntity("MobHuman", map.GridCoords);
            Assert.That(amputation.TryAmputate(human, Part(entities, human, BodyPartType.Head)), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(mobState.IsDead(human), Is.True, "a decapitated human stayed alive.");
                Assert.That(life.HasBrain(human), Is.False);
            });
        });
    }

    /// <summary>
    /// A head blown apart is a head lost. Gibbing deletes the part in its slot instead of amputating it, so
    /// the removal arrives while the part is already terminating; an IPC walked on after exactly that.
    /// </summary>
    [Test]
    public async Task GibbedHeadKillsTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();
        EntityUid ipc = default;

        await server.WaitPost(() =>
        {
            ipc = entities.SpawnEntity("MobIPC", map.GridCoords);
            var gibs = entities.System<BodySystem>().GibPart(Part(entities, ipc, BodyPartType.Head));
            Assert.That(gibs, Is.Not.Empty, "the head did not gib.");
        });

        await Pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var body = entities.System<SharedBodySystem>();
            Assert.Multiple(() =>
            {
                Assert.That(body.GetBodyChildrenOfType(ipc, BodyPartType.Head), Is.Empty, "the head is still on.");
                Assert.That(entities.System<MobStateSystem>().IsDead(ipc), Is.True, "a chassis with its head gibbed kept running.");
            });
        });
    }

    /// <summary>Taking the positronic brain out is death, the same as taking a fleshy one out.</summary>
    [Test]
    public async Task PositronicBrainOutIsDeathTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var ipc = entities.SpawnEntity("MobIPC", map.GridCoords);
            var brain = Organ<BrainComponent>(entities, ipc);

            Assert.That(entities.System<SharedBodySystem>().RemoveOrgan(brain), Is.True);
            Assert.That(entities.System<MobStateSystem>().IsDead(ipc), Is.True,
                "a chassis with its positronic brain in somebody's hand kept running.");
        });
    }

    private static EntityUid Part(IEntityManager entities, EntityUid body, BodyPartType type) =>
        entities.System<SharedBodySystem>().GetBodyChildrenOfType(body, type).First().Id;

    private static void Run(WolfmedLifeSystem life, EntityUid body, int seconds)
    {
        for (var second = 0; second < seconds; second++)
            life.Tick(body, 1f);
    }

    private static void Bleed(IEntityManager entities, EntityUid body, float target)
    {
        // BloodstreamComponent is read-only from a test assembly, so the volume is walked down instead of
        // being solved for: ten units a step is two dozen steps on a human.
        var bloodstream = entities.System<BloodstreamSystem>();
        for (var step = 0; step < 200 && bloodstream.GetBloodLevelPercentage(body) > target; step++)
            bloodstream.TryModifyBloodLevel(body, FixedPoint2.New(-10));
    }

    private static EntityUid Organ<T>(IEntityManager entities, EntityUid body) where T : IComponent
    {
        foreach (var (organ, _) in entities.System<SharedBodySystem>().GetBodyOrgans(body))
        {
            if (entities.HasComponent<T>(organ))
                return organ;
        }

        Assert.Fail($"the fixture has no {typeof(T).Name}.");
        return default;
    }

    private static DamageSpecifier Spec(string type, int amount) => new()
    {
        DamageDict = { [new ProtoId<DamageTypePrototype>(type)] = FixedPoint2.New(amount) },
    };

    [TestPrototypes]
    private const string Prototypes = @"
- type: body
  id: WolfmedBrainlessGraph
  name: ""wolfmed brainless body""
  root: torso
  slots:
    torso:
      part: TorsoHuman
      connections:
      - left leg
      - right leg
    left leg:
      part: LeftLegHuman
    right leg:
      part: RightLegHuman

# The canary from PassiveDamageMechanismStillRoutesIfReenabledTest, in one prototype: a wound host with no
# head, no brain and no heart. Nothing may kill it on a timer.
- type: entity
  id: WolfmedBrainlessBody
  parent: InventoryBase
  components:
  - type: Body
    prototype: WolfmedBrainlessGraph
  - type: Damageable
    damageContainer: Biological
  - type: MobState
  - type: MobThresholds
    thresholds:
      0: Alive
      100: Critical
      200: Dead
  - type: Alerts
  - type: MovementSpeedModifier
  - type: StandingState
  - type: WoundHost
";
}
