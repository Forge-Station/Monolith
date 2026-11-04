using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.Server._CE.ZLevels.Core.Components;
using Content.Server._WF.Caverns;
using Content.Server._WF.PlanetCities;
using Content.Server._WF.Planets;
using Content.Server.Station.Systems;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.PlanetCities;
using Content.Shared.Maps;
using Content.Shared._WF.PlanetPois;
using Content.Shared.Damage.Components;
using Content.Shared.Light.Components;
using Content.Shared.Parallax.Biomes;
using Content.Shared.Station;
using Robust.Shared.EntitySerialization;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Markdown;
using Robust.Shared.Serialization.Markdown.Mapping;
using Robust.Shared.Serialization.Markdown.Sequence;
using Robust.Shared.Serialization.Markdown.Value;
using Robust.Shared.Utility;

namespace Content.Server._WF.PlanetPois;

/// <summary>Stamps fixed multi-level cities onto a world ahead of its other sites, and makes their stations.</summary>
public sealed partial class WFPlanetPoiSystem
{
    [Dependency] private IComponentFactory _factory = default!;
    [Dependency] private StationSystem _station = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;

    /// <summary>What a level's map entity keeps when it is loaded as a plain grid; the rest is the map's own.</summary>
    private static readonly HashSet<string> CityGridComponents = new() { "MetaData", "Transform", "MapGrid", "Broadphase", "DecalGrid", "Roof" };

    /// <summary>One level of a city, loaded on its own staging map.</summary>
    /// <param name="Deferred">The bare entities left out of the load, spawned straight onto the world once the rest is merged.</param>
    /// <param name="Paved">Tiles of an entrance road, where nothing solid of <paramref name="Deferred"/> may stand.</param>
    private sealed record CityLevel(int Depth, ResPath Path, EntityUid Staging, Entity<MapGridComponent> Grid, List<DeferredSpawn> Deferred, HashSet<Vector2i> Paved);

    /// <summary>A city with its spot on the world held: its station, its entry for placement and roads, and its footprint.</summary>
    private sealed record CityPlan(WFFixedCityPrototype City, StationConfig Config, WFPlanetPoiEntry Entry, Box2i Footprint, Vector2i Origin, Box2i Reservation, List<Vector2i> Approaches);

    /// <summary>Resumes on a later tick, so a city is built a level at a time rather than in one long tick.</summary>
    private Task NextTick() => _preload.NextTick();

    /// <summary>
    /// Places every fixed city of this world. Each one's spot is held at once, before the preload's other claims such
    /// as cavern mouths, by pinning its whole footprint; the levels are then built a tick at a time and the footprint
    /// tiles the city doesn't use are let go. A city that finds no room or can't be loaded is left out.
    /// </summary>
    private async Task PlaceCitiesAsync(
        EntityUid ground,
        MapGridComponent grid,
        BiomeComponent biome,
        WFPlanetNetworkComponent network,
        EntityUid orbit,
        Vector2 centre,
        float radius,
        WFPlanetPoiTablePrototype table,
        WFPlanetPoiComponent record,
        List<Box2i> reserved,
        HashSet<Vector2i> initialPins,
        HashSet<EntityUid> created,
        List<uint> createdDecals,
        HashSet<Vector2i> changed,
        Task claimed)
    {
        if (!_cfg.GetCVar(PlanetCVars.FixedCities))
            return;

        var plans = new List<CityPlan>();
        foreach (var city in _proto.EnumeratePrototypes<WFFixedCityPrototype>().OrderBy(city => city.ID))
        {
            if (city.Surface == network.Surface && ReserveCity(city, ground, biome, centre, radius, table, reserved) is { } plan)
                plans.Add(plan);
        }

        // The footprints are held; what a city leaves of its footprint is let go only once the claims round it are in.
        await claimed;
        foreach (var plan in plans)
        {
            var levels = new List<CityLevel>();
            var built = new HashSet<Vector2i>();
            try
            {
                if (await StageCityAsync(plan, network, levels) && !TerminatingOrDeleted(ground))
                {
                    built = await BuildCityAsync(plan, levels, ground, grid, network, orbit, centre, radius,
                        table, record, initialPins, created, createdDecals, changed);
                }
            }
            finally
            {
                foreach (var level in levels)
                {
                    if (!TerminatingOrDeleted(level.Staging))
                        QueueDel(level.Staging);
                }

                // The biome grows its own terrain on the footprint tiles the city left empty.
                if (!TerminatingOrDeleted(ground))
                    _biome.WfUnpinTiles((ground, biome), Tiles(plan.Reservation).Concat(plan.Approaches).Where(tile => !built.Contains(tile) && !initialPins.Contains(tile)));
            }

            if (built.Count == 0)
                reserved.Remove(PlacementBounds(plan.Reservation, plan.Entry, table.Roads));
        }
    }

    /// <summary>Finds the city a spot and pins its whole footprint, so nothing else claims it while it loads.</summary>
    private CityPlan? ReserveCity(
        WFFixedCityPrototype city,
        EntityUid ground,
        BiomeComponent biome,
        Vector2 centre,
        float radius,
        WFPlanetPoiTablePrototype table,
        List<Box2i> reserved)
    {
        var gameMap = _proto.Index<GameMapPrototype>(city.GameMap);
        if (!gameMap.Stations.TryGetValue(city.Station, out var config))
        {
            Log.Error($"{city.ID}: game map {city.GameMap} has no station {city.Station}.");
            return null;
        }

        var footprint = city.Footprint;
        var sockets = new List<WFPlanetPoiSocket>();
        foreach (var entrance in city.Entrances)
        {
            if (!Contains(footprint, entrance.Start))
            {
                Log.Error($"{city.ID}: the entrance at {entrance.Start} is outside the city's footprint.");
                continue;
            }

            var direction = Direction(entrance.Direction);
            var edge = entrance.Start;
            while (Contains(footprint, edge + direction))
            {
                edge += direction;
            }

            sockets.Add(new WFPlanetPoiSocket { Offset = edge, Direction = entrance.Direction });
        }

        var entry = new WFPlanetPoiEntry
        {
            MapPath = gameMap.MapPath.ToString(),
            Footprint = footprint,
            Name = city.Name,
            Expeditions = false,
            Sockets = sockets,
            Connection = sockets.Count switch
            {
                0 => WFPlanetPoiConnection.Isolated,
                1 => WFPlanetPoiConnection.Roadside,
                _ => WFPlanetPoiConnection.Through,
            },
        };

        if (!TryOrigin((ground, biome), centre, radius, table, entry, footprint, reserved, out var origin))
        {
            Log.Warning($"No room for {city.ID} on {ToPrettyString(ground)}; the city is left out.");
            return null;
        }

        var reservation = Translate(footprint, origin);
        reserved.Add(PlacementBounds(reservation, entry, table.Roads));
        // The road out of each entrance is held too, so a cavern mouth can't open across it before the roads are laid.
        var approaches = new List<Vector2i>();
        if (table.Roads is { } roads)
        {
            var clearance = roads.Width / 2 + roads.ShoulderWidth + 1;
            foreach (var socket in sockets)
            {
                var direction = Direction(socket.Direction);
                for (var step = 1; step <= clearance * 2 + 1; step++)
                for (var side = -clearance; side <= clearance; side++)
                {
                    var tile = origin + socket.Offset + direction * step + new Vector2i(-direction.Y, direction.X) * side;
                    if (!Contains(reservation, tile))
                        approaches.Add(tile);
                }
            }
        }

        _biome.WfPinTiles((ground, biome), Tiles(reservation).Concat(approaches).ToList());
        return new CityPlan(city, config, entry, footprint, origin, reservation, approaches);
    }

    /// <summary>Loads every level the world has a layer for, swaps and clears what the city asks and cuts its entrances.</summary>
    private async Task<bool> StageCityAsync(CityPlan plan, WFPlanetNetworkComponent network, List<CityLevel> levels)
    {
        var city = plan.City;
        var gameMap = _proto.Index<GameMapPrototype>(city.GameMap);
        var paths = new List<(int Depth, ResPath Path)>();
        plan.Config.StationComponentOverrides.TryGetComponent<CEStationZLevelsComponent>(_factory, out var zLevels);
        var below = zLevels?.MapsBelow ?? new List<ResPath>();
        for (var i = 0; i < below.Count; i++)
        {
            paths.Add((i - below.Count, below[i]));
        }

        paths.Add((0, gameMap.MapPath));
        var above = zLevels?.MapsAbove ?? new List<ResPath>();
        for (var i = 0; i < above.Count; i++)
        {
            paths.Add((i + 1, above[i]));
        }

        var airLayers = _proto.Index(network.Surface).AirLayers;
        foreach (var (depth, path) in paths)
        {
            if (depth > airLayers || -depth > network.LowerLayers.Count)
            {
                Log.Warning($"{city.ID}: {ToPrettyString(network.GroundMap)} has no layer at depth {depth}, so {path} is left out.");
                continue;
            }

            // Under the street, a layer with terrain of its own grows the rock itself.
            var strip = depth < 0 && HasComp<BiomeComponent>(network.LowerLayers[-depth - 1]) ? city.Terrain : null;
            if (await LoadCityLevelAsync(depth, path, strip) is not { } level)
            {
                Log.Error($"{city.ID}: cannot load {path}.");
                return false;
            }

            levels.Add(level);
            await NextTick();
            await PrepareCityLevelAsync(city, level, plan.Footprint);
            await NextTick();
            if (TerminatingOrDeleted(network.GroundMap))
                return false;
        }

        if (levels.FirstOrDefault(level => level.Depth == 0) is not { } groundLevel)
            return false;

        foreach (var entrance in city.Entrances)
        {
            CutEntrance(groundLevel.Grid, plan.Footprint, entrance, groundLevel.Paved);
        }

        return true;
    }

    private static IEnumerable<Vector2i> Tiles(Box2i box)
    {
        for (var x = box.Left; x < box.Right; x++)
        for (var y = box.Bottom; y < box.Top; y++)
        {
            yield return new Vector2i(x, y);
        }
    }

    /// <summary>
    /// Loads a level's map file onto a staging map as an ordinary grid, without map-initialising it. Entities of the
    /// <paramref name="strip"/> prototypes are left out, and so are the tiles they stood on.
    /// </summary>
    private async Task<CityLevel?> LoadCityLevelAsync(int depth, ResPath path, HashSet<EntProtoId>? strip)
    {
        if (await ReadMapAsync(path) is not { } data)
            return null;

        var stripped = new List<Vector2i>();
        if (strip is { Count: > 0 } && data.TryGet<SequenceDataNode>("entities", out var groups))
        {
            for (var i = groups.Count - 1; i >= 0; i--)
            {
                var group = (MappingDataNode) groups[i];
                if (!group.TryGet<ValueDataNode>("proto", out var proto) || !strip.Contains(proto.Value))
                    continue;

                foreach (var entity in group.Get<SequenceDataNode>("entities"))
                {
                    foreach (var component in ((MappingDataNode) entity).Get<SequenceDataNode>("components"))
                    {
                        var node = (MappingDataNode) component;
                        if (node.Get<ValueDataNode>("type").Value == "Transform" && node.TryGet<ValueDataNode>("pos", out var pos))
                        {
                            var xy = pos.Value.Split(',');
                            stripped.Add(new Vector2(Parse.Float(xy[0]), Parse.Float(xy[1])).Floored());
                        }
                    }
                }

                groups.RemoveAt(i);
            }
        }

        // A level is saved as a planet map whose map entity is the grid; the loader can't merge that onto another map.
        if (data.TryGet<MappingDataNode>("meta", out var meta) && data.TryGet<SequenceDataNode>("maps", out var maps) && maps.Count > 0)
        {
            var mapUid = ((ValueDataNode) maps[0]).Value;
            meta.Remove("category");
            meta.Add("category", new ValueDataNode(nameof(FileCategory.Grid)));
            data.Remove("maps");
            data.Add("maps", new SequenceDataNode());
            data.Remove("orphans");
            data.Add("orphans", new SequenceDataNode(mapUid));
            foreach (var group in data.Get<SequenceDataNode>("entities"))
            {
                foreach (var entity in ((MappingDataNode) group).Get<SequenceDataNode>("entities"))
                {
                    var node = (MappingDataNode) entity;
                    if (node.Get<ValueDataNode>("uid").Value != mapUid || !node.TryGet<SequenceDataNode>("components", out var components))
                        continue;

                    var kept = new SequenceDataNode();
                    foreach (var component in components)
                    {
                        if (CityGridComponents.Contains(((MappingDataNode) component).Get<ValueDataNode>("type").Value))
                            kept.Add(component);
                    }

                    node.Remove("components");
                    node.Add("components", kept);
                }
            }
        }

        // A level's buildings stand apart from each other; loaded as a grid that may split, all but one would be lost.
        KeepWhole(data);
        // The bare walls, rock, cables and water are spawned after the load, within the budget, not in one tick with it.
        var deferred = (data.TryGet<SequenceDataNode>("orphans", out var roots) && roots.Count == 1
                || data.TryGet("grids", out roots) && roots.Count == 1)
            ? await Task.Run(() => TakePlainEntities(data, ((ValueDataNode) roots[0]).Value))
            : new List<DeferredSpawn>();
        var staging = _maps.CreateMap(out var stagingId, runMapInit: false);
        var options = new MapLoadOptions { MergeMap = stagingId, ExpectedCategory = FileCategory.Grid };
        LoadResult? result;
        bool loaded;
        using (_preload.Time($"loading city level {path}, {deferred.Count} spawned after"))
            loaded = _loader.TryLoadGeneric(data, path.ToString(), out result, options);
        if (!loaded || result!.Grids.Count != 1)
        {
            QueueDel(staging);
            return null;
        }

        var grid = result.Grids.Single();
        if (stripped.Count > 0)
            _maps.SetTiles(grid.Owner, grid.Comp, stripped.Select(tile => (tile, Tile.Empty)).ToList());

        return new CityLevel(depth, path, staging, grid, deferred, new HashSet<Vector2i>());
    }

    /// <summary>Swaps or removes the prototypes the city lists, drops anything standing off the tiles and strips godmode.</summary>
    private async Task PrepareCityLevelAsync(WFFixedCityPrototype city, CityLevel level, Box2i footprint)
    {
        var grid = level.Grid;
        if (city.ReplaceTiles.Count > 0)
        {
            var swaps = city.ReplaceTiles.ToDictionary(pair => (int) _tiles[pair.Key].TileId, pair => (int) _tiles[pair.Value].TileId);
            var swapped = new List<(Vector2i, Tile)>();
            var all = _maps.GetAllTilesEnumerator(grid.Owner, grid.Comp);
            while (all.MoveNext(out var tileRef))
            {
                if (swaps.TryGetValue(tileRef.Value.Tile.TypeId, out var swap))
                    swapped.Add((tileRef.Value.GridIndices, new Tile(swap)));
            }

            _maps.SetTiles(grid.Owner, grid.Comp, swapped);
        }

        var outside = LevelTiles(grid).Where(tile => !Contains(footprint, tile)).ToList();
        if (outside.Count > 0)
        {
            Log.Warning($"{city.ID}: {level.Path} has {outside.Count} tiles outside the city's footprint; they are left out.");
            _maps.SetTiles(grid.Owner, grid.Comp, outside.Select(tile => (tile, Tile.Empty)).ToList());
        }

        for (var i = level.Deferred.Count - 1; i >= 0; i--)
        {
            var spawn = level.Deferred[i];
            var tile = spawn.Position.Floored();
            if (!Contains(footprint, tile) || _maps.GetTileRef(grid.Owner, grid.Comp, tile).Tile.IsEmpty)
            {
                level.Deferred.RemoveAt(i);
                continue;
            }

            if (city.Replace.TryGetValue(spawn.Proto, out var swap))
            {
                if (swap is { } replacement)
                    level.Deferred[i] = spawn with { Proto = replacement };
                else
                    level.Deferred.RemoveAt(i);
            }
        }

        var children = new List<EntityUid>();
        var enumerator = Transform(grid).ChildEnumerator;
        while (enumerator.MoveNext(out var child))
        {
            children.Add(child);
        }

        foreach (var child in children)
        {
            // The level is paused on its staging map, so it can wait a tick between pieces.
            await _preload.Breathe();
            if (TerminatingOrDeleted(child))
                continue;
            var xform = Transform(child);
            if (_maps.GetTileRef(grid.Owner, grid.Comp, xform.Coordinates).Tile.IsEmpty)
            {
                Del(child);
                continue;
            }

            if (MetaData(child).EntityPrototype?.ID is { } id && city.Replace.TryGetValue(id, out var replacement))
            {
                if (replacement is { } swap)
                {
                    var spawned = Spawn(swap, xform.Coordinates);
                    _transform.SetLocalRotation(spawned, xform.LocalRotation);
                }
                Del(child);
                continue;
            }

            if (city.StripGodmode)
                RemComp<GodmodeComponent>(child);
        }
    }

    /// <summary>Paves a straight road from the start tile to the footprint's edge, clearing what blocks it.</summary>
    private void CutEntrance(Entity<MapGridComponent> grid, Box2i footprint, WFFixedCityEntrance entrance, HashSet<Vector2i> paved)
    {
        if (!Contains(footprint, entrance.Start) || entrance.Width < 1 || entrance.Width % 2 == 0)
        {
            Log.Error($"Entrance at {entrance.Start} is outside its city or has an even width.");
            return;
        }

        var direction = Direction(entrance.Direction);
        var side = new Vector2i(-direction.Y, direction.X);
        var tile = new Tile(_tiles[entrance.Tile].TileId);
        var cells = new List<(Vector2i, Tile)>();
        var blockers = new List<EntityUid>();
        var position = entrance.Start;
        for (; Contains(footprint, position); position += direction)
        {
            for (var offset = -entrance.Width / 2; offset <= entrance.Width / 2; offset++)
            {
                var cell = position + side * offset;
                cells.Add((cell, tile));
                paved.Add(cell);
                var anchored = _maps.GetAnchoredEntitiesEnumerator(grid.Owner, grid.Comp, cell);
                while (anchored.MoveNext(out var uid))
                {
                    if (TryComp<Robust.Shared.Physics.Components.PhysicsComponent>(uid, out var body) && body.Hard && body.CanCollide)
                        blockers.Add(uid.Value);
                }
            }
        }

        foreach (var blocker in blockers)
        {
            Del(blocker);
        }

        _maps.SetTiles(grid.Owner, grid.Comp, cells);
    }

    /// <summary>Stacks the staged levels onto the world's layers, registers the site and makes the station.</summary>
    private async Task<HashSet<Vector2i>> BuildCityAsync(
        CityPlan plan,
        List<CityLevel> levels,
        EntityUid ground,
        MapGridComponent grid,
        WFPlanetNetworkComponent network,
        EntityUid orbit,
        Vector2 centre,
        float radius,
        WFPlanetPoiTablePrototype table,
        WFPlanetPoiComponent record,
        HashSet<Vector2i> initialPins,
        HashSet<EntityUid> created,
        List<uint> createdDecals,
        HashSet<Vector2i> changed)
    {
        var (city, config, entry, footprint, origin, reservation, _) = plan;
        var stationGrids = new List<EntityUid>();
        var siteTiles = new HashSet<Vector2i>();
        // A floor is under cover wherever any level above it has a tile: the separate grids of the upper levels don't
        // roof what is below them the way a planet's own layers do.
        var tilesByDepth = levels.ToDictionary(level => level.Depth, level => LevelTiles(level.Grid));
        var floors = new List<CityFloor>();
        foreach (var level in levels.OrderBy(level => level.Depth))
        {
            if (TerminatingOrDeleted(ground))
                return new HashSet<Vector2i>();

            var tiles = LevelTiles(level.Grid).Select(tile => tile + origin).ToHashSet();
            var roofs = LevelRoofs(level.Grid);
            foreach (var (depth, higher) in tilesByDepth)
            {
                if (depth > level.Depth)
                    roofs.UnionWith(higher);
            }

            roofs = roofs.Select(tile => tile + origin).ToHashSet();
            EntityUid target;
            if (level.Depth == 0)
            {
                target = ground;
                siteTiles = tiles;
                changed.UnionWith(tiles);
            }
            else
            {
                var layer = level.Depth > 0 ? network.Layers[level.Depth] : network.LowerLayers[-level.Depth - 1];
                target = HasComp<MapGridComponent>(layer) ? layer : CreateCityGrid(city, layer, level.Depth);
            }

            var targetGrid = Comp<MapGridComponent>(target);
            if (target == ground || TryComp<BiomeComponent>(target, out _))
            {
                using (_preload.Time($"clearing {tiles.Count} tiles of {ToPrettyString(target)} for {city.ID}"))
                    ClearForCity(target, targetGrid, tiles);
            }

            await _preload.Breathe();
            await MergeSiteAsync(target, targetGrid, level.Grid, origin, footprint, created, createdDecals);
            if (TerminatingOrDeleted(ground) || TerminatingOrDeleted(target))
                return new HashSet<Vector2i>();
            await SpawnCityDeferredAsync(city, level, target, origin, created);
            if (TerminatingOrDeleted(ground) || TerminatingOrDeleted(target))
                return new HashSet<Vector2i>();
            EnsureComp<RoofComponent>(target);
            await SetRoofsAsync((target, targetGrid, null), tiles, roofs);

            floors.Add(new CityFloor(level.Depth, (target, targetGrid), tiles, tiles.Where(roofs.Contains).ToHashSet()));
            if (TryComp<BiomeComponent>(target, out var targetBiome))
            {
                using (_preload.Time($"pinning {tiles.Count} tiles of {ToPrettyString(target)}"))
                    _biome.WfPinTiles((target, targetBiome), tiles);
            }

            if (!stationGrids.Contains(target))
                stationGrids.Add(target);

            await NextTick();
        }

        if (TerminatingOrDeleted(ground))
            return new HashSet<Vector2i>();

        var stairwells = OpenStairwells(ground, grid, floors);
        var site = new WFPlanetPoiSite
        {
            MapPath = entry.MapPath,
            Name = city.Name,
            Origin = origin,
            Bounds = reservation,
            TileCount = siteTiles.Count,
            Connection = entry.Connection,
            Expeditions = false,
            Difficulty = table.Difficulty,
            Shafts = stairwells,
            City = true,
            Priority = true,
        };

        if (table.Roads != null && entry.Connection != WFPlanetPoiConnection.Isolated)
        {
            try
            {
                site.Sockets = ResolveSockets(ground, grid, entry, site, siteTiles, table.Roads, initialPins, centre, radius - 1, table.CentreClear);
                if (entry.Connection == WFPlanetPoiConnection.Through)
                    ValidateThrough(ground, grid, site, siteTiles);
            }
            catch (InvalidOperationException error)
            {
                Log.Warning($"{city.ID} on {ToPrettyString(ground)} stays off the roads: {error.Message}");
                site.Sockets = new List<WFPlanetRoadSocket>();
                site.AccessPaths.Clear();
                site.Connection = WFPlanetPoiConnection.Isolated;
            }
        }

        var middle = new Vector2i((reservation.Left + reservation.Right) / 2, (reservation.Bottom + reservation.Top) / 2);
        site.Signal = SpawnSignal(ground, orbit, middle, entry, table);
        created.Add(site.Signal);
        record.Sites.Add(site);

        // The game map's station, without the z-level loader: its levels are already on the world's layers.
        var overrides = new ComponentRegistry();
        var zLevels = _factory.GetComponentName<CEStationZLevelsComponent>();
        foreach (var (name, component) in config.StationComponentOverrides)
        {
            if (name != zLevels)
                overrides.Add(name, component);
        }

        var stationConfig = new StationConfig
        {
            StationPrototype = config.StationPrototype,
            StationComponentOverrides = overrides,
            gridComponents = config.gridComponents,
        };
        // Station setup names its grids after the station; the world's layers keep their own names.
        var names = stationGrids.ToDictionary(uid => uid, uid => MetaData(uid).EntityName);
        EntityUid station;
        using (_preload.Time($"making {city.ID}'s station"))
            station = _station.InitializeNewStation(stationConfig, stationGrids);
        foreach (var (uid, name) in names)
        {
            _meta.SetEntityName(uid, name);
        }

        created.Add(station);
        EnsureComp<WFPlanetCitiesComponent>(ground).Stations.Add(station);
        await NextTick();
        if (TerminatingOrDeleted(ground))
            return new HashSet<Vector2i>();
        await SpawnCityResidentsAsync(city, floors, created);
        await NextTick();
        if (TerminatingOrDeleted(ground))
            return new HashSet<Vector2i>();
        await RollCityFateAsync(city, floors, created);
        Log.Info($"Built {city.ID} on {ToPrettyString(ground)} at {origin}, {levels.Count} levels.");
        return siteTiles.Union(stairwells).ToHashSet();
    }

    /// <summary>
    /// Keeps open the street's holes over stairs on the level below: pinned empty, or the biome would grow its ground
    /// over them once their chunk loads and close the way down. A cavern under them fits them out as stairwells.
    /// </summary>
    private HashSet<Vector2i> OpenStairwells(EntityUid ground, MapGridComponent grid, List<CityFloor> floors)
    {
        var stairwells = new HashSet<Vector2i>();
        if (floors.FirstOrDefault(floor => floor.Depth == 0) is not { } street
            || floors.FirstOrDefault(floor => floor.Depth == -1) is not { } under)
            return stairwells;

        foreach (var tile in under.Tiles)
        {
            if (street.Tiles.Contains(tile))
                continue;

            var anchored = _maps.GetAnchoredEntitiesEnumerator(under.Grid.Owner, under.Grid.Comp, tile);
            while (anchored.MoveNext(out var uid))
            {
                if (!HasComp<CEZLevelHighGroundComponent>(uid))
                    continue;

                stairwells.Add(tile);
                break;
            }
        }

        if (stairwells.Count == 0)
            return stairwells;

        ClearForCity(ground, grid, stairwells);
        _maps.SetTiles(ground, grid, stairwells.Select(tile => (tile, Tile.Empty)).ToList());
        if (TryComp<BiomeComponent>(ground, out var biome))
            _biome.WfPinTiles((ground, biome), stairwells);

        // Holes that were never ground raise no tile change, so the cavern hears of them here.
        if (TryComp<WFCavernGroundComponent>(ground, out var cavern))
            cavern.Opened.UnionWith(stairwells);

        return stairwells;
    }

    /// <summary>A static grid at the world's origin on a layer with no ground of its own, held up like a mapped island.</summary>
    private EntityUid CreateCityGrid(WFFixedCityPrototype city, EntityUid layer, int depth)
    {
        var grid = _maps.CreateGridEntity(Comp<MapComponent>(layer).MapId);
        grid.Comp.CanSplit = false;
        _meta.SetEntityName(grid.Owner, $"{Loc.GetString(city.Name)} [{depth}]");
        EnsureComp<CEZMappingAnchorGridComponent>(grid.Owner);
        EnsureComp<WFPlanetCityGridComponent>(grid.Owner).City = city.ID;
        _physics.SetBodyType(grid.Owner, BodyType.Static);
        return grid.Owner;
    }

    /// <summary>
    /// Spawns a level's bare walls, rock, cables and water where the merged level now stands, within the generation
    /// budget: a few hundred a tick instead of all twenty thousand in the load. Nothing solid goes on an entrance road.
    /// </summary>
    private async Task SpawnCityDeferredAsync(WFFixedCityPrototype city, CityLevel level, EntityUid target, Vector2i origin, HashSet<EntityUid> created)
    {
        foreach (var (proto, position, rotation) in level.Deferred)
        {
            await _preload.Breathe();
            if (TerminatingOrDeleted(target))
                return;

            var spawned = Spawn(proto, new EntityCoordinates(target, position + origin));
            if (rotation != Angle.Zero)
                _transform.SetLocalRotation(spawned, rotation);
            if (level.Paved.Contains(position.Floored())
                && TryComp<Robust.Shared.Physics.Components.PhysicsComponent>(spawned, out var body) && body.Hard && body.CanCollide)
            {
                Del(spawned);
                continue;
            }

            if (city.StripGodmode)
                RemComp<GodmodeComponent>(spawned);
            created.Add(spawned);
        }

        level.Deferred.Clear();
    }

    /// <summary>Clears anything already standing where a level's tiles go on a layer with terrain of its own.</summary>
    private void ClearForCity(EntityUid target, MapGridComponent grid, HashSet<Vector2i> tiles)
    {
        var doomed = new List<EntityUid>();
        foreach (var tile in tiles)
        {
            var anchored = _maps.GetAnchoredEntitiesEnumerator(target, grid, tile);
            while (anchored.MoveNext(out var uid))
            {
                doomed.Add(uid.Value);
            }
        }

        foreach (var uid in doomed)
        {
            QueueDel(uid);
        }
    }

    private HashSet<Vector2i> LevelTiles(Entity<MapGridComponent> grid)
    {
        var tiles = new HashSet<Vector2i>();
        var enumerator = _maps.GetAllTilesEnumerator(grid.Owner, grid.Comp);
        while (enumerator.MoveNext(out var tile))
        {
            tiles.Add(tile.Value.GridIndices);
        }

        return tiles;
    }

    private HashSet<Vector2i> LevelRoofs(Entity<MapGridComponent> grid)
    {
        var roofs = new HashSet<Vector2i>();
        if (!TryComp<RoofComponent>(grid.Owner, out var roof))
            return roofs;

        foreach (var tile in LevelTiles(grid))
        {
            if (_roof.IsRooved((grid.Owner, grid.Comp, roof), tile))
                roofs.Add(tile);
        }

        return roofs;
    }
}
