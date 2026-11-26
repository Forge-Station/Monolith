#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Temperature.Components;
using Content.Server.Temperature.Systems;
using Content.Server._WF.Wolfmed.Life;
using Content.Shared._WF.Wolfmed.CCVar;
using Content.Shared._WF.Wolfmed.Consciousness;
using Content.Shared._WF.Wolfmed.Life;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using NUnit.Framework;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.Wolfmed.Scenarios;

/// <summary>
/// M5 (plan §3.10): cold and heat. The measurement comes first: where an unprotected human's surface settles in cold
/// air and in space, and how fast it cools after a fire. Then the hypothermia and heat stroke courses on the core
/// temperature, the fire grace, and a real-time smoke test in space.
/// </summary>
[TestFixture]
[TestOf(typeof(WolfmedBodyTemperatureSystem))]
public sealed class WolfmedTemperatureTest : GameTest
{
    private const float Band = 0.2f;

    /// <summary>The surface a naked human settles at in the maps' 235 K freezers (measured: 233 K air holds 254 K).</summary>
    private const float FreezerSurface = 256f;

    /// <summary>The shipped fire grace (wolfmed.heat_fire_grace_seconds), pinned.</summary>
    private const int GraceSeconds = 60;

    /// <summary>
    /// What the test measured, written out on the test's own thread when it ends: a line written from a server
    /// callback can land in another test's output.
    /// </summary>
    private readonly List<string> _notes = new();

    private void Note(string line) => _notes.Add(line);

    [TearDown]
    public void WriteNotes()
    {
        foreach (var line in _notes)
            TestContext.Out.WriteLine(line);
    }

    private async Task Pin()
    {
        await OverrideCVar(Side.Server, WolfmedCVars.Consciousness, true);
        await OverrideCVar(Side.Server, WolfmedCVars.ConsciousnessHysteresis, 0.1f);
        await OverrideCVar(Side.Server, WolfmedCVars.HypothermiaDownOffset, 30f);
        await OverrideCVar(Side.Server, WolfmedCVars.HypothermiaOutOffset, 15f);
        await OverrideCVar(Side.Server, WolfmedCVars.HypothermiaArrestOffset, 2f);
        await OverrideCVar(Side.Server, WolfmedCVars.HyperthermiaDownOffset, 7f);
        await OverrideCVar(Side.Server, WolfmedCVars.HyperthermiaBrainSeconds, 300f);
        await OverrideCVar(Side.Server, WolfmedCVars.HeatFireGraceSeconds, (float) GraceSeconds);
        await OverrideCVar(Side.Server, WolfmedCVars.CoreCoolingSeconds, 900f);
        // Playtest 3: the core chases hot air over time. The scenarios below hold a surface and expect the core there
        // at once, so they run with no lag; HeatStrokeIsTimedTest measures the shipped lag on its own.
        await OverrideCVar(Side.Server, WolfmedCVars.CoreHeatingSeconds, 0f);
        await OverrideCVar(Side.Server, WolfmedCVars.CoreRecoverySeconds, 0f);
        await OverrideCVar(Side.Server, WolfmedCVars.TemperatureLineMaxShare, 0.6f);
        await OverrideCVar(Side.Server, WolfmedCVars.TemperatureInputFloor, 0.5f);
        await OverrideCVar(Side.Server, WolfmedCVars.ArrestOxygenation, 0.15f);
        await OverrideCVar(Side.Server, WolfmedCVars.ArrestBlood, 0.30f);
        await OverrideCVar(Side.Server, WolfmedCVars.BrainArrestSeconds, 120f);
        await OverrideCVar(Side.Server, WolfmedCVars.BrainColdFactor, 1f);
        await OverrideCVar(Side.Server, WolfmedCVars.BrainRefillFactor, 0.5f);
        await OverrideCVar(Side.Server, WolfmedCVars.BrainPressureStart, 0.75f);
        await OverrideCVar(Side.Server, WolfmedCVars.BrainPressureOut, 0.45f);
        await OverrideCVar(Side.Server, WolfmedCVars.DefibBlood, 0.25f);
    }

    private WolfmedBodyTemperatureSystem BodyTemperature => SEntMan.System<WolfmedBodyTemperatureSystem>();

    private float Surface(EntityUid body) => SEntMan.GetComponent<TemperatureComponent>(body).CurrentTemperature;

    /// <summary>Station air's moles at a given temperature.</summary>
    private static GasMixture AirAt(float kelvin)
    {
        var air = WolfmedScenario.Air();
        air.Temperature = kelvin;
        return air;
    }

    /// <summary>
    /// Holds the surface at a temperature and runs the core, the life tick and consciousness one second at a time, as
    /// <see cref="WolfmedScenario.Advance"/> does for the life tick alone. Stops early when the callback says so.
    /// </summary>
    private int Hold(WolfmedScenario s, EntityUid body, float surface, int seconds, Func<int, bool>? stop = null)
    {
        var temperature = SEntMan.System<TemperatureSystem>();
        for (var second = 1; second <= seconds; second++)
        {
            temperature.ForceChangeTemperature(body, surface);
            BodyTemperature.Tick(body, 1f);
            s.Life.Tick(body, 1f);
            s.Consciousness.Refresh(body);
            if (stop != null && stop(second))
                return second;
        }

        return seconds;
    }

    private bool Holds(WolfmedScenario s, EntityUid body, WolfmedCause cause) =>
        s.Vitals(body).Cause == cause || (s.Vitals(body).Blockers & WolfmedCauses.Flag(cause)) != 0;

    private static void InBand(float actual, float derived, string what) =>
        Assert.That(actual, Is.InRange(derived * (1f - Band), derived * (1f + Band)),
            $"{what}: {actual} against {derived} derived.");

    /// <summary>
    /// The opening task (plan §12 M5): a naked human's surface and core temperature over five minutes in the default
    /// test map, in space, and in station air at 293, 273, 253, 233, 213 and 173 K; and a 10-stack fire in station air,
    /// how hot the surface gets and how long it takes to cool once the fire is out. Records only.
    /// </summary>
    [Test]
    public async Task ColdRoomMeasurementTest()
    {
        await Pin();
        var s = new WolfmedScenario(SEntMan);
        var atmos = SEntMan.System<AtmosphereSystem>();
        var rooms = new List<(string Name, EntityUid Body)>();
        EntityUid burning = default;

        var names = new[] { "default map", "space", "293 K", "273 K", "253 K", "233 K", "213 K", "173 K" };
        var airs = new float?[] { null, null, 293.15f, 273.15f, 253.15f, 233.15f, 213.15f, 173.15f };
        for (var i = 0; i < names.Length; i++)
        {
            var map = await Pair.CreateTestMap();
            var index = i;
            await Server.WaitPost(() =>
            {
                if (index == 1)
                    atmos.SetMapAtmosphere(map.MapUid, true, GasMixture.SpaceGas);
                else if (airs[index] is { } kelvin)
                    atmos.SetMapAtmosphere(map.MapUid, false, AirAt(kelvin));
                s.KeepGrid(map.Grid);
                rooms.Add((names[index], SEntMan.SpawnEntity("MobHuman", map.GridCoords)));
            });
        }

        // Two fires: one left to burn out, one put out at its hottest (30 s), the case the fire grace has to cover.
        var fireMap = await Pair.CreateTestMap();
        var doused = default(EntityUid);
        await Server.WaitPost(() =>
        {
            s.SetAir(fireMap.MapUid, true);
            s.KeepGrid(fireMap.Grid);
            burning = SEntMan.SpawnEntity("MobHuman", fireMap.GridCoords);
            doused = SEntMan.SpawnEntity("MobHuman", new Robust.Shared.Map.MapCoordinates(new System.Numerics.Vector2(40f, 40f), fireMap.MapId));
        });
        await RunSeconds(2);
        await Server.WaitPost(() =>
        {
            SEntMan.System<FlammableSystem>().SetFireStacks(burning, 10, ignite: true);
            SEntMan.System<FlammableSystem>().SetFireStacks(doused, 10, ignite: true);
        });

        var lines = new Dictionary<string, List<string>>();
        foreach (var (name, _) in rooms)
            lines[name] = new List<string>();
        var fire = new List<string>();
        var douse = new List<string>();
        var fireOut = -1;
        var peak = 0f;
        int? burnedUnder = null, dousedUnder = null;

        for (var second = 0; second <= 300; second += 5)
        {
            var now = second;
            await Server.WaitPost(() =>
            {
                if (now % 10 == 0)
                {
                    foreach (var (name, body) in rooms)
                    {
                        var core = BodyTemperature.GetCore(body) is { } k ? $"{k:0}" : "-";
                        var arrest = s.Life.InArrest(body) ? "/arrest" : "";
                        lines[name].Add($"{now}:{Surface(body):0}/{core}/{s.State(body)}{arrest}");
                    }
                }

                var flammable = SEntMan.GetComponent<FlammableComponent>(burning);
                if (!flammable.OnFire && fireOut < 0)
                    fireOut = now;
                if (fireOut >= 0 && burnedUnder == null && Surface(burning) < 318f)
                    burnedUnder = now - fireOut;
                peak = MathF.Max(peak, Surface(burning));
                fire.Add($"{now}:{Surface(burning):0}{(flammable.OnFire ? "*" : "")}/{s.State(burning)}");

                if (now == 30)
                    SEntMan.System<FlammableSystem>().Extinguish(doused);
                if (now >= 30 && dousedUnder == null && Surface(doused) < 318f)
                    dousedUnder = now - 30;
                if (now <= 120)
                    douse.Add($"{now}:{Surface(doused):0}/{s.State(doused)}");
            });
            await RunSeconds(5);
        }

        foreach (var (name, _) in rooms)
            Note($"ColdRoomMeasurement {name} (surface/core/state): {string.Join(" ", lines[name])}");
        Note($"ColdRoomMeasurement fire (out at {fireOut} s, peak {peak:0} K, under 318 K {burnedUnder} s later; * burning): " +
             string.Join(" ", fire));
        Note($"ColdRoomMeasurement fire put out at 30 s (under 318 K {dousedUnder} s later): {string.Join(" ", douse)}");
        Assert.That(rooms.Count, Is.EqualTo(names.Length));
    }

    /// <summary>
    /// <c>HypothermiaScenarioTest</c> (plan §3.10), with the lines from the M5 measurement: a human held at the 256 K
    /// surface of a 235 K freezer is Downed at the hypothermia line (290 K), Unconscious and breathing at 275 K, and
    /// the heart stops at 262 K with cause "cold", each at the time the core's cooling derives, ±20%. In the cold arrest
    /// the brain drains at a tenth of the arrest rate. The paddles refuse a core still under the arrest line and shock
    /// once it is rewarmed; rewarming wakes an Unconscious patient.
    /// </summary>
    [Test]
    public async Task HypothermiaScenarioTest()
    {
        await Pin();
        var map = await Pair.CreateTestMap();
        var s = new WolfmedScenario(SEntMan);
        EntityUid frozen = default, rewarmed = default;

        await Server.WaitPost(() =>
        {
            s.SetAir(map.MapUid, true);
            s.KeepGrid(map.Grid);
            frozen = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            rewarmed = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
        });
        await RunSeconds(3);

        await Server.WaitAssertion(() =>
        {
            var lines = BodyTemperature.GetLines(frozen)!.Value;
            Assert.Multiple(() =>
            {
                Assert.That(lines.ColdDown, Is.EqualTo(290f).Within(0.01f));
                Assert.That(lines.ColdOut, Is.EqualTo(275f).Within(0.01f));
                Assert.That(lines.ColdArrest, Is.EqualTo(262f).Within(0.01f));
                Assert.That(lines.HeatDown, Is.EqualTo(318f).Within(0.01f));
                Assert.That(lines.HeatOut, Is.EqualTo(325f).Within(0.01f));
            });

            var core0 = BodyTemperature.GetCore(frozen)!.Value;
            float Derived(float line) => 900f * MathF.Log((core0 - FreezerSurface) / (line - FreezerSurface));

            int? downed = null, unconscious = null;
            var arrest = Hold(s, frozen, FreezerSurface, 2600, second =>
            {
                if (downed == null && s.State(frozen) == WolfmedConsciousness.Downed)
                {
                    downed = second;
                    Assert.That(s.Vitals(frozen).Cause, Is.EqualTo(WolfmedCause.Cold), "the freezer downed the patient for something else.");
                    Assert.That(s.Analyzer(frozen), Does.Contain("DOWNED: hypothermia").And.Contain("hypothermic"));
                }

                if (unconscious == null && s.State(frozen) == WolfmedConsciousness.Unconscious)
                {
                    unconscious = second;
                    Assert.That(s.Vitals(frozen).Cause, Is.EqualTo(WolfmedCause.Cold));
                    Assert.That(s.Vitals(frozen).Breathing, Is.EqualTo(WolfmedBreathing.Normal), "hypothermia stopped the chest.");
                    Assert.That(s.Breathing.BreathingSuppressed(frozen), Is.False);
                    Assert.That(s.Analyzer(frozen), Does.Contain("UNCONSCIOUS: hypothermia"));
                }

                return s.Life.InArrest(frozen);
            });

            Note($"HypothermiaScenarioTest: 256 K surface from core {core0:0.0} K: Downed at {downed} s " +
                                      $"(derived {Derived(290f):0}), Unconscious at {unconscious} s (derived {Derived(275f):0}), " +
                                      $"arrest at {arrest} s (derived {Derived(262f):0}).");
            Assert.Multiple(() =>
            {
                Assert.That(downed, Is.Not.Null);
                Assert.That(unconscious, Is.GreaterThan(downed!));
                Assert.That(arrest, Is.GreaterThan(unconscious!));
                Assert.That(s.Life.InArrest(frozen), Is.True, "the cold never stopped the heart.");
                InBand(downed!.Value, Derived(290f), "hypothermic Downed");
                InBand(unconscious!.Value, Derived(275f), "hypothermic Unconscious");
                InBand(arrest, Derived(262f), "cold arrest");
            });

            Assert.That(SEntMan.GetComponent<WolfmedCardiacArrestComponent>(frozen).Cause, Is.EqualTo("cold"));
            var brain = s.Life.GetBrain(frozen)!.Value;
            Assert.That(s.Life.DrainRate(frozen, brain), Is.EqualTo(0.1f / 120f).Within(0.0001f),
                "the cold arrest does not drain at a tenth of the arrest rate.");
            Assert.That(s.Analyzer(frozen), Does.Contain("CARDIAC ARREST: cold").And.Contain("rewarm above 262 K"));
            Assert.That(s.Shock(frozen, out var refused), Is.False, "the paddles shocked a core under the arrest line.");
            Assert.That(refused, Is.EqualTo(WolfmedRevivalSystem.TooCold));

            // Rewarmed: the core follows a warmer surface at once, and the shock takes.
            Hold(s, frozen, 300f, 1);
            Assert.That(s.Shock(frozen, out var line), Is.True, $"the paddles refused a rewarmed body: {line}");
            Hold(s, frozen, 300f, 30);
            Assert.That(s.Life.InArrest(frozen), Is.False, "a rewarmed, shocked heart stopped again.");

            // Rewarming wakes an Unconscious patient.
            Hold(s, rewarmed, FreezerSurface, 2600, _ => s.State(rewarmed) == WolfmedConsciousness.Unconscious);
            Assert.That(s.State(rewarmed), Is.EqualTo(WolfmedConsciousness.Unconscious));
            var woke = Hold(s, rewarmed, 305f, 10, _ => s.State(rewarmed) != WolfmedConsciousness.Unconscious);
            Assert.That(s.State(rewarmed), Is.Not.EqualTo(WolfmedConsciousness.Unconscious), "rewarming did not wake the patient.");
            Note($"HypothermiaScenarioTest: rewarmed to 305 K, awake after {woke} s.");
        });

        await RunSeconds(4);
        await Server.WaitAssertion(() =>
            Assert.That(s.State(rewarmed), Is.EqualTo(WolfmedConsciousness.Up), "the rewarmed patient did not stand."));
    }

    /// <summary>
    /// <c>HeatStrokeScenarioTest</c> (plan §3.10): a core over the heat line less 7 K is Downed, cause Heat; over the
    /// line it is Unconscious and breathing with the brain draining 1/300 per s; cooling wakes them. The fire grace: a
    /// body on fire, and for the grace after it is out (60 s shipped; the plan started at 30), enters no heat cause; an
    /// existing heat stroke persists through
    /// ignition; re-ignition inside the grace does not extend it; only a core back under the heat exhaustion line earns
    /// a new one.
    /// </summary>
    [Test]
    public async Task HeatStrokeScenarioTest()
    {
        await Pin();
        var map = await Pair.CreateTestMap();
        var s = new WolfmedScenario(SEntMan);
        EntityUid hot = default, cooled = default, graced = default, stroke = default, relit = default, renewed = default;

        await Server.WaitPost(() =>
        {
            s.SetAir(map.MapUid, true);
            s.KeepGrid(map.Grid);
            hot = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            cooled = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            graced = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            stroke = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            relit = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            renewed = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
        });
        await RunSeconds(3);

        await Server.WaitAssertion(() =>
        {
            var fire = SEntMan.System<FlammableSystem>();

            // Heat exhaustion, then heat stroke.
            Hold(s, hot, 320f, 3);
            Assert.Multiple(() =>
            {
                Assert.That(s.State(hot), Is.EqualTo(WolfmedConsciousness.Downed));
                Assert.That(s.Vitals(hot).Cause, Is.EqualTo(WolfmedCause.Heat));
                Assert.That(s.Analyzer(hot), Does.Contain("DOWNED: overheating").And.Contain("Core temperature 320 K"));
            });

            Hold(s, hot, 330f, 1);
            Assert.Multiple(() =>
            {
                Assert.That(s.State(hot), Is.EqualTo(WolfmedConsciousness.Unconscious));
                Assert.That(s.Vitals(hot).Cause, Is.EqualTo(WolfmedCause.Heat));
                Assert.That(s.Vitals(hot).Breathing, Is.EqualTo(WolfmedBreathing.Normal), "heat stroke stopped the chest.");
                Assert.That(BodyTemperature.InHeatStroke(hot), Is.True);
                // Playtest 3: the heat-stroke route's first aid on "Do first".
                Assert.That(s.Analyzer(hot), Does.Contain("UNCONSCIOUS: overheating")
                    .And.Contain(WolfmedVitalsText.Aid(WolfmedRoutes.HeatStroke, false)));
            });

            var before = s.Life.GetOxygenation(hot);
            Hold(s, hot, 330f, 60);
            InBand(before - s.Life.GetOxygenation(hot), 60f / 300f, "heat stroke brain drain over 60 s");
            var arrest = 61 + Hold(s, hot, 330f, 400, _ => s.Life.InArrest(hot));
            Note($"HeatStrokeScenarioTest: heat stroke at 330 K arrests at {arrest} s (derived 255 s).");
            Assert.That(s.Life.InArrest(hot), Is.True, "heat stroke never stopped the heart.");
            Assert.That(SEntMan.GetComponent<WolfmedCardiacArrestComponent>(hot).Cause, Is.EqualTo("heat"));
            InBand(arrest, (1f - 0.15f) * 300f, "heat stroke arrest");

            // Cooling wakes them.
            Hold(s, cooled, 330f, 2);
            Assert.That(s.State(cooled), Is.EqualTo(WolfmedConsciousness.Unconscious));
            Hold(s, cooled, 308f, 2);
            Assert.That(s.State(cooled), Is.Not.EqualTo(WolfmedConsciousness.Unconscious), "cooling did not wake heat stroke.");
            Assert.That(Holds(s, cooled, WolfmedCause.Heat), Is.False);

            // The grace: nothing while burning, nothing for the grace after, then the heat counts.
            fire.SetFireStacks(graced, 5, ignite: true);
            Hold(s, graced, 600f, 60, _ =>
            {
                Assert.That(Holds(s, graced, WolfmedCause.Heat), Is.False, "a burning body entered a heat cause.");
                return false;
            });
            fire.Extinguish(graced);
            var entered = Hold(s, graced, 400f, GraceSeconds + 30, _ => Holds(s, graced, WolfmedCause.Heat));
            Note($"HeatStrokeScenarioTest: the heat cause came {entered} s after the fire went out.");
            Assert.That(entered, Is.InRange(GraceSeconds, GraceSeconds + 2), "the grace did not end on time after the fire went out.");

            // A heat stroke the body already has persists through catching fire.
            Hold(s, stroke, 330f, 2);
            Assert.That(BodyTemperature.InHeatStroke(stroke), Is.True);
            fire.SetFireStacks(stroke, 5, ignite: true);
            Hold(s, stroke, 600f, 10);
            Assert.Multiple(() =>
            {
                Assert.That(s.State(stroke), Is.EqualTo(WolfmedConsciousness.Unconscious));
                Assert.That(s.Vitals(stroke).Cause, Is.EqualTo(WolfmedCause.Heat), "catching fire cleared a heat stroke.");
            });

            // Re-ignition inside the grace does not extend it.
            fire.SetFireStacks(relit, 5, ignite: true);
            Hold(s, relit, 600f, 5);
            fire.Extinguish(relit);
            Hold(s, relit, 400f, 15);
            fire.SetFireStacks(relit, 5, ignite: true);
            var relitEntered = 15 + Hold(s, relit, 600f, GraceSeconds + 30, _ => Holds(s, relit, WolfmedCause.Heat));
            Assert.That(relitEntered, Is.InRange(GraceSeconds, GraceSeconds + 2), "re-ignition moved the end of the grace.");

            // A new grace only once the core is back under the heat exhaustion line.
            fire.SetFireStacks(renewed, 5, ignite: true);
            Hold(s, renewed, 600f, 5);
            fire.Extinguish(renewed);
            Hold(s, renewed, 400f, GraceSeconds + 2);
            Assert.That(Holds(s, renewed, WolfmedCause.Heat), Is.True);
            fire.SetFireStacks(renewed, 5, ignite: true);
            Hold(s, renewed, 600f, 5);
            Assert.That(Holds(s, renewed, WolfmedCause.Heat), Is.True, "a still-hot body got a second grace.");
            fire.Extinguish(renewed);
            Hold(s, renewed, 310f, 3);
            Assert.That(Holds(s, renewed, WolfmedCause.Heat), Is.False, "cooling did not clear the heat.");
            fire.SetFireStacks(renewed, 5, ignite: true);
            Hold(s, renewed, 600f, 10);
            Assert.That(Holds(s, renewed, WolfmedCause.Heat), Is.False, "a cooled body's next fire got no grace.");
        });
    }

    /// <summary>
    /// The lines come from each species' own thresholds, scaled so none sits closer to the normal temperature than
    /// wolfmed.temperature_line_max_share of the gap allows: a reptilian (cold threshold 285 K, normal 310 K) is not
    /// hypothermic at its own normal temperature, and an avali (normal 261 K, heat threshold 310 K) is not pushed into
    /// heat stroke by a fever. Both stand in station air.
    /// </summary>
    [Test]
    public async Task SpeciesLinesTest()
    {
        await Pin();
        var map = await Pair.CreateTestMap();
        var s = new WolfmedScenario(SEntMan);
        EntityUid human = default, reptilian = default, avali = default;

        await Server.WaitPost(() =>
        {
            s.SetAir(map.MapUid, true);
            human = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            reptilian = SEntMan.SpawnEntity("MobReptilian", map.GridCoords);
            avali = SEntMan.SpawnEntity("MobAvali", map.GridCoords);
        });
        await RunSeconds(10);

        await Server.WaitAssertion(() =>
        {
            var lizard = BodyTemperature.GetLines(reptilian)!.Value;
            var bird = BodyTemperature.GetLines(avali)!.Value;
            Note($"SpeciesLinesTest: reptilian cold lines {lizard.ColdDown:0.0}/{lizard.ColdOut:0.0}/{lizard.ColdArrest:0.0} K " +
                 $"(normal {lizard.Normal:0.0}); avali heat lines {bird.HeatDown:0.0}/{bird.HeatOut:0.0} K (normal {bird.Normal:0.0}), " +
                 $"fever ceiling {BodyTemperature.FeverCeiling(avali, 313f):0.0} K.");
            Assert.Multiple(() =>
            {
                Assert.That(lizard.ColdDown, Is.LessThan(lizard.Normal - 5f), "a reptilian is hypothermic at its own normal temperature.");
                Assert.That(lizard.ColdOut, Is.LessThan(lizard.ColdDown).And.GreaterThan(lizard.ColdArrest));
                Assert.That(lizard.ColdArrest, Is.GreaterThan(285f));
                Assert.That(BodyTemperature.FeverCeiling(human, 313f), Is.EqualTo(313f), "the human fever moved.");
                Assert.That(BodyTemperature.FeverCeiling(avali, 313f), Is.LessThan(bird.HeatDown), "a fever can put an avali down.");
                foreach (var body in new[] { human, reptilian, avali })
                {
                    Assert.That(s.State(body), Is.EqualTo(WolfmedConsciousness.Up), $"{SEntMan.ToPrettyString(body)} is down in station air.");
                    Assert.That(Holds(s, body, WolfmedCause.Cold) || Holds(s, body, WolfmedCause.Heat), Is.False);
                }
            });
        });
    }

    /// <summary>
    /// The M5 real-time smoke test (plan §12.0, at most 3 simulated minutes): a naked human in space, nothing driven by
    /// hand. The surface falls to about 16 K within a minute; the core cools behind it, and the patient goes Downed,
    /// Unconscious and into a cold arrest at the times the recorded surface derives, ±20%.
    /// </summary>
    [Test]
    public async Task SpaceColdSmokeTest()
    {
        await Pin();
        var map = await Pair.CreateTestMap();
        var s = new WolfmedScenario(SEntMan);
        EntityUid body = default;

        await Server.WaitPost(() =>
        {
            SEntMan.System<AtmosphereSystem>().SetMapAtmosphere(map.MapUid, true, GasMixture.SpaceGas);
            s.KeepGrid(map.Grid);
            body = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
        });

        var derived = 0f;
        var normal = 0f;
        int? downed = null, unconscious = null, arrest = null;
        float? derivedDowned = null, derivedOut = null, derivedArrest = null;
        for (var second = 1; second <= 180 && arrest == null; second++)
        {
            await RunSeconds(1);
            var now = second;
            await Server.WaitPost(() =>
            {
                var lines = BodyTemperature.GetLines(body)!.Value;
                if (normal == 0f)
                {
                    normal = lines.Normal;
                    derived = normal;
                }

                // The core's own rule, run on the surface this second: what the times should be.
                var surface = Surface(body);
                if (surface < derived)
                    derived -= (derived - surface) * (1f - MathF.Exp(-1f / 900f));
                if (derivedDowned == null && derived < lines.ColdDown)
                    derivedDowned = now;
                if (derivedOut == null && derived < lines.ColdOut)
                    derivedOut = now;
                if (derivedArrest == null && derived < lines.ColdArrest)
                    derivedArrest = now;

                if (downed == null && Holds(s, body, WolfmedCause.Cold))
                    downed = now;
                if (unconscious == null && s.State(body) == WolfmedConsciousness.Unconscious &&
                    s.Vitals(body).Cause == WolfmedCause.Cold)
                    unconscious = now;
                if (arrest == null && s.Life.InArrest(body))
                    arrest = now;
            });
        }

        await Server.WaitAssertion(() =>
        {
            Note($"SpaceColdSmokeTest: cold Downed at {downed} s (derived {derivedDowned}), Unconscious " +
                                      $"at {unconscious} s ({derivedOut}), arrest at {arrest} s ({derivedArrest}), core " +
                                      $"{BodyTemperature.GetCore(body):0.0} K, surface {Surface(body):0.0} K.");
            Assert.Multiple(() =>
            {
                Assert.That(downed, Is.Not.Null, "space never made the patient hypothermic.");
                Assert.That(unconscious, Is.GreaterThan(downed!));
                Assert.That(arrest, Is.GreaterThan(unconscious!));
                Assert.That(SEntMan.GetComponent<WolfmedCardiacArrestComponent>(body).Cause, Is.EqualTo("cold"));
                InBand(downed!.Value, derivedDowned!.Value, "hypothermic in space");
                InBand(unconscious!.Value, derivedOut!.Value, "unconscious from cold in space");
                InBand(arrest!.Value, derivedArrest!.Value, "cold arrest in space");
            });
        });
    }

    /// <summary>
    /// Playtest 3: "I existed for like 4 seconds in a hot room." The core chases hot air over
    /// wolfmed.core_heating_seconds, so heat exhaustion and heat stroke are a timed limit: in 330 K air a body with the
    /// 318 / 325 K lines is Downed after about two minutes and in heat stroke after five and a half, not within
    /// seconds; cooler air brings it back over wolfmed.core_recovery_seconds.
    /// </summary>
    [Test]
    public async Task HeatStrokeIsTimedTest()
    {
        await Pin();
        await OverrideCVar(Side.Server, WolfmedCVars.CoreHeatingSeconds, 240f);
        await OverrideCVar(Side.Server, WolfmedCVars.CoreRecoverySeconds, 60f);
        try
        {
            var map = await Pair.CreateTestMap();
            var s = new WolfmedScenario(SEntMan);
            EntityUid body = default;
            await Server.WaitPost(() =>
            {
                s.SetAir(map.MapUid, true);
                s.KeepGrid(map.Grid);
                body = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            });
            await RunSeconds(3);

            await Server.WaitAssertion(() =>
            {
                Hold(s, body, 330f, 4);
                Assert.That(s.State(body), Is.EqualTo(WolfmedConsciousness.Up), "hot air knocked the body down within seconds.");

                var downed = 4 + Hold(s, body, 330f, 600, _ => s.State(body) == WolfmedConsciousness.Downed);
                var stroke = downed + Hold(s, body, 330f, 900, _ => BodyTemperature.InHeatStroke(body));
                Note($"HeatStrokeIsTimedTest: in 330 K air, Downed at {downed} s (derived 121), heat stroke at {stroke} s (derived 331).");
                Assert.Multiple(() =>
                {
                    Assert.That(s.Vitals(body).Cause, Is.EqualTo(WolfmedCause.Heat));
                    // From 310.15 K with a 240 s time constant: 318 K is 39.5% of the 19.85 K gap, 325 K is 74.8%.
                    InBand(downed, 121f, "heat exhaustion in 330 K air");
                    InBand(stroke, 331f, "heat stroke in 330 K air");
                });

                var woke = Hold(s, body, 300f, 120, _ => !BodyTemperature.InHeatStroke(body));
                Note($"HeatStrokeIsTimedTest: out of heat stroke {woke} s after the air cooled to 300 K.");
                Assert.That(woke, Is.LessThan(60), "cooler air did not bring the core back down in time.");
            });
        }
        finally
        {
            await OverrideCVar(Side.Server, WolfmedCVars.CoreHeatingSeconds, 0f);
            await OverrideCVar(Side.Server, WolfmedCVars.CoreRecoverySeconds, 0f);
        }
    }
}
