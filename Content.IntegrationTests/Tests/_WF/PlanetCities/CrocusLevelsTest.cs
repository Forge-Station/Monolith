#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.IntegrationTests.Pair;
using Content.IntegrationTests.Tests._WF.Planets;
using Content.Server._CE.ZLevels.Core.Components;
using Content.Server._WF.PlanetCities;
using Content.Server._WF.PlanetPois;
using Content.Server.Parallax;
using Content.Shared.Parallax.Biomes;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._WF.PlanetCities;
using Content.Shared._WF.PlanetPois;
using Content.Shared.CCVar;
using Content.Shared.Maps;
using Robust.Shared;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._WF.PlanetCities;

/// <summary>
/// Every level of Crocus stands whole on its layer, as much of it as its map file has, nothing of it left paused or
/// uninitialised, and a body walks its stairs from the street up to the level above and down to the one below.
/// </summary>
[TestFixture]
[TestOf(typeof(WFPlanetCitySystem))]
public sealed class CrocusLevelsTest
{
    private const string Mob = "MobHuman";

    /// <summary>A biome chunk's side, in tiles.</summary>
    private const int Chunk = 8;

    /// <summary>How far a walking mob is moved between looks, in tiles.</summary>
    private const float Stride = 0.05f;

    /// <summary>The share of a level file's entities that must stand on its layer; the rest are swapped out or cut away.</summary>
    private const float Whole = 0.95f;

    /// <summary>The share underground, where whatever stood on the city's own rock goes with it.</summary>
    private const float Underground = 0.9f;

    [TestCase(false)]
    [TestCase(true)]
    public async Task CrocusLevelsAreWholeAndClimbable(bool caverns)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true, ServerSeed = 1977 });
        var server = pair.Server;
        var entMan = server.EntMan;
        var prototypes = server.ResolveDependency<IPrototypeManager>();
        await PlanetFixture.EnableFeature(pair);

        // Grid splitting as a live server has it; a dev build turns it off.
        var (layers, lower, holds) = await FixedCityTest.BuildCrocus(pair, caverns, () =>
        {
            server.CfgMan.SetCVar(CCVars.NPCEnabled, false);
            server.CfgMan.SetCVar(CVars.GridSplitting, true);
        });
        // Long enough for anything a build left waiting to come due.
        await pair.RunTicksSync(30);

        var levels = new Dictionary<int, EntityUid>();
        Box2i bounds = default;
        await server.WaitAssertion(() =>
        {
            var ground = layers[0];
            var record = entMan.GetComponent<WFPlanetPoiComponent>(ground);
            Assert.That(record.GenerationError, Is.Null);
            Assert.That(holds.All(hold => hold.IsCompletedSuccessfully), Is.True);
            bounds = record.Sites.Single(site => site.Name == "wf-city-crocus").Bounds;

            levels[0] = ground;
            for (var depth = 1; depth < layers.Count; depth++)
            {
                if (FixedCityTest.Grids(entMan, layers[depth]).FirstOrDefault(entMan.HasComponent<WFPlanetCityGridComponent>) is { Valid: true } grid)
                    levels[depth] = grid;
            }

            levels[-1] = entMan.HasComponent<MapGridComponent>(lower[0])
                ? lower[0]
                : FixedCityTest.Grids(entMan, lower[0]).Single(entMan.HasComponent<WFPlanetCityGridComponent>);

            // The street's chunks load as the preload loads them on a live world, growing ground on what the city left.
            var biomes = server.System<BiomeSystem>();
            Entity<BiomeComponent, MapGridComponent> world = (ground, entMan.GetComponent<BiomeComponent>(ground), entMan.GetComponent<MapGridComponent>(ground));
            for (var x = (int) MathF.Floor(bounds.Left / (float) Chunk) * Chunk; x < bounds.Right; x += Chunk)
            for (var y = (int) MathF.Floor(bounds.Bottom / (float) Chunk) * Chunk; y < bounds.Top; y += Chunk)
            {
                biomes.WfLoadChunk(world, new Vector2i(x, y));
            }
        });
        await pair.RunTicksSync(5);

        // Each level against its own map file: its tiles, and its entities less those the city swaps out for nothing.
        var city = prototypes.Index<WFFixedCityPrototype>("WFCityCrocus");
        var gameMap = prototypes.Index<GameMapPrototype>(city.GameMap);
        var factory = server.ResolveDependency<IComponentFactory>();
        Assert.That(gameMap.Stations[city.Station].StationComponentOverrides.TryGetComponent<CEStationZLevelsComponent>(factory, out var zLevels), Is.True);
        var paths = new Dictionary<int, ResPath> { [0] = gameMap.MapPath, [-1] = zLevels!.MapsBelow.Last() };
        for (var i = 0; i < zLevels.MapsAbove.Count; i++)
        {
            paths[i + 1] = zLevels.MapsAbove[i];
        }

        var origin = Vector2i.Zero;
        await server.WaitAssertion(() =>
        {
            origin = entMan.GetComponent<WFPlanetPoiComponent>(layers[0]).Sites.Single(site => site.Name == "wf-city-crocus").Origin;
        });

        var problems = new List<string>();
        foreach (var (depth, path) in paths.OrderBy(entry => entry.Key))
        {
            var expected = await CountFile(pair, path, city);
            await server.WaitAssertion(() =>
            {
                if (!levels.TryGetValue(depth, out var level))
                {
                    problems.Add($"Level {depth} of Crocus was never built.");
                    return;
                }

                var grid = entMan.GetComponent<MapGridComponent>(level);
                var tiles = 0;
                var enumerator = server.System<SharedMapSystem>().GetAllTilesEnumerator(level, grid);
                while (enumerator.MoveNext(out var tile))
                {
                    if (Contains(bounds, tile.Value.GridIndices))
                        tiles++;
                }

                var entities = 0;
                var paused = new List<string>();
                foreach (var child in FixedCityTest.Children(entMan, level))
                {
                    if (!Contains(bounds, entMan.GetComponent<TransformComponent>(child).LocalPosition.Floored()))
                        continue;

                    entities++;
                    var meta = entMan.GetComponent<MetaDataComponent>(child);
                    if (meta.EntityPaused || meta.EntityLifeStage != EntityLifeStage.MapInitialized)
                        paused.Add($"{meta.EntityPrototype?.ID} ({meta.EntityLifeStage}{(meta.EntityPaused ? ", paused" : "")})");
                }

                TestContext.Out.WriteLine($"Level {depth} ({path}): {tiles} of {expected.Tiles} tiles, {entities} of {expected.Entities} entities, {paused.Count} paused.");
                if (entMan.GetComponent<MetaDataComponent>(level).EntityPaused)
                    problems.Add($"Level {depth}'s grid is paused.");
                if (paused.Count > 0)
                    problems.Add($"Level {depth} has {paused.Count} entities paused or not map-initialised: {string.Join(", ", paused.Take(10))}");
                // The street is cut by the entrances; below it the world's own rock stands in for the city's, and what
                // stood on that rock goes with it.
                if (depth > 0 && tiles != expected.Tiles || depth == 0 && tiles < expected.Tiles * Whole)
                    problems.Add($"Level {depth} has {tiles} of its {expected.Tiles} tiles.");
                if (entities < (int) (expected.Entities * (depth < 0 ? Underground : Whole)))
                    problems.Add($"Level {depth} has {entities} of its {expected.Entities} entities.");
            });
        }

        Assert.That(problems, Is.Empty, string.Join("\n", problems));

        // Up from the street onto the level above and back, then down to the level below and back.
        var (upFoot, upTop) = await FindStairs(pair, levels[0], levels[1], bounds);
        var mob = await SpawnSettled(pair, layers[0], upFoot);
        await AssertWalk(pair, mob, upFoot, upTop, layers[1], "Climbing from the street");
        await AssertWalk(pair, mob, upTop, upFoot, layers[0], "Coming down to the street");
        await server.WaitPost(() => entMan.DeleteEntity(mob));

        var (downFoot, downTop) = await FindStairs(pair, levels[-1], levels[0], bounds);
        mob = await SpawnSettled(pair, layers[0], downTop);
        await AssertWalk(pair, mob, downTop, downFoot, lower[0], "Going down from the street");
        await AssertWalk(pair, mob, downFoot, downTop, layers[0], "Climbing back to the street");

        await PlanetFixture.Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    /// <summary>A level's map file loaded on its own map: its tiles, and its entities the city keeps.</summary>
    private static async Task<(int Tiles, int Entities)> CountFile(TestPair pair, ResPath path, WFFixedCityPrototype city)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var result = (Tiles: 0, Entities: 0);
        await server.WaitPost(() =>
        {
            var loader = server.System<MapLoaderSystem>();
            var options = new DeserializationOptions { InitializeMaps = false };
            Assert.That(loader.TryLoadMap(path, out var map, out _, options), Is.True, $"Cannot load {path}.");
            var grid = entMan.GetComponent<MapGridComponent>(map!.Value);
            var enumerator = server.System<SharedMapSystem>().GetAllTilesEnumerator(map.Value, grid);
            while (enumerator.MoveNext(out var t))
            {
                result.Tiles++;
            }

            foreach (var child in FixedCityTest.Children(entMan, map.Value))
            {
                var id = entMan.GetComponent<MetaDataComponent>(child).EntityPrototype?.ID;
                if (id != null && (city.Terrain.Contains(id) || city.Replace.TryGetValue(id, out var swap) && swap == null))
                    continue;

                result.Entities++;
            }

            entMan.DeleteEntity(map.Value);
        });

        return result;
    }

    /// <summary>
    /// Stairs on a level whose top comes out on floor of the level above, clear to walk: where a walk starts at their
    /// foot and where it ends past their top.
    /// </summary>
    private static async Task<(Vector2 Foot, Vector2 Top)> FindStairs(TestPair pair, EntityUid level, EntityUid above, Box2i bounds)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var maps = server.System<SharedMapSystem>();
        (Vector2, Vector2)? found = null;
        await server.WaitAssertion(() =>
        {
            var levelGrid = entMan.GetComponent<MapGridComponent>(level);
            var aboveGrid = entMan.GetComponent<MapGridComponent>(above);
            var levelOffset = server.System<SharedTransformSystem>().GetWorldPosition(level);
            var candidates = new List<(Vector2i Tile, Vector2i Climb)>();
            foreach (var child in FixedCityTest.Children(entMan, level))
            {
                if (!entMan.TryGetComponent<CEZLevelHighGroundComponent>(child, out var high) || high.Corner || !high.Stick)
                    continue;

                var xform = entMan.GetComponent<TransformComponent>(child);
                var tile = xform.LocalPosition.Floored();
                if (!xform.Anchored || !Contains(bounds, tile))
                    continue;

                // Stairs rise against the way they face.
                var climb = -xform.LocalRotation.GetCardinalDir().ToIntVec();
                candidates.Add((tile, climb));
            }

            Assert.That(candidates, Is.Not.Empty, $"No stairs on {entMan.ToPrettyString(level)}.");
            foreach (var (tile, climb) in candidates.OrderBy(candidate => candidate.Tile.X).ThenBy(candidate => candidate.Tile.Y))
            {
                // A straight run: the tile behind the foot is floor on this level, and past the top floor on the next.
                if (!Walkable(entMan, maps, (level, levelGrid), tile - climb)
                    || !Walkable(entMan, maps, (above, aboveGrid), tile + climb)
                    || !Walkable(entMan, maps, (above, aboveGrid), tile + climb * 2)
                    || !maps.GetTileRef(above, aboveGrid, tile).Tile.IsEmpty
                    || HasHighGround(entMan, maps, (level, levelGrid), tile - climb))
                    continue;

                var centre = (Vector2) tile + new Vector2(0.5f) + levelOffset;
                found = (centre - (Vector2) climb, centre + (Vector2) climb * 2);
                break;
            }

            Assert.That(found, Is.Not.Null, $"None of the {candidates.Count} stairs on {entMan.ToPrettyString(level)} come out on floor above.");
        });

        return found!.Value;
    }

    private static bool Walkable(IEntityManager entMan, SharedMapSystem maps, Entity<MapGridComponent> grid, Vector2i tile)
    {
        if (maps.GetTileRef(grid.Owner, grid.Comp, tile).Tile.IsEmpty)
            return false;

        return !maps.GetAnchoredEntities(grid, tile).Any(uid =>
            entMan.TryGetComponent<PhysicsComponent>(uid, out var body) && body.Hard && body.CanCollide
            || entMan.HasComponent<CEZLevelHighGroundComponent>(uid));
    }

    private static bool HasHighGround(IEntityManager entMan, SharedMapSystem maps, Entity<MapGridComponent> grid, Vector2i tile)
    {
        return maps.GetAnchoredEntities(grid, tile).Any(entMan.HasComponent<CEZLevelHighGroundComponent>);
    }

    /// <summary>Spawns a mob and gives it a second to settle.</summary>
    private static async Task<EntityUid> SpawnSettled(TestPair pair, EntityUid map, Vector2 position)
    {
        var server = pair.Server;
        var mob = EntityUid.Invalid;
        await server.WaitPost(() => mob = server.EntMan.SpawnEntity(Mob, new EntityCoordinates(map, position)));
        await server.WaitRunTicks(pair.SecondsToTicks(1f));
        return mob;
    }

    /// <summary>Moves a mob a stride at a time, two ticks a stride, and asserts it ends on a map at rest.</summary>
    private static async Task AssertWalk(TestPair pair, EntityUid mob, Vector2 from, Vector2 to, EntityUid map, string what)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var transform = server.System<SharedTransformSystem>();
        var strides = (int) MathF.Round((to - from).Length() / Stride);
        for (var i = 0; i <= strides; i++)
        {
            var position = Vector2.Lerp(from, to, (float) i / strides);
            await server.WaitPost(() => transform.SetWorldPosition(mob, position));
            await server.WaitRunTicks(2);
        }

        await server.WaitRunTicks(pair.SecondsToTicks(1f));
        await server.WaitAssertion(() =>
        {
            var xform = entMan.GetComponent<TransformComponent>(mob);
            var physics = entMan.GetComponent<CEZPhysicsComponent>(mob);
            Assert.That(xform.MapUid, Is.EqualTo(map), $"{what} ended on {entMan.ToPrettyString(xform.MapUid)}, at {transform.GetWorldPosition(xform)}.");
            Assert.That(physics.Velocity, Is.Zero, $"{what} left the mob moving.");
        });
    }

    private static bool Contains(Box2i box, Vector2i tile) =>
        tile.X >= box.Left && tile.X < box.Right && tile.Y >= box.Bottom && tile.Y < box.Top;
}
