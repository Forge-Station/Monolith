#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Content.Server._WF.Wolfmed.Life;
using Content.Server._WF.Wolfmed.Wounds;
using Content.Server.Medical.Components;
using Content.Shared._Onyx.Wounds;
using Content.Shared._Onyx.Targeting;
using Content.Shared._Shitmed.Targeting;
using Content.Shared._Onyx.Body.Systems;
using Content.Shared._WF.Wolfmed.Body;
using Content.Shared._WF.Wolfmed.Consciousness;
using Content.Shared._WF.Wolfmed.Wounds;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs.Systems;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Melee;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Spawners;

namespace Content.IntegrationTests.Tests._WF.Wolfmed.Scenarios;

/// <summary>
/// The revival budget: five standard deaths, each priced in a skilled hand medic's seconds. Revive is what it takes
/// to get the patient back and keep the heart going (stop every bleed, blood to the brain-safe line, failed organs,
/// the shock); patch-up is every wound's procedure and every point of stored damage on top. Revive must fit in
/// ten minutes.
/// </summary>
/// <remarks>
/// The prices come from the data the medic works with: each wound's analyzer procedure, the do-after delay of the
/// kit item a step names, and how much one use of the best kit item heals of a damage type. Fixed figures stand in
/// where there is no single item delay: a surgery step, a whole organ surgery, triage and the shock.
/// </remarks>
[TestFixture]
public sealed class WolfmedRevivalStopwatchTest : WolfmedGameTest
{
    /// <summary>The ceiling the owner set: revival in ten minutes at most.</summary>
    private const float ReviveBudget = 600f;

    private const float Triage = 20f;
    private const float Overhead = 1f;
    private const float SurgeryStep = 3f;
    private const float OrganSurgery = 40f;
    private const float Shock = 10f;
    private const float OtherTool = 3f;
    private const float ReagentStep = 3f;
    private const float FireSeconds = 360f;
    private const int SettleSeconds = 480;

    private static readonly string[] Kit =
        ["Brutepack", "Ointment", "Gauze", "MedicatedSuture", "RegenerativeMesh"];

    private static readonly HashSet<string> SurgeryTools =
        ["Scalpel", "Cautery", "Saw", "Bonesetter", "Retractor", "Drill", "WFWolfmedSkinGraft"];

    private static readonly WolfmedStepCheck[] StopsBleeding =
        [WolfmedStepCheck.BleedingStopped, WolfmedStepCheck.Dressed, WolfmedStepCheck.Sutured, WolfmedStepCheck.Tourniqueted];

    private enum Death
    {
        RifleMagazine,
        FireAxe,
        Fire,
        Explosion,
        BleedOut,
    }

    /// <summary>One priced body.</summary>
    private sealed record Bill(Death Death, bool Dead, int Wounds, int MostOnAPart, float WorstPart, float Total,
        string Groups, float Revive, float Patch, string Breakdown);

    [Test]
    public async Task RevivalFitsTheBudgetTest()
    {
        var map = await CreateTestMap();
        var deaths = Enum.GetValues<Death>();
        const int runs = 1; // one body per death keeps the run near a minute of CI time
        var bodies = new List<(Death Death, EntityUid Body, EntityUid Attacker, EntityUid Tool)>();

        await Server.WaitPost(() =>
        {
            new WolfmedScenario(SEntMan).KeepGrid(map.Grid);
            var x = 0f;
            foreach (var death in deaths)
            {
                for (var i = 0; i < runs; i++)
                {
                    var at = map.GridCoords.Offset(new System.Numerics.Vector2(x += 2f, 0f));
                    var body = SEntMan.SpawnEntity("MobHuman", at);
                    var attacker = SEntMan.SpawnEntity("MobHuman", at.Offset(new System.Numerics.Vector2(0f, 3f)));
                    // The round and the axe wait in nullspace: on the grid it would hit the first body it touched and delete
                    // itself, and it despawns on a timer; the rules read the hit's cause off it all fight.
                    var tool = death switch
                    {
                        Death.RifleMagazine => SEntMan.SpawnEntity("Bullet556x45mmFMJ", MapCoordinates.Nullspace),
                        Death.FireAxe => SEntMan.SpawnEntity("FireAxe", MapCoordinates.Nullspace),
                        _ => SEntMan.SpawnEntity("CombatKnife", at),
                    };
                    SEntMan.RemoveComponent<TimedDespawnComponent>(tool);
                    bodies.Add((death, body, attacker, tool));
                }
            }
        });
        await RunSeconds(1);

        // When each body first went unconscious and when it died, in seconds from the first hit.
        var downAt = new Dictionary<EntityUid, int>();
        var deadAt = new Dictionary<EntityUid, int>();
        var timeline = new StringBuilder();
        for (var second = 0; second < SettleSeconds; second++)
        {
            var now = second;
            await Server.WaitPost(() =>
            {
                foreach (var (death, body, attacker, tool) in bodies)
                {
                    Injure(death, body, attacker, tool, now);
                    if (!downAt.ContainsKey(body) &&
                        SEntMan.GetComponent<WolfmedConsciousnessComponent>(body).State == WolfmedConsciousness.Unconscious)
                        downAt[body] = now;
                    if (!deadAt.ContainsKey(body) && SEntMan.System<MobStateSystem>().IsDead(body))
                        deadAt[body] = now;
                }

                if (now % 60 == 30)
                {
                    foreach (var death in deaths)
                    {
                        var first = bodies.First(b => b.Death == death).Body;
                        timeline.AppendLine($"{now,4}s {death,-15} {Status(first)}");
                    }
                }
            });
            await RunSeconds(1);
        }

        var bills = new List<Bill>();
        var before = new Dictionary<EntityUid, string>();
        var woundLists = new StringBuilder();
        await Server.WaitPost(() =>
        {
            foreach (var (death, body, _, _) in bodies)
            {
                bills.Add(Price(death, body));
                before[body] = Status(body);
            }

            foreach (var death in deaths)
                woundLists.AppendLine($"{death,-15} {WoundList(bodies.First(b => b.Death == death).Body)}");
        });

        // The revive, done for real: every bleed stopped, blood to the brain-safe line, failed organs repaired, a shock.
        var shocks = new Dictionary<EntityUid, string>();
        await Server.WaitPost(() =>
        {
            foreach (var (_, body, _, _) in bodies)
                shocks[body] = Revive(body);
        });
        await RunSeconds(120);

        var after = new Dictionary<EntityUid, string>();
        await Server.WaitPost(() =>
        {
            foreach (var (_, body, _, _) in bodies)
                after[body] = Status(body);
        });

        var report = new StringBuilder();
        report.AppendLine("death           dead wounds most worstPart total  revive  patch   groups");
        foreach (var bill in bills)
        {
            report.AppendLine($"{bill.Death,-15} {(bill.Dead ? "yes" : "no"),-4} {bill.Wounds,6} {bill.MostOnAPart,4} " +
                              $"{bill.WorstPart,9:0} {bill.Total,5:0} {bill.Revive,7:0} {bill.Patch,6:0}   {bill.Groups}");
            report.AppendLine($"    {bill.Breakdown}");
        }

        report.AppendLine();
        report.Append(woundLists);
        report.AppendLine();
        report.Append(timeline);
        report.AppendLine();
        foreach (var (death, body, _, _) in bodies)
        {
            report.AppendLine($"{death,-15} down {(downAt.TryGetValue(body, out var d) ? $"{d}s" : "never")}, " +
                              $"dead {(deadAt.TryGetValue(body, out var k) ? $"{k}s" : "never")}");
            report.AppendLine($"    before: {before[body]}");
            report.AppendLine($"    shock:  {shocks[body]}");
            report.AppendLine($"    +120s:  {after[body]}");
        }

        TestContext.Out.WriteLine(report.ToString());

        Assert.Multiple(() =>
        {
            foreach (var bill in bills)
                Assert.That(bill.Revive, Is.LessThanOrEqualTo(ReviveBudget),
                    $"{bill.Death}: revival takes {bill.Revive:0} s ({bill.Breakdown}).");

            foreach (var (death, body, _, _) in bodies)
                Assert.That(after[body], Does.Not.StartWith("DEAD").And.Contain("arrest False"),
                    $"{death}: the revive did not hold. {after[body]}");
        });
    }

    /// <summary>What holds the body: its state and cause, blood, brain, pain and heart.</summary>
    private string Status(EntityUid body)
    {
        var life = SEntMan.System<WolfmedLifeSystem>();
        var consciousness = SEntMan.GetComponent<WolfmedConsciousnessComponent>(body);
        var brain = life.GetBrainOrgan(body);
        var pain = SEntMan.TryGetComponent(body, out PainComponent? painComp)
            ? SEntMan.System<PainSystem>().GetPain((body, painComp)).Float()
            : 0f;
        return $"{(SEntMan.System<MobStateSystem>().IsDead(body) ? "DEAD" : consciousness.State.ToString())} " +
               $"cause {consciousness.Cause}/{consciousness.CauseSource} blockers [{consciousness.Blockers}] " +
               $"blood {life.GetBlood(body):P0} oxy {life.GetOxygenation(body):0.00} " +
               $"brain {brain?.Comp.Health.Float() ?? 0f:0.0}/{brain?.Comp.MaxHealth.Float() ?? 0f:0} " +
               $"pain {pain:0} arrest {life.InArrest(body)} " +
               $"drain {(life.GetBrain(body) is { } b ? $"{life.DrainRate(body, b, out var source):0.000} {source}" : "-")} " +
               $"fluid {SEntMan.System<WolfmedFluidLossSystem>().GetRate(body):0.00}/s bleed {life.GetBleedRate(body):0.00}/s " +
               $"organs [{OrganList(body)}]";
    }

    /// <summary>Every open wound, by part: prototype and severity.</summary>
    private string WoundList(EntityUid body)
    {
        var wounds = SEntMan.System<WoundSystem>();
        var parts = new List<string>();
        foreach (var (part, component) in SEntMan.System<SharedBodySystem>().GetBodyChildren(body))
        {
            if (!SEntMan.TryGetComponent(part, out WoundableComponent? woundable))
                continue;

            var list = wounds.GetWounds((part, woundable))
                .Where(w => w.Comp.State is not WoundState.Healed and not WoundState.Scarred)
                .Select(w => $"{w.Comp.Prototype.Id.Replace("WFWolfmed", "")}:{w.Comp.Severity.Float():0}");
            parts.Add($"{component.Symmetry}{component.PartType}[{string.Join(" ", list)}]");
        }

        return string.Join(" ", parts);
    }

    /// <summary>Every Wolfmed organ in the body with its health, by prototype.</summary>
    private string OrganList(EntityUid body)
    {
        var names = new List<string>();
        foreach (var organ in SEntMan.System<SharedBodySystem>().GetBodyOrgans(body))
        {
            if (SEntMan.TryGetComponent(organ.Id, out WolfmedOrganComponent? data))
                names.Add($"{SEntMan.GetComponent<MetaDataComponent>(organ.Id).EntityPrototype?.ID}:{data.Health.Float():0}");
        }

        return string.Join(" ", names);
    }

    /// <summary>The revive the bill prices, done at once: the medic's work without the medic's time.</summary>
    private string Revive(EntityUid body)
    {
        var wounds = SEntMan.System<WoundSystem>();
        var fluid = SEntMan.System<WolfmedFluidLossSystem>();
        foreach (var (part, _) in SEntMan.System<SharedBodySystem>().GetBodyChildren(body))
        {
            if (!SEntMan.TryGetComponent(part, out WoundableComponent? woundable))
                continue;

            foreach (var wound in wounds.GetWounds((part, woundable)).ToList())
            {
                if (SEntMan.HasComponent<WoundInternalBleedingComponent>(wound))
                    wounds.RemoveWound(wound.Owner);
                else if (SEntMan.HasComponent<WoundBleedingComponent>(wound))
                    wounds.CloseWound(wound.Owner);
                else if (fluid.WoundRate(wound) > 0f)
                    SEntMan.EnsureComponent<WolfmedDressedComponent>(wound).TreatedSeverity = wound.Comp.Severity;
            }
        }

        var scenario = new WolfmedScenario(SEntMan);
        var (_, toSafe) = scenario.Life.GetTransfusionGuidance(body);
        if (toSafe > 0f)
            scenario.Transfuse(body, toSafe);

        var brain = scenario.Life.GetBrainOrgan(body);
        foreach (var organ in SEntMan.System<SharedBodySystem>().GetBodyOrgans(body))
        {
            if (brain?.Owner != organ.Id && SEntMan.TryGetComponent(organ.Id, out WolfmedOrganComponent? data) &&
                data.Health <= FixedPoint2.Zero)
                SEntMan.System<OrganHealthSystem>().SetHealth((organ.Id, data), data.MaxHealth);
        }

        // A heart that was destroyed outright is gone: the medic brings a spare and transplants it.
        var revival = SEntMan.System<WolfmedRevivalSystem>();
        if (scenario.Life.GetHeartHealth(body) == null && revival.ExpectsHeart(body))
        {
            var torso = scenario.Part(body, BodyPartType.Torso);
            var heart = SEntMan.SpawnEntity("OrganHumanHeart", SEntMan.GetComponent<TransformComponent>(body).Coordinates);
            SEntMan.System<SharedBodySystem>().InsertOrgan(torso, heart, "heart");
        }

        if (!scenario.Life.InArrest(body) && !SEntMan.System<MobStateSystem>().IsDead(body))
            return "not needed";

        scenario.Shock(body, out var line);
        return line;
    }

    /// <summary>What each death does to its body in the given second of the fight.</summary>
    private void Injure(Death death, EntityUid body, EntityUid attacker, EntityUid tool, int second)
    {
        if (!SEntMan.EntityExists(body))
            return;

        var damageable = SEntMan.System<DamageableSystem>();
        switch (death)
        {
            // A 30-round magazine at three hits a second, aimed at centre mass.
            case Death.RifleMagazine when second < 10:
                var round = SEntMan.GetComponent<ProjectileComponent>(tool).Damage;
                for (var i = 0; i < 3; i++)
                    damageable.TryChangeDamage(body, round, origin: attacker, targetPart: TargetBodyPart.Torso, tool: tool);
                break;

            // Twenty swings of a fire axe, one a second, aimed at centre mass.
            case Death.FireAxe when second < 20:
                var swing = SEntMan.GetComponent<MeleeWeaponComponent>(tool).Damage;
                damageable.TryChangeDamage(body, swing, origin: attacker, targetPart: TargetBodyPart.Torso, tool: tool);
                break;

            // Ten fire stacks for six minutes: the body burns where it lies, the hit landing on a random part.
            case Death.Fire when second < FireSeconds:
                damageable.TryChangeDamage(body, WolfmedScenario.Spec("Heat", 5f), interruptsDoAfters: false);
                break;

            // Two blasts a second apart, spread over the whole body.
            case Death.Explosion when second < 2:
                var blast = new DamageSpecifier
                {
                    DamageDict =
                    {
                        [new ProtoId<DamageTypePrototype>("Blunt")] = FixedPoint2.New(60),
                        [new ProtoId<DamageTypePrototype>("Heat")] = FixedPoint2.New(40),
                        [new ProtoId<DamageTypePrototype>("Piercing")] = FixedPoint2.New(30),
                    },
                };
                SEntMan.System<WoundDamageRoutingSystem>().TryRouteDistributedDamage(body, blast, TargetBodyPart.All,
                    DamageDistribution.SplitWithVariation, variation: 0.5f, isExplosion: true,
                    originFlag: DamageableSystem.DamageOriginFlag.Explosion);
                break;

            // One deep knife cut to the thigh, then nobody comes.
            case Death.BleedOut when second == 0:
                var rules = SEntMan.System<WolfmedWoundRuleSystem>();
                rules.ForcedRoll = 0f;
                try
                {
                    damageable.TryChangeDamage(body, WolfmedScenario.Spec("Slash", 30f), origin: attacker,
                        targetPart: TargetBodyPart.LeftLeg, tool: tool);
                }
                finally
                {
                    rules.ForcedRoll = null;
                }

                break;
        }
    }

    private Bill Price(Death death, EntityUid body)
    {
        var wounds = SEntMan.System<WoundSystem>();
        var bodies = SEntMan.System<SharedBodySystem>();
        var life = SEntMan.System<WolfmedLifeSystem>();
        var protos = SProtoMan;

        var revive = Triage + Shock;
        var patch = Triage + Shock;
        var breakdown = new Dictionary<string, float>();
        void Add(string key, float seconds, bool forRevival)
        {
            breakdown[key] = breakdown.GetValueOrDefault(key) + seconds;
            patch += seconds;
            if (forRevival)
                revive += seconds;
        }

        var count = 0;
        var most = 0;
        var worstPart = 0f;
        foreach (var (part, _) in bodies.GetBodyChildren(body))
        {
            if (!SEntMan.TryGetComponent(part, out WoundableComponent? woundable))
                continue;

            var onPart = 0;
            foreach (var wound in wounds.GetWounds((part, woundable)))
            {
                if (wound.Comp.State is WoundState.Healed or WoundState.Scarred ||
                    wound.Comp.Prototype == "MedicalScarWound")
                    continue;

                onPart++;
                var procedureId = WolfmedTreatmentAdvice.ProcedureId(wound.Comp.Prototype, false);
                if (!protos.TryIndex<WolfmedTreatmentProcedurePrototype>(procedureId, out var procedure))
                    continue;

                var bleeding = SEntMan.TryGetComponent(wound, out WoundBleedingComponent? bleed) && bleed.CurrentRate > 0f ||
                               SEntMan.TryGetComponent(wound, out WoundInternalBleedingComponent? inner) && inner.Rate > 0f ||
                               SEntMan.System<WolfmedFluidLossSystem>().WoundRate(wound) > 0f;
                var stopped = !bleeding;
                foreach (var step in procedure.Steps)
                {
                    var cost = StepCost(step, wound.Owner);
                    var forRevival = !stopped;
                    if (step.Done.Any(StopsBleeding.Contains))
                        stopped = true;
                    if (cost > 0f)
                        Add(forRevival ? "bleeds" : "wounds", cost, forRevival);
                }
            }

            count += onPart;
            most = Math.Max(most, onPart);

            if (!SEntMan.TryGetComponent(part, out DamageableComponent? partDamage))
                continue;

            var stored = 0f;
            foreach (var (type, amount) in partDamage.Damage.DamageDict)
            {
                if (amount <= FixedPoint2.Zero)
                    continue;

                stored += amount.Float();
                if (BestHeal(type) is { } heal)
                    Add("damage", MathF.Ceiling(amount.Float() / heal.Amount) * (heal.Delay + Overhead), false);
            }

            worstPart = Math.Max(worstPart, stored);
        }

        // Blood to the line where the brain stops draining, a pack use at a time.
        var pack = Healing("Bloodpack");
        var (_, toSafe) = life.GetTransfusionGuidance(body);
        if (toSafe > 0f && pack != null)
            Add("blood", MathF.Ceiling(toSafe / pack.ModifyBloodLevel) * (pack.Delay + Overhead), true);

        if (life.GetHeartHealth(body) == null && SEntMan.System<WolfmedRevivalSystem>().ExpectsHeart(body))
            Add("heart", OrganSurgery, true);

        var brain = life.GetBrainOrgan(body);
        foreach (var organ in bodies.GetBodyOrgans(body))
        {
            if (!SEntMan.TryGetComponent(organ.Id, out WolfmedOrganComponent? data))
                continue;

            var isBrain = brain?.Owner == organ.Id;
            if (data.Health <= FixedPoint2.Zero && !isBrain)
                Add("organs", OrganSurgery, true);
            else if (data.Health < data.MaxHealth * data.ImpairedBelow || isBrain && data.Health < data.MaxHealth)
                Add("organs", OrganSurgery, false);
        }

        var damage = SEntMan.GetComponent<DamageableComponent>(body);
        var groups = string.Join(" ", damage.Damage.DamageDict
            .Where(kv => kv.Value > FixedPoint2.Zero)
            .GroupBy(kv => GroupOf(kv.Key))
            .Select(g => $"{g.Key} {g.Sum(kv => kv.Value.Float()):0}"));

        return new Bill(death,
            SEntMan.System<MobStateSystem>().IsDead(body),
            count,
            most,
            worstPart,
            damage.TotalDamage.Float(),
            groups,
            revive,
            patch,
            string.Join(", ", breakdown.Select(kv => $"{kv.Key} {kv.Value:0}")));
    }

    /// <summary>What one procedure step costs a skilled medic, in seconds.</summary>
    private float StepCost(WolfmedTreatmentStep step, EntityUid wound)
    {
        if (step.Reagent != null)
            return ReagentStep + Overhead;

        if (step.Tool is not { } tool)
            return step.Surgery ? SurgeryStep + Overhead : 0f;

        switch (tool.Id)
        {
            case "HandheldHealthAnalyzer":
                return 0f;
            case "Defibrillator":
                return 0f; // The shock is priced once per body.
            case "Hemostat":
                var embedded = SEntMan.TryGetComponent(wound, out WolfmedEmbeddedObjectComponent? lodged)
                    ? lodged.SharpDelay.TotalSeconds
                    : SurgeryStep;
                return (float) embedded + Overhead;
            case "Tourniquet":
                return 1f + Overhead;
            case "WFWolfmedSplint":
                return 4f + Overhead;
        }

        if (SurgeryTools.Contains(tool.Id))
            return SurgeryStep + Overhead;

        return (Healing(tool.Id)?.Delay ?? OtherTool) + Overhead;
    }

    private HealingComponent? Healing(string id) =>
        SProtoMan.Index<EntityPrototype>(id).TryComp(out HealingComponent? healing, SEntMan.ComponentFactory)
            ? healing
            : null;

    /// <summary>The kit item that heals the most of a damage type per use, and its delay.</summary>
    private (float Amount, float Delay)? BestHeal(string type)
    {
        (float Amount, float Delay)? best = null;
        foreach (var id in Kit)
        {
            if (Healing(id) is not { } healing ||
                healing.TreatedDamageTypes is { Count: > 0 } treated && !treated.Contains(type))
                continue;

            var amount = -healing.Damage.DamageDict.GetValueOrDefault(type).Float();
            if (amount > 0f && (best == null || amount / healing.Delay > best.Value.Amount / best.Value.Delay))
                best = (amount, healing.Delay);
        }

        return best;
    }

    private string GroupOf(string type)
    {
        foreach (var group in SProtoMan.EnumeratePrototypes<DamageGroupPrototype>())
        {
            if (group.DamageTypes.Contains(type))
                return group.ID;
        }

        return type;
    }
}
