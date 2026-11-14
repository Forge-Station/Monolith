#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Client._WF.Planets;
using Content.IntegrationTests.Pair;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Atmos.Portable;
using Content.Shared.Atmos.Piping.Portable.Components;
using Content.Shared.Atmos.Piping.Unary.Components;
using Content.Shared._WF.Planets;
using Content.Shared._WF.Warmth;
using Content.Shared.Maps;
using Content.Shared.Power.EntitySystems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using static Content.IntegrationTests.Tests._WF.Planets.PlanetFixture;
using ClientWarmth = Content.Client._WF.Warmth.WFWarmthSystem;

namespace Content.IntegrationTests.Tests._WF.Warmth;

/// <summary>
/// The HUD carries the air temperature of the player's own tile: outdoors on a planet, inside a room a space heater
/// warms on that planet, and off the planet in the compact readout.
/// </summary>
[TestFixture]
[TestOf(typeof(ClientWarmth))]
public sealed class WarmthHudTest
{
    private const string Grass = "FloorPlanetGrass";
    private const string Wall = "WallSolid";
    private const string Heater = "SpaceHeaterEnabled";

    [Test]
    public async Task HudShowsTheAirHereIndoorsAndOut()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var client = pair.Client;
        var entMan = server.EntMan;
        var atmos = server.System<AtmosphereSystem>();

        await EnableFeature(pair);
        var layers = await BuildStandalone(pair);
        var ground = layers[0];
        var room = Area(Vector2i.Zero, new Vector2i(2, 2)).ToList();
        EntityUid heater = default, inside = default;

        await server.WaitPost(() =>
        {
            SetTiles(pair, ground, Grass, Area(new Vector2i(-4, -4), new Vector2i(12, 12)));
            SetTiles(pair, ground, FloorTile, room);
            foreach (var index in Area(new Vector2i(-1, -1), new Vector2i(3, 3)).Except(room))
                entMan.SpawnEntity(Wall, new EntityCoordinates(ground, index.X + 0.5f, index.Y + 0.5f));

            heater = entMan.SpawnEntity(Heater, new EntityCoordinates(ground, 0.5f, 0.5f));
            server.System<SharedPowerReceiverSystem>().SetNeedsPower(heater, false);
            entMan.GetComponent<GasThermoMachineComponent>(heater).TargetTemperature = 308.15f;
            entMan.GetComponent<SpaceHeaterComponent>(heater).Mode = SpaceHeaterMode.Heat;
            inside = entMan.SpawnEntity("MobHuman", new EntityCoordinates(ground, 2.5f, 2.5f));
        });

        var viewer = await AttachViewer(pair, ground, new Vector2(8.5f, 8.5f));
        await pair.RunTicksSync(pair.SecondsToTicks(60f));

        int outsideHere = 0, insideHere = 0;
        float planetKelvin = 0f, roomKelvin = 0f;
        await server.WaitAssertion(() =>
        {
            planetKelvin = atmos.GetTileMixture(null, ground, new Vector2i(8, 8))!.Temperature;
            roomKelvin = atmos.GetTileMixture(ground, ground, new Vector2i(2, 2))!.Temperature;
            var outside = entMan.GetComponent<WFWarmthComponent>(viewer);
            var indoors = entMan.GetComponent<WFWarmthComponent>(inside);
            Assert.That(outside.HereCelsius, Is.Not.Null);
            Assert.That(indoors.HereCelsius, Is.Not.Null);
            outsideHere = outside.HereCelsius!.Value;
            insideHere = indoors.HereCelsius!.Value;
            TestContext.Out.WriteLine($"planet air {planetKelvin - 273.15f:F1} °C, heated room {roomKelvin - 273.15f:F1} °C");

            // The climate keeps moving the planet's air, so the reading may be a step behind it.
            Assert.That(outsideHere, Is.EqualTo((int) MathF.Round(planetKelvin - 273.15f)).Within(1),
                "The reading outdoors is not the planet's air.");
            Assert.That(insideHere, Is.EqualTo((int) MathF.Round(roomKelvin - 273.15f)),
                "The reading indoors is not the room's air.");
            Assert.That(roomKelvin, Is.GreaterThan(planetKelvin + 3f), "A space heater did not warm a room on a planet.");
        });

        await pair.RunTicksSync(pair.SecondsToTicks(2f));
        var hud = client.System<WFPlanetTimepieceHudSystem>();
        await client.WaitAssertion(() =>
        {
            Assert.That(hud.HudVisible, Is.True);
            // The planet's air drifts with its day; a degree either way is the same reading a tick apart.
            Assert.That(Reading(hud.DisplayedHere), Is.InRange(outsideHere - 1, outsideHere + 1), hud.DisplayedHere);
            Assert.That(hud.DisplayedFeel, Is.Not.Empty, "The HUD shows no body feel.");
            Assert.That(hud.DisplayedClimate, Does.Contain("Outside"), "The HUD does not name the outside temperature.");
        });

        // Into the heated room: the reading follows the player indoors.
        await server.WaitPost(() => server.System<SharedTransformSystem>()
            .SetCoordinates(viewer, new EntityCoordinates(ground, 1.5f, 1.5f)));
        await pair.RunTicksSync(pair.SecondsToTicks(3f));
        int indoorsNow = 0;
        await server.WaitPost(() => indoorsNow = entMan.GetComponent<WFWarmthComponent>(viewer).HereCelsius ?? -999);
        await pair.RunTicksSync(pair.SecondsToTicks(1f));
        await client.WaitAssertion(() =>
        {
            Assert.That(indoorsNow, Is.GreaterThan(outsideHere), "The player's reading did not warm indoors.");
            Assert.That(Reading(hud.DisplayedHere), Is.InRange(indoorsNow - 1, indoorsNow + 1), hud.DisplayedHere);
        });

        // Off the planet, in vacuum: the planet panel goes and the compact readout says so.
        var space = await pair.CreateTestMap();
        await server.WaitPost(() => server.System<SharedTransformSystem>()
            .SetCoordinates(viewer, new EntityCoordinates(space.MapUid, Vector2.Zero)));
        await pair.RunTicksSync(pair.SecondsToTicks(3f));
        var compact = client.System<ClientWarmth>();
        await client.WaitAssertion(() =>
        {
            Assert.That(hud.HudVisible, Is.False);
            Assert.That(compact.CompactVisible, Is.True, "Off the planet nothing shows the air here.");
            Assert.That(compact.CompactHere, Is.EqualTo("Here: vacuum"));
        });

        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    /// <summary>The degrees in a "Here 12°C" reading; fails on anything else.</summary>
    private static int Reading(string text)
    {
        var match = System.Text.RegularExpressions.Regex.Match(text, "^Here (-?[0-9]+)°C$");
        Assert.That(match.Success, Is.True, $"Not a temperature reading: {text}");
        return int.Parse(match.Groups[1].Value);
    }

    private static IEnumerable<Vector2i> Area(Vector2i from, Vector2i to)
    {
        for (var x = from.X; x <= to.X; x++)
        for (var y = from.Y; y <= to.Y; y++)
        {
            yield return new Vector2i(x, y);
        }
    }

    private static void SetTiles(TestPair pair, EntityUid ground, string tile, IEnumerable<Vector2i> indices)
    {
        var server = pair.Server;
        var id = server.ResolveDependency<ITileDefinitionManager>()[tile].TileId;
        var grid = server.EntMan.GetComponent<MapGridComponent>(ground);
        server.System<SharedMapSystem>().SetTiles(ground, grid, indices.Select(index => (index, new Tile(id))).ToList());
    }
}
