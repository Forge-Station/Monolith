#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Tests._WF.Planets;
using Content.Server._WF.PlanetCities;
using Content.Server._WF.PlanetPois;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Body.Components;
using Content.Server.Atmos.Components;
using Content.Shared.Atmos;
using Content.Shared.CCVar;
using Content.Shared.Light.Components;
using Content.Shared.Light.EntitySystems;
using Content.Shared._WF.PlanetCities;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;

namespace Content.IntegrationTests.Tests._WF.PlanetCities;

/// <summary>Crocus's towers breathe Asclepiu's air, indoors and out, like its street; orbit above stays a vacuum.</summary>
[TestFixture]
[TestOf(typeof(WFPlanetCitySystem))]
public sealed class CrocusAirTest
{
    private const string Mob = "MobHuman";

    /// <summary>The share of a level's floor that may sit below half the planet's pressure: a mapped canister or the like.</summary>
    private const float Leeway = 0.01f;

    [Test]
    public async Task CrocusUpperLevelsHaveAir()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true, ServerSeed = 1977 });
        var server = pair.Server;
        var entMan = server.EntMan;
        var atmos = server.System<AtmosphereSystem>();
        var maps = server.System<SharedMapSystem>();
        var roofs = server.System<SharedRoofSystem>();
        await PlanetFixture.EnableFeature(pair);

        var (layers, _, holds) = await FixedCityTest.BuildCrocus(pair, false, () => server.CfgMan.SetCVar(CCVars.NPCEnabled, false));

        // A mob on roofed, open floor on each level above the street, and on the street itself.
        var mobs = new Dictionary<int, EntityUid>();
        var levels = new Dictionary<int, EntityUid>();
        Box2i bounds = default;
        await server.WaitAssertion(() =>
        {
            var record = entMan.GetComponent<WFPlanetPoiComponent>(layers[0]);
            Assert.That(record.GenerationError, Is.Null);
            Assert.That(holds.All(hold => hold.IsCompletedSuccessfully), Is.True);
            bounds = record.Sites.Single(site => site.Name == "wf-city-crocus").Bounds;

            levels[0] = layers[0];
            for (var depth = 1; depth < layers.Count - 1; depth++)
            {
                levels[depth] = FixedCityTest.Grids(entMan, layers[depth]).Single(entMan.HasComponent<WFPlanetCityGridComponent>);
            }

            foreach (var (depth, level) in levels)
            {
                var grid = entMan.GetComponent<MapGridComponent>(level);
                var roof = entMan.GetComponent<RoofComponent>(level);
                // Indoors where the level has a roof; the top level is open to the sky.
                Vector2i? spot = null;
                var tiles = maps.GetAllTilesEnumerator(level, grid);
                while (tiles.MoveNext(out var tile))
                {
                    var index = tile.Value.GridIndices;
                    if (!Contains(bounds, index) || Blocked(entMan, maps, (level, grid), index))
                        continue;

                    spot ??= index;
                    if (!roofs.IsRooved((level, grid, roof), index))
                        continue;

                    spot = index;
                    break;
                }

                Assert.That(spot, Is.Not.Null, $"No open floor on level {depth}.");
                mobs[depth] = entMan.SpawnEntity(Mob, maps.GridTileToLocal(level, grid, spot!.Value));
                TestContext.Out.WriteLine($"Mob on level {depth} at {spot}, {(roofs.IsRooved((level, grid, roof), spot.Value) ? "indoors" : "outdoors")}.");
            }
        });

        await server.WaitRunTicks(pair.SecondsToTicks(10f));

        await server.WaitAssertion(() =>
        {
            var orbit = layers[^1];
            Assert.That(atmos.GetTileMixture(null, orbit, Vector2i.Zero)!.Pressure, Is.LessThan(1f), "Asclepiu's orbit has air.");

            var problems = new List<string>();
            foreach (var (depth, level) in levels.OrderBy(pair => pair.Key))
            {
                var map = entMan.GetComponent<TransformComponent>(level).MapUid!.Value;
                var planet = atmos.GetTileMixture(null, map, Vector2i.Zero)!.Pressure;
                var grid = entMan.GetComponent<MapGridComponent>(level);
                var roof = entMan.GetComponent<RoofComponent>(level);
                var (indoors, outdoors, thin) = (0, 0, new List<Vector2i>());
                var tiles = maps.GetAllTilesEnumerator(level, grid);
                while (tiles.MoveNext(out var tile))
                {
                    var index = tile.Value.GridIndices;
                    if (!Contains(bounds, index) || Blocked(entMan, maps, (level, grid), index))
                        continue;

                    if (roofs.IsRooved((level, grid, roof), index))
                        indoors++;
                    else
                        outdoors++;

                    if ((atmos.GetTileMixture(level, map, index)?.Pressure ?? 0f) < planet * 0.5f)
                        thin.Add(index);
                }

                TestContext.Out.WriteLine($"Level {depth}: planet air {planet:F1} kPa; {thin.Count} of {indoors} indoor and {outdoors} outdoor floor tiles under half of it.");
                if (planet < Atmospherics.OneAtmosphere * 0.5f)
                    problems.Add($"Level {depth}'s layer has {planet:F1} kPa of air.");
                if (thin.Count > (indoors + outdoors) * Leeway)
                    problems.Add($"Level {depth} has {thin.Count} floor tiles under half the planet's pressure, e.g. {string.Join(", ", thin.Take(5))}.");

                var mob = mobs[depth];
                if (entMan.GetComponent<BarotraumaComponent>(mob).TakingDamage)
                    problems.Add($"The mob on level {depth} takes pressure damage.");
                if (entMan.GetComponent<RespiratorComponent>(mob).SuffocationCycles > 0)
                    problems.Add($"The mob on level {depth} suffocates.");
            }

            Assert.That(problems, Is.Empty, string.Join("\n", problems));
        });

        await PlanetFixture.Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    /// <summary>Whether something hard stands on a tile: a wall, a window, a machine.</summary>
    private static bool Blocked(IEntityManager entMan, SharedMapSystem maps, Entity<MapGridComponent> grid, Vector2i tile)
    {
        return maps.GetAnchoredEntities(grid, tile).Any(uid =>
            entMan.TryGetComponent<PhysicsComponent>(uid, out var body) && body.Hard && body.CanCollide);
    }

    private static bool Contains(Box2i box, Vector2i tile) =>
        tile.X >= box.Left && tile.X < box.Right && tile.Y >= box.Bottom && tile.Y < box.Top;
}
