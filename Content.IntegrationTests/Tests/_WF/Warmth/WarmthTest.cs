#nullable enable
using System.Collections.Generic;
using Content.Shared.FixedPoint;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Tests._WF.Wolfmed;
using Content.IntegrationTests.Tests._WF.Wolfmed.Scenarios;
using Content.Server._WF.Warmth;
using Content.Server._WF.Wolfmed.Life;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Temperature.Components;
using Content.Shared._WF.Warmth;
using Content.Shared._WF.Wolfmed.Consciousness;
using Content.Shared._WF.WolfWeather;
using Content.Shared.Atmos;
using Content.Shared.Damage;
using Content.Shared.Inventory;
using Content.Shared.Maps;
using Content.Shared.Movement.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Warmth;

/// <summary>
/// Body warmth out in the weather: the owner's pick of an unprotected person in an Asclepiu winter night blizzard, a
/// coat and a bonfire against it, rain soaking and drying, and the warning signs coming before any harm.
/// </summary>
[TestFixture]
[TestOf(typeof(WFWarmthSystem))]
public sealed class WarmthTest : WolfmedGameTest
{
    /// <summary>Asclepiu's winter night: -16 °C.</summary>
    private const float WinterNight = 257.15f;

    private const string OpenTile = "FloorPlanetGrass";
    private const string CoveredTile = "FloorSteel";
    private const string Blizzard = "WFBlizzard";
    private const string HeavyRain = "WFRainHeavy";
    private const string Coat = "ClothingOuterWinterCoat";
    private const int Half = 12;

    private readonly List<string> _notes = new();

    [TearDown]
    public void WriteNotes()
    {
        foreach (var line in _notes)
            TestContext.Out.WriteLine(line);
        _notes.Clear();
    }

    private WFWarmthSystem Warmth => SEntMan.System<WFWarmthSystem>();

    /// <summary>A test map of open ground in the given air, with the given weather set on it at full strength.</summary>
    private async Task<TestMapData> Outdoors(float kelvin, string? weather)
    {
        var map = await CreateTestMap();
        await Server.WaitPost(() =>
        {
            var air = WolfmedScenario.Air();
            air.Temperature = kelvin;
            SEntMan.System<AtmosphereSystem>().SetMapAtmosphere(map.MapUid, false, air);
            new WolfmedScenario(SEntMan).KeepGrid(map.Grid);

            var maps = SEntMan.System<SharedMapSystem>();
            var tile = new Tile(Server.ResolveDependency<ITileDefinitionManager>()[OpenTile].TileId);
            var tiles = new List<(Vector2i, Tile)>();
            for (var x = -Half; x <= Half; x++)
            {
                for (var y = -Half; y <= Half; y++)
                    tiles.Add((new Vector2i(x, y), tile));
            }

            maps.SetTiles(map.Grid.Owner, map.Grid.Comp, tiles);
            if (weather != null)
                SetWeatherNow(map.MapUid, weather);
        });
        return map;
    }

    /// <summary>Sets a weather already faded in, so timings start at full strength.</summary>
    private void SetWeatherNow(EntityUid map, string? weather)
    {
        var system = SEntMan.System<SharedWolfWeatherSystem>();
        system.SetWeather(map, weather == null ? (ProtoId<WolfWeatherPrototype>?) null : new ProtoId<WolfWeatherPrototype>(weather), null, 1);
        if (weather == null)
            return;

        var comp = SEntMan.GetComponent<WolfWeatherMapComponent>(map);
        var now = SGameTiming.CurTime;
        var weathers = comp.Weathers;
        weathers.Clear();
        weathers.Add(new WolfWeatherEntry { Weather = weather, Start = now - TimeSpan.FromHours(1), Seed = 1 });
    }

    private EntityUid Spawn(TestMapData map, float x, float y) =>
        SEntMan.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid.Owner, x, y));

    private WFWarmthComponent? WarmthOf(EntityUid body) => SEntMan.GetComponentOrNull<WFWarmthComponent>(body);

    private float Core(EntityUid body) =>
        SEntMan.System<WolfmedBodyTemperatureSystem>().GetCore(body) ?? float.NaN;

    private float Surface(EntityUid body) => SEntMan.GetComponent<TemperatureComponent>(body).CurrentTemperature;

    private float ColdDamage(EntityUid body) =>
        SEntMan.GetComponent<DamageableComponent>(body).Damage.DamageDict.TryGetValue("Cold", out var cold)
            ? cold.Float()
            : 0f;

    private bool Down(EntityUid body) =>
        SEntMan.GetComponent<WolfmedConsciousnessComponent>(body).State != WolfmedConsciousness.Up;

    /// <summary>
    /// The owner's pick: an unprotected person (a jumpsuit's worth of nothing) standing in a -16 °C blizzard gets about
    /// five minutes of warning signs before the cold harms them, and the warnings come before any damage. Beside them,
    /// the same person in a winter coat never gets cold enough to be warned, and one by a bonfire stays comfortable;
    /// one brought to the fire already cold warms back up.
    /// </summary>
    [Test]
    public async Task BlizzardWarnsBeforeHarmAndACoatOrFireSaves()
    {
        var map = await Outdoors(WinterNight, Blizzard);
        EntityUid naked = default, coated = default, byFire = default, rescued = default;

        await Server.WaitPost(() =>
        {
            naked = Spawn(map, 0.5f, 0.5f);
            coated = Spawn(map, -6.5f, 0.5f);
            byFire = Spawn(map, 8.5f, 0.5f);
            rescued = Spawn(map, 7.5f, 1.5f);
            SEntMan.SpawnEntity("Bonfire", new EntityCoordinates(map.Grid.Owner, 8.5f, -0.5f));
            var coat = SEntMan.SpawnEntity(Coat, SEntMan.GetComponent<TransformComponent>(coated).Coordinates);
            Assert.That(SEntMan.System<InventorySystem>().TryEquip(coated, coat, "outerClothing", force: true), Is.True);
        });

        // A few seconds so every body has a core reading, then the rescued one starts as cold as the warnings.
        await RunSeconds(3);
        await Server.WaitPost(() => SEntMan.System<WolfmedBodyTemperatureSystem>().SetCore(rescued, 296f));

        int? chilly = null, warned = null, down = null, damaged = null, rescuedWarm = null;
        var nakedSlowed = false;
        var coatedWorst = WFBodyFeel.Comfortable;
        var fireWorst = WFBodyFeel.Comfortable;
        const int step = 5;
        for (var t = step; t <= 1500 && down == null; t += step)
        {
            await RunSeconds(step);
            var now = t;
            await Server.WaitPost(() =>
            {
                var warmth = WarmthOf(naked)!;
                if (chilly == null && warmth.Feel <= WFBodyFeel.Chilly)
                    chilly = now;
                if (warned == null && warmth.Strain == WFWarmthStrain.Cold)
                    warned = now;
                if (warmth.Strain == WFWarmthStrain.Cold &&
                    SEntMan.GetComponent<MovementSpeedModifierComponent>(naked).WalkSpeedModifier < 0.95f)
                    nakedSlowed = true;
                if (damaged == null && ColdDamage(naked) > 0f)
                    damaged = now;
                if (down == null && Down(naked))
                    down = now;

                coatedWorst = (WFBodyFeel) Math.Min((int) coatedWorst, (int) WarmthOf(coated)!.Feel);
                fireWorst = (WFBodyFeel) Math.Min((int) fireWorst, (int) WarmthOf(byFire)!.Feel);
                if (rescuedWarm == null && WarmthOf(rescued)!.Feel >= WFBodyFeel.Comfortable)
                    rescuedWarm = now;

                if (now % 60 == 0)
                {
                    _notes.Add($"t={now}s naked surface {Surface(naked):F1} core {Core(naked):F1} wet {warmth.Wetness:F2} " +
                               $"feel {warmth.Feel} | coat surface {Surface(coated):F1} core {Core(coated):F1} | " +
                               $"fire surface {Surface(byFire):F1} core {Core(byFire):F1} {WarmthOf(byFire)!.Feel}");
                }
            });
        }

        _notes.Add($"naked: chilly {chilly}s, warned {warned}s, down {down}s, cold damage {damaged?.ToString() ?? "none"}; " +
                   $"coat worst {coatedWorst}, fire worst {fireWorst}, rescued warm by {rescuedWarm}s");

        Assert.Multiple(() =>
        {
            Assert.That(warned, Is.Not.Null, "The blizzard never warned an unprotected person.");
            Assert.That(down, Is.Not.Null, "The blizzard never harmed an unprotected person.");
            Assert.That(warned ?? int.MaxValue, Is.LessThan(down ?? 0), "The cold harmed before it warned.");
            Assert.That(damaged == null || damaged > warned, Is.True, "Cold damage came before the warnings.");
            Assert.That(down - warned, Is.InRange(240, 360), "The owner's pick is about five minutes of warning.");
            Assert.That(chilly ?? int.MaxValue, Is.LessThan(warned ?? 0), "The body never felt chilly before the warnings.");
            Assert.That(nakedSlowed, Is.True, "The cold's warnings did not slow the body down.");
            Assert.That(coatedWorst, Is.GreaterThan(WFBodyFeel.Cold), "A winter coat did not keep the blizzard off.");
            Assert.That(fireWorst, Is.GreaterThan(WFBodyFeel.Cold), "A bonfire did not keep the blizzard off.");
            Assert.That(rescuedWarm, Is.Not.Null.And.LessThanOrEqualTo(60), "A bonfire did not warm a cold body back up.");
        });
    }

    /// <summary>
    /// Clothing slows cooling: in the same still cold air the core of a body in a winter coat stays warmer than an
    /// unclothed one's. Measures the still-air margin the examine text's numbers come from.
    /// </summary>
    [Test]
    public async Task ClothingSlowsCooling()
    {
        var map = await Outdoors(WinterNight, null);
        EntityUid naked = default, coated = default;
        await Server.WaitPost(() =>
        {
            naked = Spawn(map, 0.5f, 0.5f);
            coated = Spawn(map, 4.5f, 0.5f);
            var coat = SEntMan.SpawnEntity(Coat, SEntMan.GetComponent<TransformComponent>(coated).Coordinates);
            Assert.That(SEntMan.System<InventorySystem>().TryEquip(coated, coat, "outerClothing", force: true), Is.True);
        });

        await RunSeconds(240);
        await Server.WaitAssertion(() =>
        {
            _notes.Add($"still -16 °C after 240 s: naked surface {Surface(naked):F1} core {Core(naked):F1}; " +
                       $"coat surface {Surface(coated):F1} core {Core(coated):F1}; " +
                       $"naked margin {Surface(naked) - WinterNight:F1} K");
            // The skin shows the coat at once; how far the core has fallen by now depends on the runner's pace.
            Assert.That(Surface(coated), Is.GreaterThan(Surface(naked) + 10f), "A coat did not slow the cooling.");
            Assert.That(Core(coated), Is.GreaterThanOrEqualTo(Core(naked)), "A coated body ended up colder inside.");
            Assert.That(Surface(naked) - WinterNight, Is.InRange(WFWarmthSystem.NakedMargin * 0.75f,
                WFWarmthSystem.NakedMargin * 1.25f), "The examine text's still-air margin no longer matches.");
        });
    }

    /// <summary>In scorching air the heat's warnings, sweating and the slowdown, come before any burn.</summary>
    [Test]
    public async Task HeatWarnsBeforeItBurns()
    {
        var map = await Outdoors(345f, null);
        EntityUid body = default;
        var start = FixedPoint2.Zero;
        await Server.WaitPost(() =>
        {
            body = Spawn(map, 0.5f, 0.5f);
            // Whatever the spawn itself costs (a landing knock, a first contact with the air) isn't the heat burning.
            start = SEntMan.GetComponent<DamageableComponent>(body).Damage.DamageDict.GetValueOrDefault("Heat");
        });

        int? warned = null, burned = null;
        for (var t = 1; t <= 120 && burned == null; t++)
        {
            await RunSeconds(1);
            var now = t;
            await Server.WaitPost(() =>
            {
                if (warned == null && WarmthOf(body)?.Strain == WFWarmthStrain.Hot)
                    warned = now;
                if (burned == null && SEntMan.GetComponent<DamageableComponent>(body).Damage.DamageDict
                        .TryGetValue("Heat", out var heat) && heat > start + 1)
                    burned = now;
            });
        }

        _notes.Add($"72 °C air: warned {warned}s, first burn {burned?.ToString() ?? "none"}");
        Assert.That(warned, Is.Not.Null, "Scorching air never warned.");
        Assert.That(burned == null || burned > warned, Is.True, "The heat burned before it warned.");
    }

    /// <summary>
    /// Heavy rain soaks a body in the open and not one under cover; once it stops the body dries, faster by a fire, and
    /// a wet body loses heat faster than a dry one.
    /// </summary>
    [Test]
    public async Task RainSoaksAndDries()
    {
        var map = await Outdoors(283.15f, HeavyRain);
        EntityUid open = default, covered = default, byFire = default;
        await Server.WaitPost(() =>
        {
            var maps = SEntMan.System<SharedMapSystem>();
            var steel = new Tile(Server.ResolveDependency<ITileDefinitionManager>()[CoveredTile].TileId);
            maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(-6, 0), steel);
            open = Spawn(map, 0.5f, 0.5f);
            covered = Spawn(map, -5.5f, 0.5f);
            byFire = Spawn(map, 6.5f, 0.5f);
        });

        await RunSeconds(75);
        float openWet = 0f, fireWet = 0f;
        await Server.WaitAssertion(() =>
        {
            openWet = WarmthOf(open)!.Wetness;
            _notes.Add($"after 75 s of heavy rain: open {openWet:F2}, covered {WarmthOf(covered)!.Wetness:F2}");
            Assert.That(openWet, Is.GreaterThan(0.9f), "Heavy rain did not soak a body in the open.");
            Assert.That(WarmthOf(covered)!.Wetness, Is.EqualTo(0f), "Rain soaked a body under cover.");
        });

        await Server.WaitPost(() =>
        {
            SetWeatherNow(map.MapUid, null);
            SEntMan.SpawnEntity("Bonfire", new EntityCoordinates(map.Grid.Owner, 6.5f, -1.0f));
            fireWet = WarmthOf(byFire)!.Wetness;
        });

        await RunSeconds(60);
        await Server.WaitAssertion(() =>
        {
            var openDried = openWet - WarmthOf(open)!.Wetness;
            var fireDried = fireWet - WarmthOf(byFire)!.Wetness;
            _notes.Add($"a minute after the rain: open dried {openDried:F2}, by the fire dried {fireDried:F2}");
            Assert.That(openDried, Is.GreaterThan(0.05f), "A wet body did not dry in the open.");
            Assert.That(fireDried, Is.GreaterThan(openDried * 3f), "A fire did not dry a body faster.");
        });

        // Wet clothes cool faster: the same cooling step costs a wet body more heat.
        await Server.WaitAssertion(() =>
        {
            var wet = WarmthOf(open)!;
            var ev = new Content.Shared.Temperature.ModifyChangedTemperatureEvent(-100f);
            wet.Wetness = 1f;
            SEntMan.EventBus.RaiseLocalEvent(open, ev);
            var wetLoss = -ev.TemperatureDelta;
            wet.Wetness = 0f;
            ev = new Content.Shared.Temperature.ModifyChangedTemperatureEvent(-100f);
            SEntMan.EventBus.RaiseLocalEvent(open, ev);
            Assert.That(wetLoss, Is.GreaterThan(-ev.TemperatureDelta * 1.3f), "Wet clothes insulated as well as dry ones.");
        });
    }
}
