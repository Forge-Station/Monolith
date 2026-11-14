#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Server._WF.Wolfmed.Consciousness;
using Content.Server._WF.Wolfmed.Wounds;
using Content.Shared._Onyx.Wounds;
using Content.Shared._WF.Wolfmed.Body;
using Content.Shared._WF.Wolfmed.CCVar;
using Content.Shared._WF.Wolfmed.Consciousness;
using Content.Shared._WF.Wolfmed.Wounds;
using Content.Shared.Atmos.Components;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Chemistry.Components;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Content.Shared.Fluids.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Traits.Assorted;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Wolfmed.Scenarios;

/// <summary>
/// M1b (plan §3.7, §6, OD11, OD12): burns kill through fluid loss into blood volume, a dressing slows it and a
/// graft stops it; a ceiling limits what a part stores, never what a hit does; a charred appendage that keeps
/// cooking crumbles, the head and torso never do.
/// </summary>
/// <remarks>Times are asserted as order plus a ±20% band, with every CVar the arithmetic reads pinned.</remarks>
[TestFixture]
[TestOf(typeof(WolfmedFluidLossSystem))]
public sealed class WolfmedBurnScenarioTest : GameTest
{
    private const float Band = 0.2f;
    private const float FaintSeconds = 20f;

    private async Task Pin()
    {
        await OverrideCVar(Side.Server, WolfmedCVars.Consciousness, true);
        await OverrideCVar(Side.Server, WolfmedCVars.ConsciousnessPainDown, 0.95f);
        await OverrideCVar(Side.Server, WolfmedCVars.ConsciousnessPainOut, 1.4f);
        await OverrideCVar(Side.Server, WolfmedCVars.ConsciousnessBloodDown, 0.5f);
        await OverrideCVar(Side.Server, WolfmedCVars.ConsciousnessBloodOut, 0.35f);
        await OverrideCVar(Side.Server, WolfmedCVars.ArrestBlood, 0.30f);
        await OverrideCVar(Side.Server, WolfmedCVars.PainFaintSeconds, FaintSeconds);
        await OverrideCVar(Side.Server, WolfmedCVars.PainFaintRise, 40f);
        await OverrideCVar(Side.Server, WolfmedCVars.PainFaintCooldown, 50f);
        await OverrideCVar(Side.Server, WolfmedCVars.AmbientPartCapFraction, 0.8f);
        await OverrideCVar(Side.Server, WolfmedCVars.BodyDamageCap, 600f);
        await OverrideCVar(Side.Server, WolfmedCVars.BurnFluidRate, 1f);
        await OverrideCVar(Side.Server, WolfmedCVars.BurnDressedFluidFactor, 0.25f);
        await OverrideCVar(Side.Server, WolfmedCVars.BurnTreatmentLostSeverity, 15f);
        await OverrideCVar(Side.Server, WolfmedCVars.CharEscalation, 1f);
    }

    private WolfmedFluidLossSystem Fluid => SEntMan.System<WolfmedFluidLossSystem>();
    private WolfmedPartHitSystem Hits => SEntMan.System<WolfmedPartHitSystem>();
    private WoundDamageRoutingSystem Routing => SEntMan.System<WoundDamageRoutingSystem>();

    private static DamageSpecifier Spec(string type, float amount) => WolfmedScenario.Spec(type, amount);

    /// <summary>Sum of burn-family wound severity on one part.</summary>
    private float BurnSeverity(EntityUid part)
    {
        var total = 0f;
        foreach (var wound in SEntMan.System<WoundSystem>().GetWounds(part))
        {
            var id = wound.Comp.Prototype.Id;
            if (id == "BurnWound" || id == "WFWolfmedCharringWound")
                total += wound.Comp.Severity.Float();
        }

        return total;
    }

    private float WoundSeverity(EntityUid part, string prototype) =>
        SEntMan.System<WoundSystem>().GetWounds(part)
            .Where(w => w.Comp.Prototype.Id == prototype)
            .Sum(w => w.Comp.Severity.Float());

    /// <summary>What the part's weeping wounds lose a second, from the wounds themselves.</summary>
    private float PartFluid(EntityUid part) =>
        SEntMan.System<WoundSystem>().GetWounds(part)
            .Where(w => SEntMan.HasComponent<WolfmedFluidLossComponent>(w))
            .Sum(w => Fluid.WoundRate(w));

    private float PartHeat(EntityUid part) =>
        SEntMan.GetComponent<DamageableComponent>(part).Damage.DamageDict.GetValueOrDefault("Heat").Float();

    private float Stored(EntityUid part) => SEntMan.GetComponent<DamageableComponent>(part).TotalDamage.Float();

    private List<EntityUid> Parts(EntityUid body) =>
        SEntMan.System<SharedBodySystem>().GetBodyChildren(body).Select(p => p.Id).ToList();

    private int Puddles()
    {
        var count = 0;
        var query = SEntMan.EntityQueryEnumerator<PuddleComponent>();
        while (query.MoveNext(out _, out _))
            count++;
        return count;
    }

    /// <summary>
    /// The uncapped-fire measurement (plan §3.7, §14): a human in a 10-stack fire in station air, never patting
    /// it out. Records every hit's Total as the dispatcher hands it on (what the fire really did, stored or
    /// not), what was stored, burn severity, fluid loss and blood over time. The burn rate is set against it.
    /// </summary>
    [Test]
    public async Task FireMeasurementTest()
    {
        await Pin();
        var map = await Pair.CreateTestMap();
        var s = new WolfmedScenario(SEntMan);
        EntityUid a = default;
        var parts = new List<(string Name, EntityUid Id)>();

        await Server.WaitPost(() =>
        {
            s.SetAir(map.MapUid, true);
            s.KeepGrid(map.Grid);
            a = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
        });
        await RunSeconds(2);
        await Server.WaitPost(() =>
        {
            foreach (var (id, part) in SEntMan.System<SharedBodySystem>().GetBodyChildren(a))
                parts.Add(($"{part.PartType}{(part.Symmetry == BodyPartSymmetry.None ? "" : part.Symmetry.ToString()[0].ToString())}", id));
            SEntMan.System<FlammableSystem>().SetFireStacks(a, 10, ignite: true);
        });

        var totalHeat = 0f;
        var storedHeat = 0f;
        Hits.Observer = (_, hit) =>
        {
            if (hit.Body == a)
                totalHeat += hit.Total.DamageDict.GetValueOrDefault("Heat").Float();
        };

        var lines = new List<string>();
        var fireOut = -1;
        var arrest = -1;
        try
        {
            for (var second = 0; second <= 300; second += 10)
            {
                await Server.WaitPost(() =>
                {
                    var flammable = SEntMan.GetComponent<FlammableComponent>(a);
                    if (!flammable.OnFire && fireOut < 0)
                        fireOut = second;
                    if (s.Life.InArrest(a) && arrest < 0)
                        arrest = second;

                    var alive = parts.Where(p => !SEntMan.Deleted(p.Id)).ToList();
                    storedHeat = alive.Sum(p => PartHeat(p.Id));
                    var burns = alive.Sum(p => BurnSeverity(p.Id));
                    lines.Add($"{second}s stacks {flammable.FireStacks:0.0} heatTotal {totalHeat:0} stored {storedHeat:0} " +
                              $"burns {burns:0} fluid {Fluid.GetRate(a):0.00}u/s blood {s.Blood(a):P0} state {s.State(a)} parts[" +
                              string.Join(" ", alive.Select(p => $"{p.Name}:{PartHeat(p.Id):0}/{BurnSeverity(p.Id):0}")) + "]");
                });
                await RunSeconds(10);
            }
        }
        finally
        {
            Hits.Observer = null;
        }

        TestContext.Out.WriteLine($"FireMeasurement: fire out at {fireOut} s, arrest at {arrest} s, Heat total " +
                                  $"{totalHeat:0}, stored {storedHeat:0}, not stored {totalHeat - storedHeat:0}.");
        foreach (var line in lines)
            TestContext.Out.WriteLine(line);

        Assert.Multiple(() =>
        {
            Assert.That(totalHeat, Is.GreaterThan(1000f), "the fire landed far less Heat than measured (1615).");
            Assert.That(fireOut, Is.GreaterThan(0), "the fire never went out.");
            Assert.That(arrest, Is.GreaterThan(fireOut), "the untreated burns never arrested the patient.");
        });
    }

    /// <summary>
    /// The burn route end to end (plan §12 M1b). Untreated: Heat keeps landing past the old 600, burns keep
    /// growing, nothing is destroyed, the faints are short, the patient breathes, blood falls with no puddle and
    /// the heart stops on blood inside the window the measured burns predict. Treated at 60 s (extinguished,
    /// dressed, 60 u): fluid loss falls to a quarter, a graft stops it, the patient is stable at 10 min, and an
    /// opiate stands them up.
    /// </summary>
    [Test]
    public async Task BurnScenarioTest()
    {
        await Pin();
        var map = await Pair.CreateTestMap();
        var s = new WolfmedScenario(SEntMan);
        EntityUid a = default;
        EntityUid b = default;
        var partsA = new List<EntityUid>();

        await Server.WaitPost(() =>
        {
            s.SetAir(map.MapUid, true);
            s.KeepGrid(map.Grid);
            a = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            // Far enough apart that neither body can set the other alight again.
            b = SEntMan.SpawnEntity("MobHuman", new MapCoordinates(new Vector2(40f, 40f), map.MapId));
        });
        await RunSeconds(2);

        var heatA = 0f;
        await Server.WaitPost(() =>
        {
            partsA = Parts(a);
            Hits.Observer = (_, hit) =>
            {
                if (hit.Body == a)
                    heatA += hit.Total.DamageDict.GetValueOrDefault("Heat").Float();
            };
            SEntMan.System<FlammableSystem>().SetFireStacks(a, 10, ignite: true);
            SEntMan.System<FlammableSystem>().SetFireStacks(b, 10, ignite: true);
        });

        var faints = new List<(float Start, float Length)>();
        float? faintStart = null;
        float burnsAt50 = 0f, burnsAt100 = 0f;
        float? derivedArrest = null;
        float? arrestAt = null;
        float? rateBeforeDressing = null, rateAfterDressing = null, rateAfterGraft = null;
        var bloodB540 = 0f;
        var tick = 5f;

        try
        {
            for (var t = 0f; t <= 600f; t += tick)
            {
                var now = t;
                await Server.WaitAssertion(() =>
                {
                    // --- A: untreated. ---
                    if (arrestAt == null)
                    {
                        var consc = s.Vitals(a);
                        if (s.Life.InArrest(a))
                        {
                            arrestAt = now;
                            Assert.That(consc.CauseSource, Is.EqualTo(WolfmedCauseSource.ArrestBlood),
                                "the burned patient's heart stopped on something other than blood.");
                        }
                        else
                        {
                            Assert.That(s.Breathing.BreathingSuppressed(a), Is.False, $"a burned patient stopped breathing at {now} s.");
                            Assert.That(s.Damage(a, "Asphyxiation"), Is.EqualTo(FixedPoint2.Zero), "a burned patient suffocated.");
                            Assert.That(partsA.All(p => !SEntMan.Deleted(p) && SEntMan.System<SharedBodySystem>().BodyHasChild(a, p)),
                                Is.True, $"environmental heat destroyed a part by {now} s.");

                            var faint = consc.State == WolfmedConsciousness.Unconscious && consc.Cause == WolfmedCause.PainFaint;
                            if (faint && faintStart == null)
                                faintStart = now;
                            else if (!faint && faintStart is { } start)
                            {
                                faints.Add((start, now - start));
                                faintStart = null;
                            }
                        }

                        var burns = Parts(a).Sum(BurnSeverity);
                        if (MathF.Abs(now - 50f) < 0.1f)
                            burnsAt50 = burns;
                        if (MathF.Abs(now - 100f) < 0.1f)
                            burnsAt100 = burns;

                        // The fire is out and the burns have settled: predict the arrest from them.
                        if (MathF.Abs(now - 130f) < 0.1f)
                        {
                            var stream = SEntMan.GetComponent<BloodstreamComponent>(a);
                            FixedPoint2 max = stream.BloodMaxVolume;
                            FixedPoint2 refresh = stream.BloodRefreshAmount;
                            var pool = max.Float();
                            var regen = refresh.Float() / (float) stream.UpdateInterval.TotalSeconds;
                            var net = Fluid.GetRate(a) - regen;
                            Assert.That(net, Is.GreaterThan(0f), "the measured burns lose less than regeneration.");
                            derivedArrest = now + (s.Blood(a) - 0.30f) * pool / net;
                            TestContext.Out.WriteLine($"BurnScenario: burns {burns:0}, fluid {Fluid.GetRate(a):0.00} u/s, " +
                                                      $"blood {s.Blood(a):P0} at 130 s; derived arrest {derivedArrest:0} s.");
                        }
                    }

                    // --- B: extinguished, dressed and given 60 u at 60 s; grafted at 120 s. ---
                    if (MathF.Abs(now - 60f) < 0.1f)
                    {
                        SEntMan.System<FlammableSystem>().Extinguish(b);
                        Fluid.Tick(0f);
                        rateBeforeDressing = Fluid.GetRate(b);
                        foreach (var part in Parts(b))
                            Fluid.Dress(part);
                        Fluid.Tick(0f);
                        rateAfterDressing = Fluid.GetRate(b);
                        s.Transfuse(b, 60f);
                    }

                    if (MathF.Abs(now - 120f) < 0.1f)
                    {
                        foreach (var part in Parts(b))
                            Fluid.Graft(part);
                        Fluid.Tick(0f);
                        rateAfterGraft = Fluid.GetRate(b);
                    }

                    if (MathF.Abs(now - 540f) < 0.1f)
                        bloodB540 = s.Blood(b);
                });

                await RunSeconds(tick);
            }
        }
        finally
        {
            Hits.Observer = null;
        }

        TestContext.Out.WriteLine($"BurnScenario: Heat on A {heatA:0}; burns {burnsAt50:0} at 50 s, {burnsAt100:0} at 100 s; " +
                                  $"faints {string.Join(", ", faints.Select(f => $"{f.Start:0}s+{f.Length:0}s"))}; " +
                                  $"arrest {arrestAt} s (derived {derivedArrest:0}); B fluid {rateBeforeDressing:0.00} -> " +
                                  $"{rateAfterDressing:0.00} dressed -> {rateAfterGraft:0.00} grafted.");

        await Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(heatA, Is.GreaterThan(600f), "Heat stopped landing at the old 600.");
                Assert.That(burnsAt100, Is.GreaterThan(burnsAt50), "burn severity stopped rising.");
                Assert.That(faints, Is.Not.Empty, "the fire never fainted the patient.");
                Assert.That(faints.All(f => f.Length <= FaintSeconds + tick), Is.True, "a faint ran past 20 s.");
                // Plan §2.3's per-encounter budget: at most 40 s Critical in the fire's two minutes.
                Assert.That(faints.Where(f => f.Start < 120f).Sum(f => MathF.Min(f.Length, 120f - f.Start)),
                    Is.LessThanOrEqualTo(40f + tick), "more than 40 s of faints in the fire's two minutes.");
                Assert.That(Puddles(), Is.EqualTo(0), "burns spilled blood on the floor.");

                Assert.That(arrestAt, Is.Not.Null, "the untreated burns never stopped the heart.");
                Assert.That(derivedArrest, Is.Not.Null);
                Assert.That(arrestAt!.Value, Is.InRange(derivedArrest!.Value * (1 - Band), derivedArrest.Value * (1 + Band)),
                    "the arrest came outside the window the measured burns predict.");

                Assert.That(rateBeforeDressing, Is.GreaterThan(0f));
                Assert.That(rateAfterDressing!.Value / rateBeforeDressing!.Value, Is.EqualTo(0.25f).Within(0.01f),
                    "a dressing did not cut the fluid loss to a quarter.");
                Assert.That(rateAfterGraft, Is.EqualTo(0f), "a graft did not stop the fluid loss.");

                var mob = SEntMan.System<MobStateSystem>();
                Assert.That(mob.IsDead(b), Is.False, "the treated patient died.");
                Assert.That(s.Life.InArrest(b), Is.False);
                Assert.That(s.State(b), Is.AnyOf(WolfmedConsciousness.Up, WolfmedConsciousness.Downed));
                Assert.That(s.Blood(b), Is.GreaterThanOrEqualTo(bloodB540 - 0.005f), "the treated patient's blood is still falling.");
            });

            SEntMan.System<BloodstreamSystem>().TryAddToChemicals(b, new Solution("WFWolfmedOpiate", FixedPoint2.New(5)));
        });

        var stood = false;
        for (var i = 0; i < 30 && !stood; i++)
        {
            await RunSeconds(1);
            await Server.WaitPost(() => stood = s.State(b) == WolfmedConsciousness.Up);
        }

        Assert.That(stood, Is.True, "an opiate did not stand the treated burn patient up.");
    }

    /// <summary>
    /// Same hit, same consequences, saturated or not (P8, plan §6.2). A torso at its 250 and a fresh one each take
    /// the same five stabs: per stab the wound growth, the bleed-rate change and the Total handed on match; the
    /// saturated torso stores nothing (Applied 0, Overflow the whole stab), never fractures, stays at 250 with its
    /// pain at the clamp; one event per stab; the accumulator and the body total count Applied only.
    /// </summary>
    [Test]
    public async Task SaturatedTorsoTest()
    {
        await Pin();
        var map = await Pair.CreateTestMap();
        var s = new WolfmedScenario(SEntMan);
        EntityUid sat = default, fresh = default, attacker = default, satTorso = default, freshTorso = default;

        await Server.WaitAssertion(() =>
        {
            s.SetAir(map.MapUid, true);
            s.KeepGrid(map.Grid);
            sat = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            fresh = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            attacker = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            satTorso = s.Part(sat, BodyPartType.Torso);
            freshTorso = s.Part(fresh, BodyPartType.Torso);

            foreach (var body in new[] { sat, fresh })
                SEntMan.EnsureComponent<PainNumbnessComponent>(body);

            // Saturate with a burn: a separate wound, so the stabs' own wound starts equal on both torsos.
            Routing.TryApplyPartDamage(sat, satTorso, Spec("Heat", 250), attacker, ignoreResistances: true);
            Assert.That(Stored(satTorso), Is.EqualTo(250f).Within(0.01f), "the torso did not saturate at 250.");
        });

        var events = new Dictionary<EntityUid, List<PartDamageAppliedEvent>>();
        Hits.Observer = (part, hit) =>
        {
            if (!events.TryGetValue(hit.Body, out var list))
                events[hit.Body] = list = new List<PartDamageAppliedEvent>();
            list.Add(hit);
        };

        try
        {
            await Server.WaitAssertion(() =>
            {
                var damageable = SEntMan.System<DamageableSystem>();
                var bleeding = SEntMan.System<WoundBleedingSystem>();
                var satBody = SEntMan.GetComponent<DamageableComponent>(sat).TotalDamage;
                var satPainBefore = SEntMan.GetComponent<PainComponent>(satTorso).Value;

                for (var shot = 1; shot <= 5; shot++)
                {
                    var results = new Dictionary<EntityUid, (float Growth, float Bleed, float Total, float Applied, float Overflow, float Returned, int Events)>();
                    foreach (var (body, torso) in new[] { (sat, satTorso), (fresh, freshTorso) })
                    {
                        events.Remove(body);
                        var woundBefore = WoundSeverity(torso, "PiercingWound");
                        var bleedBefore = bleeding.GetPartRate(torso);

                        // The routed call's return is the D27 accumulator.
                        var returned = damageable.TryChangeDamage(body, Spec("Piercing", 20), ignoreResistances: true,
                            origin: attacker, targetPart: Content.Shared._Shitmed.Targeting.TargetBodyPart.Torso);

                        var hits = events.GetValueOrDefault(body) ?? new List<PartDamageAppliedEvent>();
                        results[body] = (WoundSeverity(torso, "PiercingWound") - woundBefore,
                            bleeding.GetPartRate(torso) - bleedBefore,
                            hits.Sum(h => h.Total.DamageDict.GetValueOrDefault("Piercing").Float()),
                            hits.Sum(h => h.Damage.DamageDict.GetValueOrDefault("Piercing").Float()),
                            hits.Sum(h => h.Overflow?.DamageDict.GetValueOrDefault("Piercing").Float() ?? 0f),
                            returned?.DamageDict.GetValueOrDefault("Piercing").Float() ?? 0f,
                            hits.Count);
                    }

                    var (satR, freshR) = (results[sat], results[fresh]);
                    Assert.Multiple(() =>
                    {
                        Assert.That(satR.Events, Is.EqualTo(1), $"shot {shot}: not exactly one event on the saturated torso.");
                        Assert.That(freshR.Events, Is.EqualTo(1), $"shot {shot}: not exactly one event on the fresh torso.");
                        Assert.That(satR.Growth, Is.EqualTo(freshR.Growth).Within(0.01f), $"shot {shot}: wound growth differs.");
                        Assert.That(satR.Bleed, Is.EqualTo(freshR.Bleed).Within(0.0001f), $"shot {shot}: bleed change differs.");
                        Assert.That(satR.Total, Is.EqualTo(20f).Within(0.01f), $"shot {shot}: the saturated Total is not the stab.");
                        Assert.That(freshR.Total, Is.EqualTo(20f).Within(0.01f), $"shot {shot}: the fresh Total is not the stab.");
                        Assert.That(satR.Applied, Is.EqualTo(0f), $"shot {shot}: the saturated torso stored some of the stab.");
                        Assert.That(satR.Overflow, Is.EqualTo(20f).Within(0.01f), $"shot {shot}: Overflow is not the whole stab.");
                        Assert.That(freshR.Overflow, Is.EqualTo(0f), $"shot {shot}: the fresh torso overflowed.");
                        Assert.That(satR.Returned, Is.EqualTo(0f), $"shot {shot}: the accumulator counted overflow.");
                        Assert.That(freshR.Returned, Is.EqualTo(20f).Within(0.01f), $"shot {shot}: the accumulator lost Applied.");
                    });
                }

                Assert.Multiple(() =>
                {
                    Assert.That(Stored(satTorso), Is.EqualTo(250f).Within(0.01f), "the saturated torso's store moved.");
                    Assert.That(SEntMan.GetComponent<DamageableComponent>(sat).TotalDamage, Is.EqualTo(satBody),
                        "the saturated body's total drifted with overflow.");
                    Assert.That(SEntMan.System<WoundFractureSystem>().GetFracture(satTorso), Is.Null, "overflow fractured the torso.");
                    Assert.That(SEntMan.System<SharedBodySystem>().BodyHasChild(sat, satTorso), Is.True);
                    Assert.That(SEntMan.GetComponent<PainComponent>(satTorso).Value, Is.EqualTo(satPainBefore),
                        "overflow added pain past the clamp.");
                });
            });

        }
        finally
        {
            Hits.Observer = null;
        }
    }

    /// <summary>
    /// The ceilings (P31, plan §6.1). The admin part command passes them and can destroy a limb; an arm in the fire
    /// stores its ceiling (168 since M3 raised the Blunt and Heat rungs; 0.8 of the Slash rung 210) while its burn
    /// keeps growing; a corpse stops at the corpse ceiling. A tick that crosses the
    /// ceiling raises one event whose Applied + Overflow is the tick, and grows the burn and its fluid loss exactly
    /// as the same tick with no ceiling does; the accumulator counts Applied only.
    /// </summary>
    [Test]
    public async Task AmbientCeilingTest()
    {
        await Pin();
        var map = await Pair.CreateTestMap();
        var s = new WolfmedScenario(SEntMan);
        EntityUid admin = default;

        await Server.WaitPost(() =>
        {
            s.SetAir(map.MapUid, true);
            s.KeepGrid(map.Grid);
            admin = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            SEntMan.EnsureComponent<PainNumbnessComponent>(admin);
        });
        await RunSeconds(1);

        // --- The admin part command: past 600 in all, and a limb destroyed. ---
        var net = SEntMan.GetNetEntity(admin);
        await Pair.WaitCommand($"damage Heat 240 true {net} LeftLeg");
        await Pair.WaitCommand($"damage Heat 240 true {net} RightLeg");
        await Pair.WaitCommand($"damage Piercing 200 true {net} Torso");
        await Server.WaitAssertion(() =>
        {
            Assert.That(PartHeat(s.Part(admin, BodyPartType.Leg, BodyPartSymmetry.Left)), Is.EqualTo(240f).Within(0.01f),
                "the admin command was held at the ceiling.");
            Assert.That(SEntMan.GetComponent<DamageableComponent>(admin).TotalDamage, Is.GreaterThan(FixedPoint2.New(600)),
                "the admin command could not pass 600.");
        });

        EntityUid rightArm = default;
        await Server.WaitPost(() => rightArm = s.Part(admin, BodyPartType.Arm, BodyPartSymmetry.Right));
        await Pair.WaitCommand($"damage Blunt 410 true {net} RightArm"); // M3 (P19): the Blunt rung is 400
        await RunSeconds(1);
        await Server.WaitAssertion(() =>
            Assert.That(SEntMan.System<SharedBodySystem>().BodyHasChild(admin, rightArm), Is.False,
                "the admin command could not destroy a limb."));

        // --- An arm in the fire: stores its ceiling (168), the burn keeps growing. ---
        EntityUid burning = default, burningArm = default;
        await Server.WaitAssertion(() =>
        {
            burning = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            SEntMan.EnsureComponent<PainNumbnessComponent>(burning);
            burningArm = s.Part(burning, BodyPartType.Arm, BodyPartSymmetry.Left);
            var burnAtCeiling = 0f;
            for (var i = 0; i < 45; i++)
            {
                Routing.TryApplyPartDamage(burning, burningArm, Spec("Heat", 5), null, ignoreResistances: true);
                if (i == 34)
                    burnAtCeiling = BurnSeverity(burningArm);
            }

            Assert.Multiple(() =>
            {
                Assert.That(Stored(burningArm), Is.EqualTo(168f).Within(0.5f), "the arm is not held at 0.8 x 210.");
                Assert.That(BurnSeverity(burningArm), Is.GreaterThan(burnAtCeiling), "the burn stopped growing at the ceiling.");
            });
        });

        // --- A corpse stops at the corpse ceiling. ---
        await Server.WaitAssertion(() =>
        {
            var corpse = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            SEntMan.System<MobStateSystem>().ChangeMobState(corpse, MobState.Dead);
            Assert.That(SEntMan.System<MobStateSystem>().IsDead(corpse), Is.True);
            for (var i = 0; i < 60; i++)
            {
                foreach (var part in Parts(corpse))
                    Routing.TryApplyPartDamage(corpse, part, Spec("Cold", 5), null, ignoreResistances: true);
            }

            Assert.That(SEntMan.GetComponent<DamageableComponent>(corpse).TotalDamage, Is.LessThanOrEqualTo(FixedPoint2.New(601)),
                "a corpse went past the corpse ceiling.");
        });

        // --- Same tick, same consequences, across the ceiling or not. ---
        var events = new Dictionary<EntityUid, List<PartDamageAppliedEvent>>();
        Hits.Observer = (_, hit) =>
        {
            if (!events.TryGetValue(hit.Body, out var list))
                events[hit.Body] = list = new List<PartDamageAppliedEvent>();
            list.Add(hit);
        };

        try
        {
            await Server.WaitAssertion(() =>
            {
                var capped = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
                var open = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
                var cappedArm = s.Part(capped, BodyPartType.Arm, BodyPartSymmetry.Left);
                var openArm = s.Part(open, BodyPartType.Arm, BodyPartSymmetry.Left);
                var parts = SEntMan.System<WolfmedBodyPartSystem>();

                // Both arms at 166 Heat and a 166 burn, 2 under the ceiling; the open one got there with it off.
                Routing.TryApplyPartDamage(capped, cappedArm, Spec("Heat", 166), null, ignoreResistances: true);
                parts.WithCeilingBypass(open, () =>
                    Routing.TryApplyPartDamage(open, openArm, Spec("Heat", 166), null, ignoreResistances: true));
                Assert.That(BurnSeverity(cappedArm), Is.EqualTo(BurnSeverity(openArm)).Within(0.01f));

                events.Clear();
                var cappedBurn = BurnSeverity(cappedArm);
                var cappedReturn = SEntMan.System<DamageableSystem>().TryChangeDamage(capped, Spec("Heat", 10),
                    ignoreResistances: true, targetPart: Content.Shared._Shitmed.Targeting.TargetBodyPart.LeftArm);
                var openBurn = BurnSeverity(openArm);
                parts.WithCeilingBypass(open, () =>
                    SEntMan.System<DamageableSystem>().TryChangeDamage(open, Spec("Heat", 10),
                        ignoreResistances: true, targetPart: Content.Shared._Shitmed.Targeting.TargetBodyPart.LeftArm));

                Assert.Multiple(() =>
                {
                    Assert.That(events[capped], Has.Count.EqualTo(1), "a tick across the ceiling raised more than one event.");
                    Assert.That(events[open], Has.Count.EqualTo(1));
                    Assert.That(events[capped][0].Total.DamageDict["Heat"].Float(), Is.EqualTo(10f).Within(0.01f),
                        "Applied + Overflow is not the tick.");
                    Assert.That(events[capped][0].Damage.DamageDict.GetValueOrDefault("Heat").Float(), Is.EqualTo(2f).Within(0.01f),
                        "Applied is not the room under the ceiling.");
                    Assert.That(events[capped][0].Overflow?.DamageDict.GetValueOrDefault("Heat").Float() ?? 0f,
                        Is.EqualTo(8f).Within(0.01f), "Overflow is not what the ceiling cut.");
                    Assert.That(Stored(cappedArm), Is.EqualTo(168f).Within(0.01f), "Applied was not the room under the ceiling.");
                    Assert.That(cappedReturn?.DamageDict.GetValueOrDefault("Heat").Float() ?? 0f, Is.EqualTo(2f).Within(0.01f),
                        "the accumulator counted overflow.");
                    Assert.That(BurnSeverity(cappedArm) - cappedBurn, Is.EqualTo(BurnSeverity(openArm) - openBurn).Within(0.01f),
                        "the burn grew differently across the ceiling.");
                    Assert.That(PartFluid(cappedArm), Is.EqualTo(PartFluid(openArm)).Within(0.0001f),
                        "the fluid loss differs across the ceiling.");
                });
            });
        }
        finally
        {
            Hits.Observer = null;
        }
    }

    /// <summary>
    /// P20: a dressed burn infects at <c>dressedMultiplier</c> (0.15) of the rate of the same burn undressed.
    /// </summary>
    [Test]
    public async Task BurnDressingInfectionTest()
    {
        await Pin();
        await OverrideCVar(Side.Server, WolfmedCVars.InfectionEnabled, true);
        await OverrideCVar(Side.Server, WolfmedCVars.InfectionRate, 1f);
        var map = await Pair.CreateTestMap();
        var s = new WolfmedScenario(SEntMan);

        await Server.WaitAssertion(() =>
        {
            var attacker = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            var burns = new List<EntityUid>();
            foreach (var dressed in new[] { false, true })
            {
                var body = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
                var torso = s.Part(body, BodyPartType.Torso);
                Routing.TryApplyPartDamage(body, torso, Spec("Heat", 40), attacker, ignoreResistances: true);
                var burn = SEntMan.System<WoundSystem>().GetWounds(torso).First(w => w.Comp.Prototype.Id == "BurnWound");
                var infection = SEntMan.EnsureComponent<WolfmedInfectionComponent>(burn);
                infection.Progress = 0f;
                infection.Contamination = 1f;
                if (dressed)
                    Assert.That(Fluid.Dress(torso), Is.True, "the burn could not be dressed.");
                burns.Add(burn);
            }

            // Three minutes in one step: short of the local stage, so nothing reopens either burn.
            SEntMan.System<WolfmedInfectionSystem>().Update(180f);

            var open = SEntMan.GetComponent<WolfmedInfectionComponent>(burns[0]).Progress;
            var covered = SEntMan.GetComponent<WolfmedInfectionComponent>(burns[1]).Progress;
            TestContext.Out.WriteLine($"BurnDressingInfection: undressed {open:0.00}, dressed {covered:0.00}.");
            Assert.Multiple(() =>
            {
                Assert.That(open, Is.GreaterThan(0f), "the undressed burn did not infect.");
                Assert.That(covered / open, Is.InRange(0.15f * (1 - Band), 0.15f * (1 + Band)),
                    "a dressed burn does not infect at 0.15 of the undressed rate.");
            });
        });
    }

    /// <summary>
    /// OD12: a hand whose charring sits at its maximum while the fire keeps coming crumbles after
    /// <c>wolfmed.char_crumble_seconds</c>; an arm takes twice as long; the head and torso under the same fire
    /// never do.
    /// </summary>
    [Test]
    public async Task CharCrumbleTest()
    {
        const float crumble = 20f;
        await Pin();
        await OverrideCVar(Side.Server, WolfmedCVars.CharCrumbleSeconds, crumble);
        await OverrideCVar(Side.Server, WolfmedCVars.CharCrumbleLimbMultiplier, 2f);
        await OverrideCVar(Side.Server, WolfmedCVars.CharCrumbleGapSeconds, 10f);
        var map = await Pair.CreateTestMap();
        var s = new WolfmedScenario(SEntMan);
        EntityUid body = default, hand = default, arm = default, head = default, torso = default;

        await Server.WaitPost(() =>
        {
            s.SetAir(map.MapUid, true);
            s.KeepGrid(map.Grid);
            body = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            SEntMan.EnsureComponent<PainNumbnessComponent>(body);
            hand = s.Part(body, BodyPartType.Hand, BodyPartSymmetry.Left);
            arm = s.Part(body, BodyPartType.Arm, BodyPartSymmetry.Right);
            head = s.Part(body, BodyPartType.Head);
            torso = s.Part(body, BodyPartType.Torso);
        });

        var charred = new Dictionary<EntityUid, float>();
        var gone = new Dictionary<EntityUid, float>();
        var hits = SEntMan.System<WolfmedPartHitSystem>();
        for (var t = 0f; t < 120f; t += 1f)
        {
            var now = t;
            await Server.WaitPost(() =>
            {
                foreach (var part in new[] { hand, arm, head, torso })
                {
                    if (gone.ContainsKey(part))
                        continue;

                    if (SEntMan.Deleted(part) || !SEntMan.System<SharedBodySystem>().BodyHasChild(body, part))
                    {
                        gone[part] = now;
                        continue;
                    }

                    if (!charred.ContainsKey(part) && hits.IsCharredThrough(part))
                        charred[part] = now;

                    Routing.TryApplyPartDamage(body, part, Spec("Heat", 10), null, ignoreResistances: true);
                }

                // The fluid loss would otherwise empty this body before the arm is done.
                s.SetBlood(body, 1f);
            });
            await RunSeconds(1);
        }

        TestContext.Out.WriteLine("CharCrumble: charred through at " +
                                  string.Join(", ", charred.Select(c => $"{SEntMan.ToPrettyString(c.Key)} {c.Value:0}s")) +
                                  "; gone at " + string.Join(", ", gone.Select(g => $"{g.Value:0}s")));

        await Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(charred.ContainsKey(hand) && gone.ContainsKey(hand), Is.True, "the charred hand never crumbled.");
                Assert.That(charred.ContainsKey(arm) && gone.ContainsKey(arm), Is.True, "the charred arm never crumbled.");
                Assert.That(charred.ContainsKey(head), Is.True, "the head never charred through, so the test proves nothing.");
                Assert.That(charred.ContainsKey(torso), Is.True, "the torso never charred through, so the test proves nothing.");
                Assert.That(gone.ContainsKey(head), Is.False, "the head crumbled.");
                Assert.That(gone.ContainsKey(torso), Is.False, "the torso crumbled.");
                Assert.That(SEntMan.System<SharedBodySystem>().BodyHasChild(body, head), Is.True);
                Assert.That(PartHeat(head), Is.LessThanOrEqualTo(400.5f), "the head stored past its ceiling.");
            });

            var handTime = gone[hand] - charred[hand];
            var armTime = gone[arm] - charred[arm];
            Assert.Multiple(() =>
            {
                Assert.That(handTime, Is.InRange(crumble * (1 - Band), crumble * (1 + Band) + 2f),
                    "the hand did not crumble after the time band.");
                Assert.That(armTime, Is.InRange(2 * crumble * (1 - Band), 2 * crumble * (1 + Band) + 2f),
                    "the arm did not take twice as long.");
            });
        });
    }

    /// <summary>
    /// Plan §3.7 "to confirm": a Downed body on fire can still resist to pat the fire out.
    /// </summary>
    [Test]
    public async Task DownedCanPatOutFireTest()
    {
        await Pin();
        var map = await Pair.CreateTestMap();
        var s = new WolfmedScenario(SEntMan);
        EntityUid a = default;

        await Server.WaitPost(() =>
        {
            s.SetAir(map.MapUid, true);
            s.KeepGrid(map.Grid);
            a = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
        });
        await RunSeconds(1);
        await Server.WaitPost(() => SEntMan.System<FlammableSystem>().SetFireStacks(a, 10, ignite: true));

        var downed = false;
        for (var i = 0; i < 150 && !downed; i++)
        {
            await RunSeconds(0.2f);
            await Server.WaitPost(() => downed = s.State(a) == WolfmedConsciousness.Downed);
        }

        Assert.That(downed, Is.True, "the fire never put the patient down, so the test proves nothing.");
        // Still burning, but slowly: at ten stacks the pain climbs from the Downed line to a faint within the
        // wait below. Past the fall's own short stun, which cancels every action.
        await Server.WaitPost(() => SEntMan.System<FlammableSystem>().SetFireStacks(a, 1, ignite: true));
        await RunSeconds(3);
        await Server.WaitAssertion(() =>
        {
            Assert.That(s.State(a), Is.EqualTo(WolfmedConsciousness.Downed));
            SEntMan.System<FlammableSystem>().Resist(a);
            Assert.That(SEntMan.GetComponent<FlammableComponent>(a).Resisting, Is.True,
                "a Downed body on fire cannot pat it out.");
        });
    }
}
