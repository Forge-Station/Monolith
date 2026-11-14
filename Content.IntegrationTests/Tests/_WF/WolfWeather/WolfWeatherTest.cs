#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server._WF.Planets;
using Content.Server._WF.WolfWeather;
using Content.Server.Parallax;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.Planets;
using Content.Shared._WF.WolfWeather;
using Content.Shared.Item;
using Content.Shared.Light.Components;
using Content.Shared.Light.EntitySystems;
using Content.Shared.Maps;
using Content.Shared.Parallax.Biomes;
using Content.Shared.Station.Components;
using Content.Shared.Weather;
using Robust.Server.Console;
using Robust.Shared.Console;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;
using static Content.IntegrationTests.Tests._WF.Planets.PlanetFixture;

namespace Content.IntegrationTests.Tests._WF.WolfWeather;

/// <summary>WolfWeather as planet weather, its cover rules, lightning, wind, schedules and admin commands.</summary>
[TestFixture]
[TestOf(typeof(SharedWolfWeatherSystem))]
public sealed class WolfWeatherTest
{
    /// <summary>A tile that takes weather off a planet, for the plain test maps.</summary>
    private const string OpenTile = "FloorPlanetGrass";

    /// <summary>Half the side of the square of open ground laid on a test map.</summary>
    private const int Half = 24;

    private static readonly ProtoId<WolfWeatherSettingsPrototype> DefaultSettings = "WFDefault";
    private static readonly ProtoId<WeatherSchedulePrototype> Temperate = "WFTemperate";

    [Test]
    public async Task EveryPlanetWeatherHasAWolfWeather()
    {
        await using var pair = await PoolManager.GetServerClient();
        await pair.Server.WaitAssertion(() =>
        {
            var proto = pair.Server.ResolveDependency<IPrototypeManager>();
            foreach (var profile in proto.EnumeratePrototypes<WFPlanetWeatherPrototype>())
            {
                var phases = profile.Weather.Concat(profile.Storms.SelectMany(s =>
                    new[] { s.Telegraph, (ProtoId<WeatherPrototype>?) s.Main, s.End }.OfType<ProtoId<WeatherPrototype>>()));
                foreach (var weather in phases)
                {
                    Assert.That(profile.WolfWeathers.TryGetValue(weather, out var wolf), Is.True,
                        $"{profile.ID} shows nothing for {weather}.");
                    Assert.That(proto.HasIndex<WolfWeatherPrototype>(wolf), Is.True, $"{profile.ID}: {wolf} is no wolfWeather.");
                }
            }
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task PlanetWeatherSetsItsWolfWeatherOnThatWorldOnly()
    {
        await using var pair = await PoolManager.GetServerClient();
        await EnableFeature(pair);
        var server = pair.Server;
        var entMan = server.EntMan;
        await server.WaitPost(() => server.CfgMan.SetCVar(PlanetCVars.PlanetWeather, true));

        var worlds = new List<List<EntityUid>>();
        await server.WaitPost(() =>
        {
            var proto = server.ResolveDependency<IPrototypeManager>();
            foreach (var name in new[] { "Asclepiu", "Thrascias" })
            {
                var surface = proto.Index<WFPlanetSurfacePrototype>("WFSurface" + name);
                var built = server.System<WFPlanetNetworkSystem>().BuildNetwork(surface, Vector2.Zero, name, null);
                var layers = entMan.GetComponent<WFPlanetNetworkComponent>(built!.Value).Layers.ToList();
                server.System<BiomeSystem>().SetEnabled((layers[0], entMan.GetComponent<BiomeComponent>(layers[0])), false);
                worlds.Add(layers);
            }

            var now = server.ResolveDependency<IGameTiming>().CurTime;
            Network(entMan, worlds[0]).NextChange = TimeSpan.Zero;
            Network(entMan, worlds[1]).NextChange = now + TimeSpan.FromMinutes(30);
        });

        // Two scheduler steps: the storm's first phase, then the next one cross-fades in.
        for (var step = 0; step < 2; step++)
        {
            await server.WaitRunTicks(pair.SecondsToTicks(3));
            await server.WaitAssertion(() =>
            {
                var planets = server.System<WFPlanetWeatherSystem>();
                var state = Network(entMan, worlds[0]);

                // The first step always starts a storm; a later one may end it, and then every layer fades out.
                if (step == 0)
                    Assert.That(state.Current, Is.Not.Null, "The scheduler set no weather.");

                var expected = planets.ResolveWolfWeather(state.Profile, state.Current);
                Assert.That(expected == null, Is.EqualTo(state.Current == null));

                // Weather falls on the ground only; the air above and the orbit stay clear.
                foreach (var layer in worlds[0].Skip(1))
                {
                    Assert.That(!entMan.TryGetComponent<WolfWeatherMapComponent>(layer, out var above)
                                || above.Weathers.Count == 0, Is.True, "Weather fell above the surface.");
                }

                foreach (var layer in worlds[0].Take(1))
                {
                    var wolf = entMan.GetComponent<WolfWeatherMapComponent>(layer);
                    var shown = SharedWolfWeatherSystem.GetCurrent(wolf);
                    if (expected == null)
                    {
                        Assert.That(wolf.Weathers.All(w => w.End != null), Is.True, "Clear skies left a weather on.");
                        continue;
                    }

                    Assert.That(shown?.Weather, Is.EqualTo(expected), "A layer shows another weather.");
                    Assert.That(shown?.End, Is.Null, "A planet's WolfWeather should hold until the scheduler moves.");
                }


                foreach (var layer in worlds[1])
                {
                    Assert.That(!entMan.TryGetComponent<WolfWeatherMapComponent>(layer, out var other)
                                || other.Weathers.Count == 0, Is.True, "One world's weather reached another.");
                }

                // Move the storm on a phase, unless it has already cleared.
                if (state.Phase != WFStormPhase.None)
                    state.NextChange = TimeSpan.Zero;
            });

            if (Network(entMan, worlds[0]).Phase == WFStormPhase.None)
                break;
        }

        foreach (var layers in worlds)
            await Teardown(pair, layers);
        await server.WaitPost(() => server.CfgMan.SetCVar(PlanetCVars.PlanetWeather, false));

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RoofsVesselsAndCavernsStayDry()
    {
        await using var pair = await PoolManager.GetServerClient();
        await EnableFeature(pair);
        var server = pair.Server;
        var entMan = server.EntMan;
        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(PlanetCVars.PlanetWeather, true);
            server.CfgMan.SetCVar(CavernCVars.Caverns, true);
        });

        var layers = await BuildStandalone(pair);
        var ground = layers[0];
        var hull = await BuildHull(pair, entMan.GetComponent<MapComponent>(ground).MapId, new Vector2(40, 40));

        await server.WaitPost(() => Network(entMan, layers).NextChange = TimeSpan.Zero);
        await server.WaitRunTicks(pair.SecondsToTicks(3));

        await server.WaitAssertion(() =>
        {
            var wolf = server.System<WolfWeatherSystem>();
            var maps = server.System<SharedMapSystem>();
            var roofs = server.System<SharedRoofSystem>();
            var tiles = server.ResolveDependency<ITileDefinitionManager>();
            var grid = entMan.GetComponent<MapGridComponent>(ground);

            // Steel is no weather tile, but on a planet roofs decide.
            var spot = new Vector2i(-30, -30);
            maps.SetTile(ground, grid, spot, new Tile(tiles[FloorTile].TileId));
            Assert.That(wolf.IsExposed((ground, grid, entMan.GetComponentOrNull<RoofComponent>(ground)),
                maps.GetTileRef(ground, grid, spot)), Is.True, "Open ground on a planet stayed dry.");
            var roof = entMan.EnsureComponent<RoofComponent>(ground);
            roofs.SetRoof((ground, grid, roof), spot, true);
            Assert.That(wolf.IsExposed((ground, grid, roof), maps.GetTileRef(ground, grid, spot)), Is.False,
                "It rained under a roof.");

            var hullGrid = entMan.GetComponent<MapGridComponent>(hull);
            var hullTile = maps.GetAllTiles(hull, hullGrid).First();
            Assert.That(wolf.IsExposed((hull, hullGrid, null), hullTile), Is.False, "It rained inside a vessel.");

            var network = entMan.GetComponent<WFPlanetNetworkComponent>(entMan.GetComponent<CEZMapComponent>(ground).NetworkUid);
            Assert.That(network.LowerLayers, Is.Not.Empty, "No cavern was built to check.");
            Assert.That(entMan.HasComponent<WolfWeatherMapComponent>(ground), Is.True);
            foreach (var cavern in network.LowerLayers)
            {
                Assert.That(!entMan.TryGetComponent<WolfWeatherMapComponent>(cavern, out var below) || below.Weathers.Count == 0,
                    Is.True, "It rained in a cavern.");
            }

            // A planet layer follows its own weather, never a schedule.
            Assert.That(server.System<WeatherScheduleSystem>().SetSchedule(ground, "WFTemperate"), Is.False);
            Assert.That(entMan.HasComponent<WeatherScheduleComponent>(ground), Is.False);
        });

        await Teardown(pair, layers);
        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(PlanetCVars.PlanetWeather, false);
            server.CfgMan.SetCVar(CavernCVars.Caverns, false);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task LightningStrikesOnlyOpenGroundNearTheLiving()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var grid = LayOpenGround(pair, map.Grid);
            var lightning = server.System<WolfLightningSystem>();
            var wolf = server.System<WolfWeatherSystem>();
            var maps = server.System<SharedMapSystem>();
            var roofs = server.System<SharedRoofSystem>();
            var bolt = new WolfWeatherLightning();

            var mob = entMan.SpawnEntity("MobHuman", new EntityCoordinates(grid, 0.5f, 0.5f));
            var ghost = entMan.SpawnEntity("MobObserver", new EntityCoordinates(grid, 0.5f, 0.5f));
            Assert.That(lightning.IsValidTarget(mob), Is.True);
            Assert.That(lightning.IsValidTarget(ghost), Is.False, "Lightning picked a ghost.");

            // Open ground: every strike lands on an open tile within range.
            for (var i = 0; i < 20; i++)
            {
                var spot = lightning.FindStrikeTile(mob, bolt);
                Assert.That(spot, Is.Not.Null);
                var tile = maps.GetTileRef(grid, grid.Comp, spot!.Value);
                Assert.That(wolf.IsExposed((grid, grid.Comp, null), tile), Is.True);
                Assert.That(spot.Value.Position.Length(), Is.LessThanOrEqualTo(bolt.StrikeRange.Y + 1.5f));
            }

            Assert.That(lightning.IsShockable(mob), Is.True, "A mob out in the open was spared.");

            // Roofed over, nothing is open: no strike, and nobody under the roof is shocked.
            var roof = entMan.EnsureComponent<RoofComponent>(grid);
            for (var x = -Half; x <= Half; x++)
            {
                for (var y = -Half; y <= Half; y++)
                    roofs.SetRoof((grid, grid.Comp, roof), new Vector2i(x, y), true);
            }

            Assert.That(lightning.FindStrikeTile(mob, bolt), Is.Null, "Lightning struck under a roof.");
            Assert.That(lightning.IsShockable(mob), Is.False, "A strike reached a mob indoors.");

            // A station's grid is never struck, roof or not.
            for (var x = -Half; x <= Half; x++)
            {
                for (var y = -Half; y <= Half; y++)
                    roofs.SetRoof((grid, grid.Comp, roof), new Vector2i(x, y), false);
            }

            Assert.That(lightning.FindStrikeTile(mob, bolt), Is.Not.Null);
            entMan.EnsureComponent<StationMemberComponent>(grid);
            Assert.That(lightning.FindStrikeTile(mob, bolt), Is.Null, "Lightning struck a station grid.");
            entMan.RemoveComponent<StationMemberComponent>(grid);

            entMan.DeleteEntity(mob);
            entMan.DeleteEntity(ghost);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task WindPushesLooseItemsOutdoorsOnly()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var grid = LayOpenGround(pair, map.Grid);
            var wind = server.System<WindPushSystem>();
            var roofs = server.System<SharedRoofSystem>();
            var xforms = server.System<SharedTransformSystem>();
            var settings = server.ResolveDependency<IPrototypeManager>().Index(DefaultSettings);

            var loose = entMan.SpawnEntity("Paper", new EntityCoordinates(grid, 0.5f, 0.5f));
            var anchored = entMan.SpawnEntity("Paper", new EntityCoordinates(grid, 0.5f, 3.5f));
            xforms.AnchorEntity(anchored);
            var indoors = entMan.SpawnEntity("Paper", new EntityCoordinates(grid, 0.5f, -3.5f));
            var roof = entMan.EnsureComponent<RoofComponent>(grid);
            roofs.SetRoof((grid, grid.Comp, roof), new Vector2i(0, -4), true);
            var edge = entMan.SpawnEntity("Paper", new EntityCoordinates(grid, Half + 0.5f, 6.5f));
            var mob = entMan.SpawnEntity("MobHuman", new EntityCoordinates(grid, -3.5f, 0.5f));

            // Far past the threshold, so every candidate is rolled in.
            var gale = new Vector2(30f, 0f);
            var pushed = wind.PushAround(xforms.GetMapCoordinates(loose), gale, settings, settings.PushMaxPerTick);
            Assert.That(pushed, Is.EqualTo(1), "The wind pushed something other than the one loose item.");

            Assert.That(entMan.GetComponent<PhysicsComponent>(loose).LinearVelocity.X, Is.GreaterThan(0f),
                "The loose item outdoors did not move downwind.");
            Assert.That(entMan.GetComponent<PhysicsComponent>(anchored).LinearVelocity, Is.EqualTo(Vector2.Zero));
            Assert.That(entMan.GetComponent<PhysicsComponent>(indoors).LinearVelocity, Is.EqualTo(Vector2.Zero));
            Assert.That(entMan.GetComponent<PhysicsComponent>(mob).LinearVelocity, Is.EqualTo(Vector2.Zero));

            // At the grid's downwind edge there is no ground ahead to blow onto.
            Assert.That(wind.PushAround(xforms.GetMapCoordinates(edge), gale, settings, 64), Is.Zero);
            Assert.That(entMan.GetComponent<PhysicsComponent>(edge).LinearVelocity, Is.EqualTo(Vector2.Zero),
                "The wind blew an item off its grid's edge.");

            // No wind past the threshold, nothing moves.
            Assert.That(wind.PushAround(xforms.GetMapCoordinates(loose), new Vector2(1f, 0f), settings, 64), Is.Zero);

            foreach (var uid in new[] { loose, anchored, indoors, edge, mob })
                entMan.DeleteEntity(uid);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ScheduleRollsOnlyWhileItsCVarIsOn()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(WolfWeatherCVars.WeatherSchedule, false);
            Assert.That(server.System<WeatherScheduleSystem>().SetSchedule(map.MapUid, "WFTemperate"), Is.True);
            entMan.GetComponent<WeatherScheduleComponent>(map.MapUid).NextChange = TimeSpan.Zero;
        });

        await server.WaitRunTicks(pair.SecondsToTicks(3));
        await server.WaitAssertion(() =>
        {
            Assert.That(!entMan.TryGetComponent<WolfWeatherMapComponent>(map.MapUid, out var weather) || weather.Weathers.Count == 0,
                Is.True, "The schedule rolled with its cvar off.");
            server.CfgMan.SetCVar(WolfWeatherCVars.WeatherSchedule, true);
            entMan.GetComponent<WeatherScheduleComponent>(map.MapUid).NextChange = TimeSpan.Zero;
        });

        await server.WaitRunTicks(pair.SecondsToTicks(3));
        await server.WaitAssertion(() =>
        {
            var schedule = entMan.GetComponent<WeatherScheduleComponent>(map.MapUid);
            var weather = entMan.GetComponent<WolfWeatherMapComponent>(map.MapUid);
            Assert.That(schedule.Current, Is.Not.Null, "The schedule rolled nothing.");
            Assert.That(SharedWolfWeatherSystem.GetCurrent(weather)?.Weather, Is.EqualTo(schedule.Current));
            var proto = server.ResolveDependency<IPrototypeManager>().Index(Temperate);
            Assert.That(proto.Weathers.Any(w => w.Weather == schedule.Current), Is.True);

            // Off: the schedule's own weather fades out.
            server.System<WeatherScheduleSystem>().SetSchedule(map.MapUid, null);
            Assert.That(weather.Weathers.All(w => w.End != null), Is.True);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AdminCommandsSetAndClearWeather()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        var console = server.ResolveDependency<IServerConsoleHost>();
        var id = map.MapId.ToString();

        await server.WaitAssertion(() =>
        {
            var now = server.ResolveDependency<IGameTiming>().CurTime;
            var shell = new RecordingShell(console);

            // Bad input is refused with an error, without throwing or touching the weather.
            foreach (var bad in new[]
                     {
                         "wolfweather", "wolfweather nope WFRainLight", "wolfweather 999999 WFRainLight",
                         $"wolfweather {id} nonsense", $"wolfweather {id} WFRainLight NaN", $"wolfweather {id} WFRainLight -5",
                         $"wolfweather {id} WFRainLight 99999999", $"wolfweather_schedule {id} nonsense", "wolfweather_schedule",
                     })
            {
                shell.Errors.Clear();
                Assert.DoesNotThrow(() => Run(console, shell, bad), bad);
                Assert.That(shell.Errors, Is.Not.Empty, $"No error for: {bad}");
            }

            Assert.That(!entMan.TryGetComponent<WolfWeatherMapComponent>(map.MapUid, out var untouched) || untouched.Weathers.Count == 0,
                Is.True, "Bad input changed the weather.");

            shell.Errors.Clear();
            Run(console, shell, $"wolfweather {id} WFRainLight");
            var weather = entMan.GetComponent<WolfWeatherMapComponent>(map.MapUid);
            Assert.That(SharedWolfWeatherSystem.GetCurrent(weather)?.Weather.Id, Is.EqualTo("WFRainLight"));
            Assert.That(SharedWolfWeatherSystem.GetCurrent(weather)?.End, Is.Null);

            Run(console, shell, $"wolfweather {id} WFSnow 30");
            var snow = SharedWolfWeatherSystem.GetCurrent(weather);
            Assert.That(snow?.Weather.Id, Is.EqualTo("WFSnow"));
            Assert.That(snow?.End, Is.EqualTo(now + TimeSpan.FromSeconds(30)));

            Run(console, shell, $"wolfweather {id} clear");
            Assert.That(weather.Weathers.All(w => w.End != null && w.End <= now + TimeSpan.FromSeconds(30)), Is.True,
                "Clear left a weather running.");
            Assert.That(weather.Wind, Is.EqualTo(Vector2.Zero));

            Run(console, shell, $"wolfweather_schedule {id} WFTemperate");
            Assert.That(entMan.GetComponent<WeatherScheduleComponent>(map.MapUid).Schedule?.Id, Is.EqualTo("WFTemperate"));
            Run(console, shell, $"wolfweather_schedule {id} status");
            Run(console, shell, $"wolfweather_schedule {id} off");
            Assert.That(entMan.GetComponent<WeatherScheduleComponent>(map.MapUid).Schedule, Is.Null);
            Assert.That(shell.Errors, Is.Empty, string.Join("; ", shell.Errors));
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Runs a console command straight through its command object, into the given shell.</summary>
    private static void Run(IConsoleHost console, IConsoleShell shell, string line)
    {
        var args = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var argStr = line.Length > args[0].Length ? line[(args[0].Length + 1)..] : string.Empty;
        console.AvailableCommands[args[0]].Execute(shell, argStr, args.Skip(1).ToArray());
    }

    /// <summary>A server shell that keeps errors for the test instead of failing on them.</summary>
    private sealed class RecordingShell(IConsoleHost host) : IConsoleShell
    {
        public readonly List<string> Errors = new();

        public IConsoleHost ConsoleHost => host;
        public bool IsLocal => true;
        public bool IsServer => true;
        public ICommonSession? Player => null;

        public void ExecuteCommand(string command) => host.ExecuteCommand(null, command);
        public void RemoteExecuteCommand(string command) { }
        public void WriteLine(string text) { }
        public void WriteLine(FormattedMessage message) { }
        public void WriteError(string text) => Errors.Add(text);
        public void Clear() { }
    }

    private static WFPlanetWeatherComponent Network(IEntityManager entMan, List<EntityUid> layers)
    {
        return entMan.GetComponent<WFPlanetWeatherComponent>(entMan.GetComponent<CEZMapComponent>(layers[0]).NetworkUid);
    }

    /// <summary>Lays a square of open weather ground on a test grid, centred on its origin.</summary>
    private static Entity<MapGridComponent> LayOpenGround(TestPair pair, Entity<MapGridComponent> grid)
    {
        var maps = pair.Server.System<SharedMapSystem>();
        var tile = new Tile(pair.Server.ResolveDependency<ITileDefinitionManager>()[OpenTile].TileId);
        var tiles = new List<(Vector2i, Tile)>();
        for (var x = -Half; x <= Half; x++)
        {
            for (var y = -Half; y <= Half; y++)
                tiles.Add((new Vector2i(x, y), tile));
        }

        maps.SetTiles(grid.Owner, grid.Comp, tiles);
        return grid;
    }
}
