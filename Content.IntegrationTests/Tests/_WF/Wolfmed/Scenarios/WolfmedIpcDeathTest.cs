#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Temperature.Components;
using Content.Server._WF.Wolfmed.Consciousness;
using Content.Server._WF.Wolfmed.Hud;
using Content.Server._WF.Wolfmed.Life;
using Content.Shared._WF.Wolfmed.CCVar;
using Content.Shared._WF.Wolfmed.Consciousness;
using Content.Shared._WF.Wolfmed.Hud;
using Content.Shared._WF.Wolfmed.Life;
using Content.Shared.Atmos.Components;
using Content.Shared.FixedPoint;
using Content.Shared.Mind;
using Content.Shared.Mobs.Systems;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;

namespace Content.IntegrationTests.Tests._WF.Wolfmed.Scenarios;

/// <summary>
/// M4 (plan §3.11, OD3 (b), OD10): the IPC core-heat route. The core soaks up the chassis's heat and the pump takes it
/// off; past the core-heat line the chassis is in thermal shutdown, Dying with Succumb and Last Words, and the core
/// loses health; cooled under the wake line it comes back Downed. An untreated 10-stack fire is core failure, one put
/// out at a minute is not.
/// </summary>
/// <remarks>Times are asserted as order plus a ±20% band, with every CVar the route reads pinned.</remarks>
[TestFixture]
[TestOf(typeof(WolfmedOverheatSystem))]
public sealed class WolfmedIpcDeathTest : GameTest
{
    private const float Band = 0.2f;

    /// <summary>Measured with the shipped values (M4): thermal shutdown about 40 s into a 10-stack fire.</summary>
    private const float ShutdownAt = 40f;

    /// <summary>Measured with the shipped values (M4): untreated, core failure about 100 s after ignition.</summary>
    private const float CoreFailureAt = 100f;

    private async Task Pin()
    {
        await OverrideCVar(Side.Server, WolfmedCVars.Consciousness, true);
        await OverrideCVar(Side.Server, WolfmedCVars.ConsciousnessPainDown, 0.95f);
        await OverrideCVar(Side.Server, WolfmedCVars.ConsciousnessHysteresis, 0.1f);
        await OverrideCVar(Side.Server, WolfmedCVars.IpcCoreHeatK, 500f);
        await OverrideCVar(Side.Server, WolfmedCVars.IpcCoreHeatWakeK, 450f);
        await OverrideCVar(Side.Server, WolfmedCVars.IpcCoreHeatRate, 0.5333f); // playtest 3: the core is 40 health
        await OverrideCVar(Side.Server, WolfmedCVars.IpcCoreHeatSoak, 0.02f);
        await OverrideCVar(Side.Server, WolfmedCVars.IpcPumpCooling, 5f);
        await OverrideCVar(Side.Server, WolfmedCVars.IpcCoreHeatWarnK, 400f);
    }

    private WolfmedConsciousnessComponent Consc(EntityUid body) =>
        SEntMan.GetComponent<WolfmedConsciousnessComponent>(body);

    private bool HasDyingActions(EntityUid body) =>
        SEntMan.TryGetComponent(body, out WolfmedDyingActionsComponent? actions) &&
        actions.Succumb != null && actions.LastWords != null;

    /// <summary>
    /// <c>IpcFireScenarioTest</c> (plan §12 M4): three chassis in 10-stack fires, each on its own map. A burning IPC under
    /// the core line is Downed by pain, conscious, with no Succumb, and can pat itself out; past the line it goes into
    /// thermal shutdown (Critical, cause CoreHeat) with Succumb and Last Words; cooled under the wake line it is Downed
    /// again without them. Untreated, the core fails; put out at 60 s, it survives.
    /// </summary>
    [Test]
    public async Task IpcFireScenarioTest()
    {
        await Pin();
        var s = new WolfmedScenario(SEntMan);
        var bodies = new EntityUid[3];
        for (var i = 0; i < bodies.Length; i++)
        {
            var map = await Pair.CreateTestMap();
            var index = i;
            await Server.WaitPost(() =>
            {
                s.SetAir(map.MapUid, true);
                s.KeepGrid(map.Grid);
                bodies[index] = SEntMan.SpawnEntity("MobIPC", map.GridCoords);
            });
        }

        var (untreated, putOut, patter) = (bodies[0], bodies[1], bodies[2]);
        await RunSeconds(1);
        await Server.WaitPost(() =>
        {
            foreach (var body in bodies)
                SEntMan.System<FlammableSystem>().SetFireStacks(body, 10, ignite: true);
        });

        float? downedAt = null, shutdownAt = null, deadAt = null, outShutdownAt = null, outWokeAt = null, pattedAt = null;
        var warned = false;
        var patterPeak = 0f;
        var outCoreLeft = 0f;
        var trace = new List<string>();
        var start = 0f;
        await Server.WaitPost(() => start = (float) SGameTiming.CurTime.TotalSeconds);

        for (var tick = 0; tick <= 180; tick++)
        {
            await Server.WaitAssertion(() =>
            {
                var t = (float) SGameTiming.CurTime.TotalSeconds - start;
                var mobState = SEntMan.System<MobStateSystem>();

                if (tick == 60)
                    SEntMan.System<FlammableSystem>().Extinguish(putOut);

                // --- Untreated: Downed by pain, then thermal shutdown, then core failure. ---
                var heat = SEntMan.GetComponent<WolfmedCoreHeatComponent>(untreated);
                var comp = Consc(untreated);
                if (!mobState.IsDead(untreated))
                {
                    if (!heat.ThermalShutdown && comp.State == WolfmedConsciousness.Downed)
                    {
                        downedAt ??= t;
                        Assert.That(comp.Cause, Is.EqualTo(WolfmedCause.Pain), "a heating chassis was Downed by something else.");
                        Assert.That(HasDyingActions(untreated), Is.False, "a conscious, crawling IPC was offered Succumb.");
                        if (heat.Hot)
                        {
                            var hud = SEntMan.GetComponent<WolfmedSyntheticHudComponent>(untreated);
                            warned |= hud.Faults.Any(fault => fault.Line == "wolfmed-synthetic-line-core-temp-critical");
                        }
                    }

                    if (heat.ThermalShutdown && shutdownAt == null)
                    {
                        shutdownAt = t;
                        Assert.Multiple(() =>
                        {
                            Assert.That(comp.State, Is.EqualTo(WolfmedConsciousness.Unconscious));
                            Assert.That(comp.Cause, Is.EqualTo(WolfmedCause.CoreHeat));
                            Assert.That(mobState.IsCritical(untreated), Is.True, "thermal shutdown is not Critical.");
                            Assert.That(HasDyingActions(untreated), Is.True, "thermal shutdown offers no Succumb and Last Words.");
                            Assert.That(SEntMan.System<WolfmedDyingActionsSystem>().IsDying(untreated), Is.True);
                            Assert.That(s.AnalyzerLines(untreated)[0], Is.EqualTo("THERMAL SHUTDOWN: core overheating"));
                            // Playtest 3: the heat is a vitals item, the route's aid is on "Do first".
                            Assert.That(s.AnalyzerLines(untreated)[1], Does.Contain("Core ").And.Contain(" K, chassis "));
                            Assert.That(s.AnalyzerLines(untreated)[2], Does.StartWith("Do first: ")
                                .And.Contain(WolfmedVitalsText.Aid(WolfmedRoutes.CoreHeat, true)));
                            Assert.That(SEntMan.System<WolfmedConditionAlertSystem>().GetShownHealthAlert(untreated)?.Id,
                                Is.EqualTo("WFWolfmedOutCoreHeat"));
                        });
                    }
                }
                else
                {
                    deadAt ??= t;
                }

                // --- Put out at 60 s: thermal shutdown, then cooled back to Downed with the core standing. ---
                var outHeat = SEntMan.GetComponent<WolfmedCoreHeatComponent>(putOut);
                if (outHeat.ThermalShutdown)
                    outShutdownAt ??= t;
                else if (outShutdownAt != null && outWokeAt == null && !mobState.IsDead(putOut))
                {
                    outWokeAt = t;
                    Assert.Multiple(() =>
                    {
                        Assert.That(Consc(putOut).State, Is.Not.EqualTo(WolfmedConsciousness.Unconscious),
                            "a cooled chassis stayed out.");
                        Assert.That(HasDyingActions(putOut), Is.False, "a cooled chassis kept Succumb.");
                        Assert.That(outHeat.CoreTemperature, Is.LessThan(450f));
                    });
                }

                outCoreLeft = s.Life.GetBrainOrgan(putOut)?.Comp.Health.Float() ?? 0f;

                // --- The patter: Downed, past the fall's stun, pats itself out before its core is at risk. ---
                var patHeat = SEntMan.GetComponent<WolfmedCoreHeatComponent>(patter);
                patterPeak = MathF.Max(patterPeak, patHeat.CoreTemperature);
                if (pattedAt == null && Consc(patter).State == WolfmedConsciousness.Downed && t >= 12f)
                {
                    SEntMan.System<FlammableSystem>().Resist(patter);
                    Assert.That(SEntMan.GetComponent<FlammableComponent>(patter).Resisting, Is.True,
                        "a Downed IPC on fire cannot pat itself out.");
                    Assert.That(patHeat.CoreTemperature, Is.LessThan(500f));
                    pattedAt = t;
                }

                if (tick % 5 == 0)
                {
                    trace.Add($"{t:0}s untreated {SEntMan.GetComponent<TemperatureComponent>(untreated).CurrentTemperature:0}/" +
                              $"{heat.CoreTemperature:0}K core {s.Life.GetBrainOrgan(untreated)?.Comp.Health.Float() ?? 0f:0.0}; " +
                              $"out {SEntMan.GetComponent<TemperatureComponent>(putOut).CurrentTemperature:0}/{outHeat.CoreTemperature:0}K " +
                              $"core {outCoreLeft:0.0}; patter core {patHeat.CoreTemperature:0}K");
                }
            });
            await RunSeconds(1);
        }

        TestContext.Out.WriteLine($"IpcFireScenario: Downed {downedAt:0.0} s, thermal shutdown {shutdownAt:0.0} s, core failure " +
                                  $"{deadAt:0.0} s; put out at 60 s: shutdown {outShutdownAt:0.0} s, back {outWokeAt:0.0} s, " +
                                  $"core left {outCoreLeft:0.0}; patter: patted {pattedAt:0.0} s, core peak {patterPeak:0} K.");
        TestContext.Out.WriteLine("Trace: " + string.Join(" | ", trace));

        Assert.Multiple(() =>
        {
            Assert.That(downedAt, Is.Not.Null, "the fire never put the chassis down.");
            Assert.That(shutdownAt, Is.Not.Null, "an untreated 10-stack fire never reached thermal shutdown.");
            Assert.That(downedAt, Is.LessThan(shutdownAt!), "the chassis was not Downed and conscious first.");
            Assert.That(shutdownAt, Is.InRange(ShutdownAt * (1 - Band), ShutdownAt * (1 + Band)));
            Assert.That(warned, Is.True, "the readout never said CORE TEMP CRITICAL before thermal shutdown.");

            Assert.That(deadAt, Is.Not.Null, "an untreated 10-stack fire did not destroy the core.");
            Assert.That(deadAt, Is.InRange(CoreFailureAt * (1 - Band), CoreFailureAt * (1 + Band)));
            Assert.That(s.Life.GetBrainOrgan(untreated)?.Comp.Health, Is.EqualTo(FixedPoint2.Zero), "death was not core failure.");

            Assert.That(outShutdownAt, Is.Not.Null, "the chassis put out at 60 s never reached thermal shutdown.");
            Assert.That(outWokeAt, Is.Not.Null, "the chassis put out at 60 s never came back from thermal shutdown.");
            Assert.That(outCoreLeft, Is.GreaterThan(0f), "put out at 60 s, the core still failed.");
            Assert.That(SEntMan.System<MobStateSystem>().IsDead(putOut), Is.False);

            Assert.That(pattedAt, Is.Not.Null, "the patter was never Downed.");
            Assert.That(patterPeak, Is.LessThan(500f), "patting the fire out early did not keep the core safe.");
            Assert.That(SEntMan.GetComponent<WolfmedCoreHeatComponent>(patter).ThermalShutdown, Is.False);
        });
    }

    /// <summary>
    /// Playtest 5, "no ghost button when COOLANT PUMP OFFLINE: SHUTDOWN": a chassis shut down for want of power or a
    /// pump runs nothing out, but nobody may come. It gets Succumb and Last Words like an arrest, the dialog names the
    /// reason, a yes is core failure with the core left in place, and power back takes the actions away again.
    /// </summary>
    [Test]
    public async Task PowerShutdownSuccumbTest()
    {
        await Pin();
        var map = await Pair.CreateTestMap();
        var s = new WolfmedScenario(SEntMan);
        EntityUid ipc = default, restored = default;

        await Server.WaitPost(() =>
        {
            s.SetAir(map.MapUid, true);
            ipc = SEntMan.SpawnEntity("MobIPC", map.GridCoords);
            restored = SEntMan.SpawnEntity("MobIPC", map.GridCoords);
            var minds = SEntMan.System<SharedMindSystem>();
            minds.TransferTo(minds.CreateMind(null).Owner, ipc);
        });
        await RunSeconds(2);

        await Server.WaitAssertion(() =>
        {
            var shutdown = SEntMan.System<WolfmedShutdownSystem>();
            var dying = SEntMan.System<WolfmedDyingActionsSystem>();
            foreach (var chassis in new[] { ipc, restored })
            {
                shutdown.Refresh(chassis, powered: false);
                Assert.That(shutdown.IsShutDown(chassis), Is.True, "the chassis did not shut down without power.");
                Assert.That(dying.IsDying(chassis), Is.True, "a shut-down chassis is not Dying.");
                Assert.That(HasDyingActions(chassis), Is.True, "a shut-down chassis has no Succumb and Last Words.");
            }

            // Power back: the way out goes with it.
            shutdown.Refresh(restored, powered: true);
            Assert.That(shutdown.IsShutDown(restored), Is.False);
            Assert.That(HasDyingActions(restored), Is.False, "a chassis back on power kept Succumb.");

            dying.OpenSuccumbDialog(ipc);
            Assert.That(dying.GetPendingChoice(ipc), Is.EqualTo(WolfmedEndingChoice.Succumb));
            Assert.That(Loc.GetString("wolfmed-succumb-dialog-text-shutdown-no-decay", ("reason", "no power")),
                Does.Contain("no power").And.Contain("core failure"));
            Assert.That(dying.Confirm(ipc), Is.True);

            var core = s.Life.GetBrainOrgan(ipc);
            Assert.Multiple(() =>
            {
                Assert.That(SEntMan.System<MobStateSystem>().IsDead(ipc), Is.True, "Succumb did not kill the chassis.");
                Assert.That(core, Is.Not.Null, "the core left the chassis.");
                Assert.That(core!.Value.Comp.Health, Is.EqualTo(FixedPoint2.Zero), "Succumb left the core standing.");
                Assert.That(SEntMan.HasComponent<WolfmedDyingActionsComponent>(ipc), Is.False);
            });
        });
    }

    /// <summary>
    /// OD3 (b), plan §5.4: Succumb in thermal shutdown is core failure with the core left in place at 0, the shutdown
    /// ended on the corpse, and a returnable ghost; the dialog says core failure and core repair. The overheat pulse
    /// that burns the parts never reaches the core: heat reaches it only by the core-heat route.
    /// </summary>
    [Test]
    public async Task ThermalShutdownSuccumbTest()
    {
        await Pin();
        var map = await Pair.CreateTestMap();
        var s = new WolfmedScenario(SEntMan);
        EntityUid ipc = default, pulsed = default;

        await Server.WaitPost(() =>
        {
            s.SetAir(map.MapUid, true);
            ipc = SEntMan.SpawnEntity("MobIPC", map.GridCoords);
            pulsed = SEntMan.SpawnEntity("MobIPC", map.GridCoords);
            var minds = SEntMan.System<SharedMindSystem>();
            minds.TransferTo(minds.CreateMind(null).Owner, ipc);
        });
        await RunSeconds(2);

        await Server.WaitAssertion(() =>
        {
            var overheat = SEntMan.System<WolfmedOverheatSystem>();
            var dying = SEntMan.System<WolfmedDyingActionsSystem>();
            SEntMan.GetComponent<TemperatureComponent>(ipc).CurrentTemperature = 900f;
            overheat.SetCoreTemperature(ipc, 600f);
            overheat.Tick(ipc, 1f);
            Assert.That(overheat.InThermalShutdown(ipc), Is.True);
            Assert.That(dying.IsDying(ipc), Is.True);

            dying.OpenSuccumbDialog(ipc);
            Assert.That(dying.GetPendingChoice(ipc), Is.EqualTo(WolfmedEndingChoice.Succumb));
            Assert.That(Loc.GetString("wolfmed-succumb-dialog-text-core-no-decay"), Does.Contain("core failure"));
            Assert.That(dying.Confirm(ipc), Is.True);

            var core = s.Life.GetBrainOrgan(ipc);
            Assert.Multiple(() =>
            {
                Assert.That(SEntMan.System<MobStateSystem>().IsDead(ipc), Is.True, "Succumb did not kill the chassis.");
                Assert.That(core, Is.Not.Null, "the core left the chassis.");
                Assert.That(core!.Value.Comp.Health, Is.EqualTo(FixedPoint2.Zero), "Succumb left the core standing.");
                Assert.That(overheat.InThermalShutdown(ipc), Is.False);
                Assert.That(SEntMan.GetComponent<WolfmedCoreHeatComponent>(ipc).ThermalShutdown, Is.False,
                    "the corpse is still flagged in thermal shutdown.");
                Assert.That(SEntMan.HasComponent<WolfmedDyingActionsComponent>(ipc), Is.False);
            });

            // Forty overheat pulses, one a second: parts burn, the core does not.
            var before = s.Life.GetBrainOrgan(pulsed)!.Value.Comp.Health;
            for (var i = 0; i < 40; i++)
            {
                SEntMan.EnsureComponent<WolfmedOverheatComponent>(pulsed).NextPulse = TimeSpan.Zero;
                Assert.That(overheat.TryOverheat(pulsed, "ipc-overheat-popup"), Is.True);
            }

            Assert.That(s.Life.GetBrainOrgan(pulsed)!.Value.Comp.Health, Is.EqualTo(before),
                "the overheat pulse reached the core: a second, hidden heat route.");
        });
    }
}
