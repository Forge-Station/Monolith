#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._WF.Wolfmed.Life;
using Content.Server._WF.Wolfmed.Wounds;
using Content.Server.Chat;
using Content.Shared.Prototypes;
using Content.Server.Medical;
using Content.Shared._Onyx.Body.Systems;
using Content.Shared._Onyx.Wounds;
using Content.Shared._Shitmed.Medical.Surgery;
using Content.Shared._Shitmed.Targeting;
using Content.Shared._WF.Wolfmed.CCVar;
using Content.Shared._WF.Wolfmed.Hud;
using Content.Shared._WF.Wolfmed.Life;
using Content.Shared._WF.Wolfmed.Surgery;
using Content.Shared._WF.Wolfmed.Wounds;
using Content.Shared.Body.Organ;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Electrocution;
using Content.Shared.FixedPoint;
using Content.Shared.Ghost;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Mind;
using Content.Shared.Mobs.Systems;
using Content.Shared.Players;
using Content.Shared.Verbs;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Wolfmed.Scenarios;

/// <summary>
/// M2 (plan §7.2, §5.4, §3.5, §3.6, P20, P24, P28, OD10, OD15, OD17): what brings a body back and what finishes it
/// honestly. The restart button reads what a chassis needs, not a damage total; core repair gives a destroyed
/// positronic core back to the same mind with no trauma; an execution is a revivable catastrophic brain injury and a
/// suicide the same with no way back; late sepsis stops the heart on a clock, not a roll; cold slows tissue loss;
/// insulation counts before the heart does; a suture closes a wound for infection.
/// </summary>
[TestFixture]
[TestOf(typeof(WolfmedRevivalSystem))]
public sealed class WolfmedRevivalTest : GameTest
{
    private async Task Pin()
    {
        await OverrideCVar(Side.Server, WolfmedCVars.Consciousness, true);
        // The sepsis fixtures infect a 20-point cut, a minor wound, which playtest 5 spares the air.
        await OverrideCVar(Side.Server, WolfmedCVars.InfectionSparesMinor, false);
        await OverrideCVar(Side.Server, WolfmedCVars.ArrestBlood, 0.30f);
        await OverrideCVar(Side.Server, WolfmedCVars.ArrestOxygenation, 0.15f);
        await OverrideCVar(Side.Server, WolfmedCVars.ArrestSepsis, 80f);
        await OverrideCVar(Side.Server, WolfmedCVars.ArrestSepsisChance, 0f);
        await OverrideCVar(Side.Server, WolfmedCVars.ArrestShockDamage, 60f);
        await OverrideCVar(Side.Server, WolfmedCVars.BrainSepsisSeconds, 600f);
        // The M2 figures the derivations use; playtest 5 ships 180 and 0.05 (WolfmedArrestClockTest walks those).
        await OverrideCVar(Side.Server, WolfmedCVars.BrainArrestSeconds, 120f);
        await OverrideCVar(Side.Server, WolfmedCVars.BrainDamageOxygenation, 0.4f);
        await OverrideCVar(Side.Server, WolfmedCVars.BrainDamageRate, 0.1f);
        await OverrideCVar(Side.Server, WolfmedCVars.BrainColdFactor, 1f);
        await OverrideCVar(Side.Server, WolfmedCVars.BrainTraumaMinutes, 30f);
        await OverrideCVar(Side.Server, WolfmedCVars.DefibBlood, 0.25f);
        await OverrideCVar(Side.Server, WolfmedCVars.SutureTreatmentLostSeverity, 15f);
    }

    private EntityUid Part(EntityUid body, BodyPartType type) =>
        SEntMan.System<SharedBodySystem>().GetBodyChildren(body).First(part => part.Component.PartType == type).Id;

    private EntityUid Organ<T>(EntityUid body) where T : IComponent =>
        SEntMan.System<SharedBodySystem>().GetBodyOrgans(body).First(organ => SEntMan.HasComponent<T>(organ.Id)).Id;

    private static DamageSpecifier Spec(string type, float amount) => new()
    {
        DamageDict = { [new ProtoId<DamageTypePrototype>(type)] = FixedPoint2.New(amount) },
    };

    /// <summary>
    /// A shipped surgery step's effect, raised on its singleton the way the wound surgery tests drive one, with the
    /// tools in hand: Shitmed only applies a step's add and remove when one of them carries the step's tool.
    /// </summary>
    private void Step(string prototype, EntityUid body, EntityUid part, List<EntityUid> tools)
    {
        var step = SEntMan.System<Content.Server._Shitmed.Medical.Surgery.SurgerySystem>().GetSingleton(prototype);
        Assert.That(step, Is.Not.Null, $"{prototype} has no singleton.");
        var ev = new SurgeryStepEvent(body, body, part, tools, step!.Value);
        SEntMan.EventBus.RaiseLocalEvent(step.Value, ref ev);
    }

    /// <summary>
    /// <c>RestartHookTest</c> (plan §7.2): the restart button refuses a headless chassis and a coreless one, and says
    /// why; a repaired chassis carrying well over 100 damage restarts, where the button's own damage total would have
    /// refused it.
    /// </summary>
    [Test]
    public async Task RestartHookTest()
    {
        await Pin();
        var map = await Pair.CreateTestMap();
        var revival = SEntMan.System<WolfmedRevivalSystem>();
        var life = SEntMan.System<WolfmedLifeSystem>();
        var mobState = SEntMan.System<MobStateSystem>();
        EntityUid headless = default, coreless = default, battered = default;

        await Server.WaitPost(() =>
        {
            headless = SEntMan.SpawnEntity("MobIPC", map.GridCoords);
            coreless = SEntMan.SpawnEntity("MobIPC", map.GridCoords);
            battered = SEntMan.SpawnEntity("MobIPC", map.GridCoords);
            var minds = SEntMan.System<SharedMindSystem>();
            foreach (var ipc in new[] { headless, coreless, battered })
                minds.TransferTo(minds.CreateMind(null).Owner, ipc);
        });
        await RunSeconds(2);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.System<AmputationSystem>().TryAmputate(headless, Part(headless, BodyPartType.Head)), Is.True);
            Assert.That(SEntMan.System<SharedBodySystem>().RemoveOrgan(Organ<Content.Server.Body.Components.BrainComponent>(coreless)), Is.True);

            // Battered: well over 100 in total, spread so no part is destroyed, then the core destroyed.
            var damageable = SEntMan.System<DamageableSystem>();
            foreach (var target in new[] { TargetBodyPart.Torso, TargetBodyPart.LeftArm, TargetBodyPart.RightArm, TargetBodyPart.LeftLeg })
                damageable.TryChangeDamage(battered, Spec("Blunt", 35), ignoreResistances: true, targetPart: target);

            SEntMan.System<OrganHealthSystem>().SetHealth(life.GetBrainOrgan(battered)!.Value, FixedPoint2.Zero);
        });
        await RunSeconds(1);

        await Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(mobState.IsDead(headless) && mobState.IsDead(coreless) && mobState.IsDead(battered), Is.True,
                    "the fixtures are not all dead.");
                Assert.That(revival.GetRestartRefusal(headless), Is.EqualTo(WolfmedRevivalSystem.RestartNoHead));
                Assert.That(revival.GetRestartRefusal(coreless), Is.EqualTo(WolfmedRevivalSystem.RestartNoCore));
                Assert.That(revival.GetRestartRefusal(battered), Is.EqualTo(WolfmedRevivalSystem.RestartCoreDestroyed));

                // The hook's hand-off handles them (the button's damage check never runs) and refuses.
                Assert.That(revival.TryRestart(headless), Is.True);
                Assert.That(revival.TryRestart(coreless), Is.True);
                Assert.That(mobState.IsDead(headless) && mobState.IsDead(coreless), Is.True, "a refused restart revived.");
            });

            var total = SEntMan.GetComponent<DamageableComponent>(battered).TotalDamage;
            TestContext.Out.WriteLine($"RestartHookTest: battered chassis carries {total} damage.");
            Assert.That(total, Is.GreaterThan(FixedPoint2.New(100)), "the fixture is not over the button's old damage gate.");

            life.RepairBrain(battered);
            Assert.That(revival.GetRestartRefusal(battered), Is.Null, "a repaired chassis is still refused.");
            Assert.That(revival.TryRestart(battered), Is.True);
            Assert.That(mobState.IsDead(battered), Is.False, "a repaired chassis over 100 damage did not restart.");
        });
    }

    /// <summary>
    /// <c>CoreRepairTest</c> (plan §7.2, OD10): a positronic core at 0 is core failure; the analyzer names core repair;
    /// WFSurgeryRepairCore (wrench, multitool, welder) puts it back; the restart brings back the same mind, with no
    /// brain trauma on the chassis and "CORE RESTORED" on its readout instead.
    /// </summary>
    [Test]
    public async Task CoreRepairTest()
    {
        await Pin();
        var map = await Pair.CreateTestMap();
        var revival = SEntMan.System<WolfmedRevivalSystem>();
        var life = SEntMan.System<WolfmedLifeSystem>();
        var mobState = SEntMan.System<MobStateSystem>();
        var surgery = SEntMan.System<Content.Server._Shitmed.Medical.Surgery.SurgerySystem>();
        EntityUid ipc = default, human = default, mind = default;

        await Server.WaitPost(() =>
        {
            ipc = SEntMan.SpawnEntity("MobIPC", map.GridCoords);
            human = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            var minds = SEntMan.System<SharedMindSystem>();
            mind = minds.CreateMind(null).Owner;
            minds.TransferTo(mind, ipc);
        });
        await RunSeconds(2);

        await Server.WaitPost(() =>
            SEntMan.System<OrganHealthSystem>().SetHealth(life.GetBrainOrgan(ipc)!.Value, FixedPoint2.Zero));
        await RunSeconds(1);

        await Server.WaitAssertion(() =>
        {
            Assert.That(mobState.IsDead(ipc), Is.True, "a destroyed core did not kill the chassis.");

            var report = SEntMan.System<HealthAnalyzerSystem>().BuildVitals(ipc)!;
            var text = string.Join(" | ", WolfmedVitalsText.Lines(report));
            TestContext.Out.WriteLine($"CoreRepairTest analyzer: {text}");
            Assert.Multiple(() =>
            {
                Assert.That(report.Restart, Is.EqualTo(WolfmedRestartVerdict.CoreDestroyed));
                Assert.That(text, Does.Contain("core repair"), "the analyzer does not name core repair.");
                Assert.That(Loc.GetString("health-analyzer-wound-brain-damage-critical-core", ("activity", 0)),
                    Does.Contain("Core repair"), "the core line still advises brain repair.");
                Assert.That(Loc.GetString("health-analyzer-wound-brain-dead-core"), Does.Contain("Core repair"));
            });

            var torso = Part(ipc, BodyPartType.Torso);
            Assert.Multiple(() =>
            {
                Assert.That(surgery.WolfmedSurgeryValid(ipc, torso, "WFSurgeryRepairCore"), Is.True,
                    "core repair is not offered on a chassis with a destroyed core.");
                Assert.That(surgery.WolfmedSurgeryValid(human, Part(human, BodyPartType.Torso), "WFSurgeryRepairCore"), Is.False,
                    "core repair is offered on flesh.");
                Assert.That(SProtoMan.Index<EntityPrototype>("Multitool").HasComponent<WolfmedCoreProbeComponent>(), Is.True,
                    "the multitool is not the core repair tool.");
            });

            var tools = new List<EntityUid>
            {
                SEntMan.SpawnEntity("Wrench", map.GridCoords),
                SEntMan.SpawnEntity("Multitool", map.GridCoords),
                SEntMan.SpawnEntity("Welder", map.GridCoords),
            };

            Step("WFSurgeryStepUnseatCore", ipc, torso, tools);
            Assert.That(SEntMan.HasComponent<WolfmedCoreHousingOpenComponent>(torso), Is.True, "the wrench did not open the housing.");
            Step("WFSurgeryStepRepairCore", ipc, torso, tools);
            Step("WFSurgeryStepReseatCore", ipc, torso, tools);

            var core = life.GetBrainOrgan(ipc)!.Value;
            Assert.Multiple(() =>
            {
                Assert.That(core.Comp.Health, Is.EqualTo(core.Comp.MaxHealth), "the core was not repaired.");
                Assert.That(SEntMan.HasComponent<WolfmedCoreHousingOpenComponent>(torso), Is.False, "the housing was left open.");
                Assert.That(SEntMan.HasComponent<WolfmedBrainTraumaComponent>(ipc), Is.False, "a repaired core carries brain trauma.");
                Assert.That(SEntMan.HasComponent<WolfmedCoreRestoredComponent>(ipc), Is.True);
                Assert.That(surgery.WolfmedSurgeryValid(ipc, torso, "WFSurgeryRepairCore"), Is.False,
                    "core repair is still offered on a whole core.");
            });

            Assert.That(revival.TryRestart(ipc), Is.True);
            Assert.That(mobState.IsDead(ipc), Is.False, "the restart did not bring the chassis back.");
            var minds = SEntMan.System<SharedMindSystem>();
            Assert.That(minds.TryGetMind(ipc, out var after, out _), Is.True);
            Assert.That(after, Is.EqualTo(mind), "the chassis came back as somebody else.");
        });

        await RunSeconds(1);
        await Server.WaitAssertion(() =>
        {
            Assert.That(mobState.IsDead(ipc), Is.False, "the restarted chassis died again a second later.");
            var hud = SEntMan.GetComponent<WolfmedSyntheticHudComponent>(ipc);
            Assert.That(hud.Faults.Any(fault => fault.Line == "wolfmed-synthetic-line-core-restored"), Is.True,
                $"the readout does not say CORE RESTORED: {string.Join(", ", hud.Faults.Select(f => f.Line))}");
        });
    }

    /// <summary>
    /// <c>ExecutionAndSuicideTest</c> (OD17, P10, P29): an execution leaves the victim dead of a catastrophic brain
    /// injury with the brain at 0 where it sits, and brain repair plus a shock revives them; a suicide leaves the
    /// same body state and a ghost that cannot return.
    /// </summary>
    [Test]
    public async Task ExecutionAndSuicideTest()
    {
        await Pin();
        var map = await Pair.CreateTestMap();
        var s = new WolfmedScenario(SEntMan);
        var mobState = SEntMan.System<MobStateSystem>();
        EntityUid victim = default, attacker = default, knife = default;

        await Server.WaitPost(() =>
        {
            s.SetAir(map.MapUid, true);
            victim = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            attacker = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            knife = SEntMan.SpawnEntity("KitchenKnife", map.GridCoords);
        });
        await RunSeconds(2);

        await Server.WaitAssertion(() =>
        {
            // The victim has to be helpless to be executed: held out by a test pressure.
            s.Consciousness.SetExternalPressure(victim, "test", 1f);
            Assert.That(mobState.IsCritical(victim), Is.True);
            Assert.That(SEntMan.System<SharedHandsSystem>().TryPickupAnyHand(attacker, knife), Is.True);

            var verbs = SEntMan.System<SharedVerbSystem>().GetLocalVerbs(victim, attacker, typeof(UtilityVerb), force: true);
            var execute = verbs.FirstOrDefault(verb => verb.Text == Loc.GetString("execution-verb-name"));
            Assert.That(execute?.Act, Is.Not.Null, "no execution verb on a helpless wound host.");
            execute!.Act!.Invoke();
        });
        await RunSeconds(7);

        await Server.WaitAssertion(() =>
        {
            var brain = s.Life.GetBrainOrgan(victim);
            Assert.Multiple(() =>
            {
                Assert.That(mobState.IsDead(victim), Is.True, "the execution did not kill.");
                Assert.That(brain, Is.Not.Null, "the brain left the body.");
                Assert.That(brain!.Value.Comp.Health, Is.EqualTo(FixedPoint2.Zero), "an execution left the brain whole.");
            });

            // Revivable: brain repair and a shock.
            s.Consciousness.SetExternalPressure(victim, "test", 0f);
            s.Life.RepairBrain(victim);
            Assert.That(s.Shock(victim, out var line), Is.True, $"the paddles refused an executed body after brain repair: {line}");
            Assert.That(mobState.IsDead(victim), Is.False);
        });

        // --- Suicide: the same injury, and a ghost that cannot come back. ---
        Assert.That(ServerSession, Is.Not.Null, "The suicide half needs a connected pair.");
        var session = ServerSession!;
        EntityUid body = default, mindId = default;
        await Server.WaitPost(() =>
        {
            var minds = SEntMan.System<SharedMindSystem>();
            minds.WipeMind(session.ContentData()?.Mind);
            body = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            mindId = minds.CreateMind(session.UserId).Owner;
            minds.TransferTo(mindId, body);
        });
        await RunTicksSync(30);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.System<SuicideSystem>().Suicide(body), Is.True, "the suicide was refused.");
            var brain = s.Life.GetBrainOrgan(body);
            var ghost = session.AttachedEntity;
            Assert.Multiple(() =>
            {
                Assert.That(mobState.IsDead(body), Is.True, "a wound host's suicide did not kill.");
                Assert.That(brain, Is.Not.Null);
                Assert.That(brain!.Value.Comp.Health, Is.EqualTo(FixedPoint2.Zero), "a suicide left the brain whole.");
                Assert.That(ghost, Is.Not.EqualTo(body));
                Assert.That(SEntMan.TryGetComponent(ghost, out GhostComponent? ghostComp), Is.True, "the suicide did not ghost.");
                Assert.That(ghostComp!.CanReturnToBody, Is.False, "a suicide's ghost can return.");
            });
        });
    }

    /// <summary>
    /// <c>SepsisDeterministicTest</c> (OD15): with wolfmed.brain_sepsis_seconds set (Pin puts back the 600 that
    /// playtest 5 ships as 0), late sepsis drains the brain at 1/600 per second and the heart stops
    /// through the oxygen trigger at the derived time, 510 s from full, ±20%, the same second on every run, and the
    /// arrest is named for the sepsis.
    /// </summary>
    [Test]
    public async Task SepsisDeterministicTest()
    {
        await Pin();
        var map = await Pair.CreateTestMap();
        var s = new WolfmedScenario(SEntMan);
        EntityUid first = default, second = default;

        await Server.WaitPost(() =>
        {
            s.SetAir(map.MapUid, true);
            first = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            second = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
        });
        await RunSeconds(2);

        await Server.WaitAssertion(() =>
        {
            var arrests = new List<int>();
            foreach (var body in new[] { first, second })
            {
                SEntMan.EnsureComponent<WolfmedSepsisComponent>(body).Progress = 80f;
                var at = s.Advance(body, 900, _ => s.Life.InArrest(body));
                Assert.That(s.Life.InArrest(body), Is.True, "sepsis 80 never stopped the heart.");
                Assert.That(SEntMan.GetComponent<WolfmedCardiacArrestComponent>(body).Cause, Is.EqualTo("sepsis"),
                    "the arrest is not named for the sepsis behind it.");
                arrests.Add(at);
            }

            TestContext.Out.WriteLine($"SepsisDeterministicTest: arrest at {arrests[0]} s and {arrests[1]} s (derived 510 s).");
            Assert.Multiple(() =>
            {
                Assert.That(arrests[0], Is.EqualTo(arrests[1]), "two identical septic bodies arrested at different times.");
                Assert.That(arrests[0], Is.InRange(408, 612), "the sepsis arrest is off the derived 510 s by more than 20%.");
            });
        });
    }

    /// <summary><c>ColdBrainTest</c> (P24): below 20 C a starved brain loses tissue at a tenth of the warm rate.</summary>
    [Test]
    public async Task ColdBrainTest()
    {
        await Pin();
        var map = await Pair.CreateTestMap();
        var s = new WolfmedScenario(SEntMan);
        EntityUid warm = default, cold = default;

        await Server.WaitPost(() =>
        {
            s.SetAir(map.MapUid, true);
            warm = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            cold = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
        });
        await RunSeconds(2);

        await Server.WaitAssertion(() =>
        {
            SEntMan.GetComponent<Content.Server.Temperature.Components.TemperatureComponent>(warm).CurrentTemperature = 310f;
            SEntMan.GetComponent<Content.Server.Temperature.Components.TemperatureComponent>(cold).CurrentTemperature = 280f;

            var lost = new Dictionary<EntityUid, float>();
            foreach (var body in new[] { warm, cold })
            {
                Assert.That(s.Life.StartArrest(body, "test"), Is.True);
                var before = s.Life.GetBrainOrgan(body)!.Value.Comp.Health.Float();
                for (var second = 0; second < 10; second++)
                {
                    s.Life.SetOxygenation(body, 0f);
                    s.Life.Tick(body, 1f);
                }

                lost[body] = before - s.Life.GetBrainOrgan(body)!.Value.Comp.Health.Float();
            }

            TestContext.Out.WriteLine($"ColdBrainTest: 10 s at no oxygen cost {lost[warm]:0.000} warm, {lost[cold]:0.000} at 280 K.");
            Assert.That(lost[warm], Is.GreaterThan(0f), "a starved warm brain lost no tissue.");
            Assert.That(lost[cold] / lost[warm], Is.EqualTo(0.1f).Within(0.02f), "cold does not slow tissue loss to a tenth.");
        });
    }

    /// <summary>
    /// <c>InsulatedShockTest</c> (P28): the shock that stops a heart is the one that gets through the gloves. 100
    /// through gloves that pass 40% of it does not arrest; the same shock bare-handed does.
    /// </summary>
    [Test]
    public async Task InsulatedShockTest()
    {
        await Pin();
        var map = await Pair.CreateTestMap();
        var life = SEntMan.System<WolfmedLifeSystem>();
        EntityUid gloved = default, bare = default;

        await Server.WaitPost(() =>
        {
            gloved = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            bare = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
        });
        await RunSeconds(2);

        await Server.WaitAssertion(() =>
        {
            var gloves = SEntMan.SpawnEntity("ClothingHandsGlovesColorYellow", map.GridCoords);
            var electrocution = SEntMan.System<SharedElectrocutionSystem>();
            electrocution.SetInsulatedSiemensCoefficient(gloves, 0.4f);
            Assert.That(SEntMan.System<InventorySystem>().TryEquip(gloved, gloves, "gloves", silent: true, force: true), Is.True);

            electrocution.TryDoElectrocution(gloved, null, 100, TimeSpan.FromSeconds(1), true);
            electrocution.TryDoElectrocution(bare, null, 100, TimeSpan.FromSeconds(1), true);

            Assert.Multiple(() =>
            {
                Assert.That(life.InArrest(gloved), Is.False, "a shock the gloves cut to 40 stopped the heart.");
                Assert.That(life.InArrest(bare), Is.True, "a bare 100 shock did not stop the heart.");
            });
        });
    }

    /// <summary>
    /// <c>SutureInfectionTest</c> (P20): a sutured wound infects at the profile's Sutured rate, below an untreated
    /// one, until it grows past where it was sutured. The medicated suture carries the suture marker.
    /// </summary>
    [Test]
    public async Task SutureInfectionTest()
    {
        await Pin();
        var map = await Pair.CreateTestMap();
        EntityUid sutured = default, open = default;

        await Server.WaitPost(() =>
        {
            sutured = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            open = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
        });
        await RunSeconds(2);

        await Server.WaitAssertion(() =>
        {
            var infection = SEntMan.System<WolfmedInfectionSystem>();
            var damageable = SEntMan.System<DamageableSystem>();
            foreach (var body in new[] { sutured, open })
                damageable.TryChangeDamage(body, Spec("Slash", 20), ignoreResistances: true, targetPart: TargetBodyPart.Torso);

            var torso = Part(sutured, BodyPartType.Torso);
            Assert.That(infection.MarkSutured(torso, new[] { "Slash", "Piercing" }), Is.GreaterThan(0), "no wound took the suture.");
            Assert.That(SProtoMan.Index<EntityPrototype>("MedicatedSuture").HasComponent<WolfmedSutureComponent>(), Is.True);

            var suturedWound = Wound(torso, "SlashWound");
            var openWound = Wound(Part(open, BodyPartType.Torso), "SlashWound");

            infection.Update(6 * 60f);
            var profile = infection.Profile;
            var rate = profile.TreatmentMultipliers.GetValueOrDefault(BleedingTreatment.Sutured, 0f);
            var suturedProgress = Progress(suturedWound);
            var openProgress = Progress(openWound);
            TestContext.Out.WriteLine($"SutureInfectionTest: 6 min: sutured {suturedProgress:0.00}, open {openProgress:0.00} (Sutured rate {rate}).");
            Assert.Multiple(() =>
            {
                Assert.That(openProgress, Is.GreaterThan(0f));
                Assert.That(suturedProgress, Is.LessThan(openProgress), "a sutured wound infected like an open one.");
                Assert.That(suturedProgress, Is.EqualTo(openProgress * rate).Within(openProgress * 0.2f + 0.01f),
                    "the sutured wound does not infect at the Sutured rate.");
            });

            // Torn open again past where it was sutured: the suture no longer counts.
            SEntMan.System<WoundSystem>().ChangeSeverity(suturedWound, FixedPoint2.New(16));
            var before = Progress(suturedWound);
            infection.Update(60f);
            Assert.That(Progress(suturedWound), Is.GreaterThan(before), "a reopened wound still counts as sutured.");
            Assert.That(SEntMan.HasComponent<WolfmedSuturedComponent>(suturedWound), Is.False);
        });
    }

    private EntityUid Wound(EntityUid part, string prototype) =>
        SEntMan.System<WoundSystem>().GetWounds((part, SEntMan.GetComponent<WoundableComponent>(part)))
            .First(wound => wound.Comp.Prototype == new ProtoId<WoundPrototype>(prototype)).Owner;

    private float Progress(EntityUid wound) =>
        SEntMan.TryGetComponent(wound, out WolfmedInfectionComponent? infection) ? infection.Progress : 0f;
}
