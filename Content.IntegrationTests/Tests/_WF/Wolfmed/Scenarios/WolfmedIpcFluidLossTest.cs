#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._WF.Wolfmed.Consciousness;
using Content.Shared._Onyx.Wounds;
using Content.Shared._Shitmed.Targeting;
using Content.Shared._WF.Wolfmed.CCVar;
using Content.Shared._WF.Wolfmed.Consciousness;
using Content.Shared._WF.Wolfmed.Life;
using Content.Shared.Body.Part;
using Content.Shared.Damage;
using Content.Shared.Mobs.Systems;
using NUnit.Framework;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.Wolfmed.Scenarios;

/// <summary>
/// Playtest 3, IPC round: an IPC's oil runs out through the same wound bleeding as blood. The owner took seven spear hits
/// (Piercing 15) to the torso in 8 s and was shut down by fluid loss 51 s after the last. A sealed hydraulic system
/// leaks, it does not spurt: from the same wounds an IPC now reaches its lines about twice as late as a human.
/// </summary>
/// <remarks>
/// Both bodies bleed through the bloodstream, which takes at most 10 u a 3 s tick and puts 1 u a tick back. Before, the
/// IPC's wounds asked for 12.6 u a tick and the human's 11, so both bled at the cap; the IPC's smaller pool (250 u
/// against 300) made it slightly the faster. <c>IpcBodyPartProfile</c>'s <c>bleedingMultiplier</c> 1 → 0.4 brings the
/// chassis under the cap. Times are asserted as order plus a ±20% band, with every CVar the route reads pinned.
/// </remarks>
[TestFixture]
[TestOf(typeof(WolfmedConsciousnessSystem))]
public sealed class WolfmedIpcFluidLossTest : GameTest
{
    private const float Band = 0.2f;
    private const int Hits = 7;
    private const float HitSpan = 8f;
    private const float HitDamage = 15f;
    private const int Limit = 400;

    private const float DownLine = 0.5f;
    private const float OutLine = 0.35f;

    // Measured with the shipped values, seconds from the first hit. Before the change the IPC's were 46.4 and 57.8.
    private const float HumanDownedAt = 49.5f;
    private const float HumanOutAt = 61.9f;
    private const float IpcDownedAt = 97.1f;
    private const float IpcOutAt = 127f;

    private async Task Pin()
    {
        await OverrideCVar(Side.Server, WolfmedCVars.Consciousness, true);
        await OverrideCVar(Side.Server, WolfmedCVars.ConsciousnessBloodDown, DownLine);
        await OverrideCVar(Side.Server, WolfmedCVars.ConsciousnessBloodOut, OutLine);
        await OverrideCVar(Side.Server, WolfmedCVars.ConsciousnessHysteresis, 0.1f);
        await OverrideCVar(Side.Server, WolfmedCVars.ArrestBlood, 0.30f);
        await OverrideCVar(Side.Server, WolfmedCVars.BleedRate, 0.3f);
    }

    /// <summary>
    /// <c>IpcFluidLossTest</c>: an IPC and a human each take seven Piercing 15 hits to the torso in 8 s, then bleed
    /// untreated. The IPC passes its fluid Downed line after the human passes the blood one and shuts down after the
    /// human is Unconscious, by a clear margin; the analyzer reads the same fraction; a shut-down chassis refilled above
    /// the Downed line comes back.
    /// </summary>
    [Test]
    public async Task IpcFluidLossTest()
    {
        await Pin();
        var s = new WolfmedScenario(SEntMan);
        var protos = new[] { "MobIPC", "MobHuman" };
        var bodies = new EntityUid[protos.Length];
        for (var i = 0; i < bodies.Length; i++)
        {
            var map = await Pair.CreateTestMap();
            var index = i;
            await Server.WaitPost(() =>
            {
                s.SetAir(map.MapUid, true);
                s.KeepGrid(map.Grid);
                bodies[index] = SEntMan.SpawnEntity(protos[index], map.GridCoords);
            });
        }

        var (ipc, human) = (bodies[0], bodies[1]);
        await RunSeconds(2);

        var start = 0f;
        await Server.WaitPost(() => start = (float) SGameTiming.CurTime.TotalSeconds);
        for (var hit = 0; hit < Hits; hit++)
        {
            await Server.WaitPost(() =>
            {
                var damage = SEntMan.System<DamageableSystem>();
                foreach (var body in bodies)
                {
                    damage.TryChangeDamage(body, WolfmedScenario.Spec("Piercing", HitDamage), origin: null,
                        targetPart: TargetBodyPart.Torso);
                }
            });

            if (hit < Hits - 1)
                await RunSeconds(HitSpan / (Hits - 1));
        }

        var trace = new List<string>();
        await Server.WaitPost(() =>
        {
            foreach (var body in bodies)
            {
                var torso = s.Part(body, BodyPartType.Torso);
                var wounds = SEntMan.System<WoundSystem>().GetWounds(torso)
                    .Select(w => $"{w.Comp.Prototype} {w.Comp.Severity} " +
                                 $"({(SEntMan.TryGetComponent(w.Owner, out WoundBleedingComponent? b) ? b.CurrentRate : 0f):F2} u/tick)");
                trace.Add($"{protos[Array.IndexOf(bodies, body)]}: pool {s.Pool(body)} u; torso {string.Join(", ", wounds)}; " +
                          $"bleed {s.Life.GetBleedRate(body):F2} u/s");
            }
        });

        var downAt = new float?[bodies.Length];
        var outAt = new float?[bodies.Length];
        var outCause = new WolfmedCause?[bodies.Length];
        float ipcAnalyzerBlood = -1f, ipcBloodAtOut = -1f;
        var ipcDoFirst = string.Empty;
        var ipcCritical = false;
        var done = false;
        for (var second = 0; second < Limit && !done; second++)
        {
            await RunSeconds(1);
            await Server.WaitPost(() =>
            {
                var t = (float) SGameTiming.CurTime.TotalSeconds - start;
                for (var i = 0; i < bodies.Length; i++)
                {
                    var body = bodies[i];
                    var blood = s.Blood(body);
                    if (downAt[i] == null && blood <= DownLine)
                        downAt[i] = t;

                    // Out: the out line passed and the body Unconscious for it.
                    if (outAt[i] == null && blood <= OutLine && s.State(body) == WolfmedConsciousness.Unconscious)
                    {
                        outAt[i] = t;
                        outCause[i] = s.Vitals(body).Cause;
                        if (body == ipc)
                        {
                            ipcBloodAtOut = blood;
                            ipcCritical = SEntMan.System<MobStateSystem>().IsCritical(ipc);
                            ipcAnalyzerBlood = s.Report(ipc).Blood;
                            ipcDoFirst = s.AnalyzerLines(ipc).FirstOrDefault(line => line.StartsWith("Do first")) ?? string.Empty;
                        }
                    }
                }

                if (second % 10 == 0)
                {
                    trace.Add($"t {t:F0} s: " + string.Join("; ", bodies.Select(body =>
                        $"{protos[Array.IndexOf(bodies, body)]} {s.Blood(body):P0} {s.State(body)}/{s.Vitals(body).Cause}" +
                        $"{(s.Life.InArrest(body) ? " arrest" : "")}")));
                }

                done = outAt.All(x => x != null);
            });
        }

        for (var i = 0; i < bodies.Length; i++)
        {
            trace.Add($"{protos[i]}: {DownLine:P0} line at {downAt[i]:F1} s, {OutLine:P0} and Unconscious at {outAt[i]:F1} s " +
                      $"({outCause[i]})");
        }

        foreach (var line in trace)
            TestContext.Out.WriteLine(line);

        Assert.Multiple(() =>
        {
            Assert.That(downAt[0], Is.Not.Null, "the IPC never reached its Downed line.");
            Assert.That(outAt[0], Is.Not.Null, "the IPC never shut down.");
            Assert.That(downAt[1], Is.Not.Null, "the human never reached the Downed line.");
            Assert.That(outAt[1], Is.Not.Null, "the human never went Unconscious.");
        });

        Assert.Multiple(() =>
        {
            // The order, by a clear margin.
            Assert.That(downAt[0], Is.GreaterThan(downAt[1]! * 1.5f), "the IPC reached its Downed line too soon after the human.");
            Assert.That(outAt[0], Is.GreaterThan(outAt[1]! * 1.5f), "the IPC shut down too soon after the human went out.");

            Assert.That(downAt[1], Is.InRange(HumanDownedAt * (1f - Band), HumanDownedAt * (1f + Band)));
            Assert.That(outAt[1], Is.InRange(HumanOutAt * (1f - Band), HumanOutAt * (1f + Band)));
            Assert.That(downAt[0], Is.InRange(IpcDownedAt * (1f - Band), IpcDownedAt * (1f + Band)));
            Assert.That(outAt[0], Is.InRange(IpcOutAt * (1f - Band), IpcOutAt * (1f + Band)));

            // The IPC's shutdown is the fluid's; the analyzer reads the same fraction and asks for hydraulic fluid
            // (playtest 3 IPC 2: it asked for oil, which is now a foreign reagent the pod and the pack refuse).
            Assert.That(outCause[0], Is.EqualTo(WolfmedCause.Oil));
            Assert.That(outCause[1], Is.EqualTo(WolfmedCause.Blood));
            Assert.That(ipcCritical, Is.True, "a shut-down chassis is not Critical.");
            Assert.That(ipcAnalyzerBlood, Is.EqualTo(ipcBloodAtOut).Within(0.01f), "the analyzer's fluid is not the body's.");
            Assert.That(ipcDoFirst, Does.Contain("refill hydraulic fluid"), "the analyzer does not ask for hydraulic fluid.");
        });

        // Refilled above the Downed line, the chassis comes back: no longer shut down, and not for the oil.
        await Server.WaitPost(() => s.Transfuse(ipc, (0.6f - s.Blood(ipc)) * s.Pool(ipc)));
        await RunSeconds(4);
        await Server.WaitAssertion(() =>
        {
            TestContext.Out.WriteLine($"refilled: {s.Blood(ipc):P0} {s.State(ipc)}/{s.Vitals(ipc).Cause}");
            Assert.Multiple(() =>
            {
                Assert.That(s.Blood(ipc), Is.GreaterThan(DownLine), "the refill did not take the chassis above its Downed line.");
                Assert.That(s.State(ipc), Is.Not.EqualTo(WolfmedConsciousness.Unconscious), "a refilled chassis stayed shut down.");
                Assert.That(s.Vitals(ipc).Cause, Is.Not.EqualTo(WolfmedCause.Oil), "a refilled chassis is still held by its oil.");
                Assert.That(SEntMan.System<MobStateSystem>().IsCritical(ipc), Is.False, "a refilled chassis is still Critical.");
            });
        });
    }
}
