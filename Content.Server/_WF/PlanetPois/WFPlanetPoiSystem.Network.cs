using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.Server._WF.Planets;
using Content.Shared._WF.PlanetPois;
using Content.Shared._WF.Planets;
using Content.Shared.Doors.Components;
using Content.Shared.Decals;
using Content.Server.Decals;
using Content.Shared.Light.Components;
using Content.Shared.Parallax.Biomes;
using Content.Shared.Physics;
using Content.Server.Parallax;
using Robust.Server.Physics;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Utility;

namespace Content.Server._WF.PlanetPois;

/// <summary>Places reserved sites and their physical roads before terrain generation.</summary>
public sealed partial class WFPlanetPoiSystem
{
    private const int AttemptsPerSite = 128;

    [Dependency] private MapLoaderSystem _loader = default!;
    [Dependency] private SharedMapSystem _maps = default!;
    [Dependency] private GridFixtureSystem _fixtures = default!;
    [Dependency] private ITileDefinitionManager _tiles = default!;
    [Dependency] private DecalSystem _decals = default!;

    /// <summary>Builds and validates every site and route before releasing the terrain hold.</summary>
    private async Task PlaceAsync(EntityUid ground, WFPlanetNetworkComponent network, Vector2 centre, float radius, WFPlanetPoiTablePrototype table,
        List<Task> claims)
    {
        var record = EnsureComp<WFPlanetPoiComponent>(ground);
        var created = new HashSet<EntityUid>();
        var createdDecals = new List<uint>();
        var changed = new HashSet<Vector2i>();
        var originalTiles = new Dictionary<Vector2i, Tile>();
        var originalRoof = new HashSet<Vector2i>();
        var initialPins = new HashSet<Vector2i>();
        // Every step waits for a later tick once this one's budget is spent.
        _preload.GenerationSpent();
        Report(ground, WFWorldFormingStage.Cities, 0f);
        var claimsDone = ClaimsDone(claims);
        try
        {
            ValidateTable(table);
            if (!TryComp<BiomeComponent>(ground, out var biome) || !TryComp<MapGridComponent>(ground, out var grid))
                throw new InvalidOperationException("Ground has no biome or grid.");

            foreach (var pinned in biome.ModifiedTiles.Values) initialPins.UnionWith(pinned);
            TryComp<RoofComponent>(ground, out var oldRoof);
            var before = _maps.GetAllTilesEnumerator(ground, grid);
            while (before.MoveNext(out var oldTile))
            {
                originalTiles[oldTile.Value.GridIndices] = oldTile.Value.Tile;
                if (oldRoof != null && _roof.IsRooved((ground, grid, oldRoof), oldTile.Value.GridIndices)) originalRoof.Add(oldTile.Value.GridIndices);
            }
            var orbit = network.OrbitMap;
            var entries = SelectEntries(table);
            var extra = new WFPlanetPoiExtraSitesEvent(ground, network.Surface, new List<WFPlanetPoiEntry>());
            RaiseLocalEvent(ref extra);
            entries.InsertRange(0, extra.Sites);
            var reserved = new List<Box2i>();
            await PlaceCitiesAsync(ground, grid, biome, network, orbit, centre, radius, table, record, reserved, initialPins, created, createdDecals, changed, claimsDone);
            Report(ground, WFWorldFormingStage.Sites, 0.1f);
            await claimsDone;
            if (TerminatingOrDeleted(ground))
                return;
            var placed = 0;
            foreach (var listed in entries)
            {
                Report(ground, WFWorldFormingStage.Sites, 0.1f + 0.5f * placed++ / Math.Max(1, entries.Count));
                await _preload.Breathe();
                if (TerminatingOrDeleted(ground))
                    return;
                var turns = listed.MapPath != null && listed.Rotate ? _random.Next(4) : 0;
                var entry = turns == 0 ? listed : Turned(listed, turns);
                var footprint = entry.Footprint ?? new Box2i(-table.Footprint, -table.Footprint, table.Footprint + 1, table.Footprint + 1);
                HashSet<Vector2i> siteTiles;
                var shafts = new HashSet<Vector2i>();
                Vector2i origin;
                Box2i reservation;
                // A site that throws, a renamed prototype in its loot say, is undone and skipped; the world keeps the rest.
                var createdBefore = created.ToHashSet();
                var decalsStart = createdDecals.Count;
                var changedBefore = changed.ToHashSet();
                try
                {
                    if (entry.MapPath is { } path)
                    {
                        // A site with no room is left out rather than costing the world every other site.
                        if (!TryOrigin((ground, biome), centre, radius, table, entry, footprint, reserved, out origin))
                        {
                            Log.Warning($"No room for {entry.Name} on {ToPrettyString(ground)}; the site is skipped.");
                            continue;
                        }
                        reservation = Translate(footprint, origin);
                        siteTiles = await LoadSiteAsync(ground, grid, path, origin, listed.Footprint ?? footprint, turns, created, createdDecals, changed);
                    }
                    else
                    {
                        StagedDungeon generated;
                        try
                        {
                            generated = await GenerateAccessibleDungeonAsync(entry, table.Roads, footprint, _random.Next());
                        }
                        catch (InvalidOperationException error)
                        {
                            Log.Warning($"{entry.Name} on {ToPrettyString(ground)} is skipped: {error.Message}");
                            continue;
                        }
                        try
                        {
                            if (TerminatingOrDeleted(ground)) return;
                            footprint = generated.Footprint;
                            if (!TryOrigin((ground, biome), centre, radius, table, entry, footprint, reserved, out origin))
                            {
                                Log.Warning($"No room for {entry.Name} on {ToPrettyString(ground)}; the site is skipped.");
                                continue;
                            }
                            reservation = Translate(footprint, origin);
                            siteTiles = generated.Tiles.Select(tile => tile + origin).ToHashSet();
                            changed.UnionWith(siteTiles);
                            await MergeSiteAsync(ground, grid, generated.Grid, origin, footprint, created, createdDecals);
                            EnsureComp<RoofComponent>(ground);
                            foreach (var tile in siteTiles) _roof.SetRoof((ground, grid, null), tile, true);
                        }
                        finally { if (!TerminatingOrDeleted(generated.Map)) QueueDel(generated.Map); }
                    }
                    changed.UnionWith(siteTiles);
                    if (siteTiles.Count == 0)
                        throw new InvalidOperationException($"{entry.Name} generated no tiles.");
                    var actual = BoundsOf(siteTiles);
                    if (!Fits(actual, centre, radius - 1, table.CentreClear) || reserved.Any(other => Overlaps(other, actual)))
                        throw new InvalidOperationException($"{entry.Name} exceeded its reserved placement; increase its footprint or world radius.");
                    reserved.Add(PlacementBounds(Union(reservation, actual), entry, table.Roads));
                    if (entry.MapPath != null)
                    {
                        var claimed = new HashSet<Vector2i>();
                        for (var x = reservation.Left; x < reservation.Right; x++)
                        for (var y = reservation.Bottom; y < reservation.Top; y++) claimed.Add(new Vector2i(x, y));
                        changed.UnionWith(claimed);
                        // The map's open ground is the world's: lay it before the pin keeps the biome off for good.
                        var open = new List<(Vector2i, Tile)>();
                        foreach (var tile in claimed)
                            if (_maps.GetTileRef(ground, grid, tile).Tile.IsEmpty && _biome.TryGetBiomeTile(ground, grid, tile, out var natural))
                                open.Add((tile, natural.Value));
                        _maps.SetTiles(ground, grid, open);
                        _biome.WfPinTiles((ground, biome), claimed);
                        if (listed.Below is { } belowPath)
                            shafts = await LayBelowAsync(ground, grid, network, belowPath, origin, listed.Footprint ?? footprint, turns, siteTiles, created, createdDecals);
                    }
                    else _biome.WfPinTiles((ground, biome), siteTiles);
                    var site = new WFPlanetPoiSite
                    {
                        Dungeon = entry.Dungeon, MapPath = entry.MapPath, Name = entry.Name,
                        Origin = origin, Bounds = table.Roads != null || entry.MapPath != null ? reservation : actual,
                        TileCount = siteTiles.Count, Connection = entry.Connection,
                        Expeditions = entry.Expeditions, Difficulty = entry.Difficulty ?? table.Difficulty,
                        Dwelling = entry.Dwelling,
                        Priority = entry.Priority,
                        Shafts = shafts,
                    };
                    if (entry.Trail && entry.Sockets.Count > 0)
                    {
                        site.TrailStart = new WFPlanetRoadSocket(origin + entry.Sockets[0].Offset, entry.Sockets[0].Direction);
                        site.Tiles = siteTiles;
                    }
                    if (table.Roads != null && entry.Connection != WFPlanetPoiConnection.Isolated)
                    {
                        // A site whose entrances can't be reached on this ground stays, off the roads.
                        try
                        {
                            site.Sockets = ResolveSockets(ground, grid, entry, site, siteTiles, table.Roads, initialPins, centre, radius - 1, table.CentreClear);
                            if (entry.Connection == WFPlanetPoiConnection.Through)
                                ValidateThrough(ground, grid, site, siteTiles);
                        }
                        catch (InvalidOperationException error)
                        {
                            Log.Warning($"{entry.Name} on {ToPrettyString(ground)} stays off the roads: {error.Message}");
                            site.Sockets = new List<WFPlanetRoadSocket>();
                            site.AccessPaths.Clear();
                            site.Connection = WFPlanetPoiConnection.Isolated;
                        }
                    }
                    site.Signal = SpawnSignal(ground, orbit, new Vector2i((actual.Left + actual.Right) / 2, (actual.Bottom + actual.Top) / 2), entry, table);
                    record.Sites.Add(site);
                    created.Add(site.Signal);
                }
                catch (Exception error) when (!TerminatingOrDeleted(ground))
                {
                    Log.Error($"{entry.Name} on {ToPrettyString(ground)} failed and is skipped: {error}");
                    RollBackSite(ground, created, createdBefore, createdDecals, decalsStart, changed, changedBefore, originalTiles, originalRoof, initialPins);
                }
            }
            Report(ground, WFWorldFormingStage.Roads, 0.6f);
            await _preload.Breathe();
            if (table.Roads is { } roads && record.Sites.Any(site => site.Sockets.Count > 0))
            {
                if (!float.IsFinite(radius) || radius <= 0 || radius > 4096)
                    throw new InvalidOperationException("Road worlds must have a finite radius of at most 4096 tiles.");
                // Only pinned ground is in the way: the sites, their clearance, cavern mouths and the ring. Whatever
                // the biome has grown elsewhere by now is cleared when the road is painted, as under the ring.
                var blocked = new HashSet<Vector2i>(initialPins);
                foreach (var pinned in biome.ModifiedTiles.Values)
                    blocked.UnionWith(pinned);
                foreach (var site in record.Sites)
                foreach (var socket in site.Sockets)
                {
                    var direction = Direction(socket.Direction);
                    var clearance = roads.Width / 2 + roads.ShoulderWidth;
                    for (var step = 0; step <= clearance + 1; step++)
                    for (var side = -clearance; side <= clearance; side++)
                    {
                        var tile = socket.Position + direction * step + new Vector2i(-direction.Y, direction.X) * side;
                        if (Contains(site.Bounds, tile) && !initialPins.Contains(tile) && (Usable(ground, grid, tile) || _maps.GetTileRef(ground, grid, tile).Tile.IsEmpty))
                            blocked.Remove(tile);
                    }
                }
                await _preload.Breathe();
                var plan = await PlanRoadsAsync(ground, record, centre, radius, table, roads, blocked);
                await _preload.Breathe();
                if (!plan.Success)
                    Log.Warning($"No roads on {ToPrettyString(ground)}: {plan.Error ?? "routing failed"}. The sites stay.");
                else
                {
                    foreach (var site in record.Sites)
                    foreach (var socket in site.Sockets)
                        if (site.AccessPaths.TryGetValue(socket, out var access))
                            plan.RoadTiles.UnionWith(access.Where(tile => _maps.GetTileRef(ground, grid, tile).Tile.IsEmpty));
                    // An access path the site has already floored over leaves only its loose ends as road; those
                    // no longer touch the network, so they go, and the road graph stays one piece.
                    var pieces = WFPlanetRoadPlanner.Components(plan.RoadTiles);
                    if (pieces.Count > 1)
                    {
                        var main = pieces.MaxBy(piece => piece.Count)!;
                        foreach (var piece in pieces)
                            if (!ReferenceEquals(piece, main))
                                plan.RoadTiles.ExceptWith(piece);
                    }
                    changed.UnionWith(plan.ShoulderTiles);
                    changed.UnionWith(plan.RoadTiles);
                    Report(ground, WFWorldFormingStage.Roads, 0.65f);
                    await PaintRoadAsync(ground, grid, biome, plan.ShoulderTiles, roads.ShoulderTile ?? roads.Tile);
                    Report(ground, WFWorldFormingStage.Roads, 0.72f);
                    await PaintRoadAsync(ground, grid, biome, plan.RoadTiles, roads.Tile);
                    record.RoadTiles.UnionWith(plan.RoadTiles);
                    record.ShoulderTiles.UnionWith(plan.ShoulderTiles);
                    record.Centreline.UnionWith(plan.Centreline);
                    Report(ground, WFWorldFormingStage.Roads, 0.78f);
                    await DressRoadsAsync(ground, grid, record, roads, plan, created, createdDecals);
                    Report(ground, WFWorldFormingStage.Roads, 0.84f);
                    await LayTrailsAsync(ground, grid, biome, record, roads, centre, radius - 1, table.CentreClear, changed);
                    Report(ground, WFWorldFormingStage.Roads, 0.88f);
                    await DigTunnelsAsync(ground, grid, biome, record, roads, plan, created, changed);
                }
            }
            // Wrecks last: they keep off the sites and roads, which are pinned by now.
            Report(ground, WFWorldFormingStage.Wrecks, 0.9f);
            await PlaceWrecksAsync(ground, grid, biome, orbit, centre, radius, table, record, created);
            Report(ground, WFWorldFormingStage.Wrecks, 0.95f);
            await PlaceCampsAsync(ground, grid, biome, orbit, centre, radius, table, record, created);
            Report(ground, WFWorldFormingStage.Wrecks, 1f);
            record.PendingRoamers = table.Roamers.ToList();
            record.PendingResidents = table.Residents;
            record.ResidentChance = table.ResidentChance;
            record.ResidentHome = table.ResidentHome;
            record.PendingVehicles = table.Vehicles;
            record.PendingRoadPatrols = table.RoadPatrols.ToList();
            record.RoamerCentre = centre;
            record.RoamerRadius = radius - table.EdgeMargin;
            record.RoamerInner = table.CentreClear + RoamerClearance;
            record.NetworkReady = true;
        }
        catch (Exception error)
        {
            if (TerminatingOrDeleted(ground))
                return;
            foreach (var decal in createdDecals) _decals.RemoveDecal(ground, decal);
            foreach (var entity in created)
                if (!TerminatingOrDeleted(entity)) QueueDel(entity);
            if (TryComp<MapGridComponent>(ground, out var rollbackGrid))
            {
                _maps.SetTiles(ground, rollbackGrid, changed.Select(tile => (tile, originalTiles.GetValueOrDefault(tile))).ToList());
                foreach (var tile in changed) _roof.SetRoof((ground, rollbackGrid, null), tile, originalRoof.Contains(tile));
            }
            if (TryComp<BiomeComponent>(ground, out var rollbackBiome))
                foreach (var modified in rollbackBiome.ModifiedTiles.Values) modified.RemoveWhere(tile => changed.Contains(tile) && !initialPins.Contains(tile));
            record.Sites.Clear();
            record.RoadTiles.Clear();
            record.ShoulderTiles.Clear();
            record.TrailTiles.Clear();
            record.Tunnels.Clear();
            record.Centreline.Clear();
            record.Furniture.Clear();
            record.GenerationError = $"POI table {table.ID}: {error.Message}";
            Log.Error(record.GenerationError);
            throw;
        }
    }

    /// <summary>Undoes one failed site: its entities, decals, tiles, roofs and pins, back to before it was laid.</summary>
    private void RollBackSite(EntityUid ground, HashSet<EntityUid> created, HashSet<EntityUid> createdBefore, List<uint> createdDecals, int decalsStart,
        HashSet<Vector2i> changed, HashSet<Vector2i> changedBefore, Dictionary<Vector2i, Tile> originalTiles, HashSet<Vector2i> originalRoof,
        HashSet<Vector2i> initialPins)
    {
        foreach (var entity in created.Where(entity => !createdBefore.Contains(entity)).ToList())
        {
            if (!TerminatingOrDeleted(entity)) QueueDel(entity);
            created.Remove(entity);
        }
        for (var i = decalsStart; i < createdDecals.Count; i++)
            _decals.RemoveDecal(ground, createdDecals[i]);
        createdDecals.RemoveRange(decalsStart, createdDecals.Count - decalsStart);

        var undone = changed.Where(tile => !changedBefore.Contains(tile)).ToList();
        if (TryComp<MapGridComponent>(ground, out var grid))
        {
            _maps.SetTiles(ground, grid, undone.Select(tile => (tile, originalTiles.GetValueOrDefault(tile))).ToList());
            foreach (var tile in undone) _roof.SetRoof((ground, grid, null), tile, originalRoof.Contains(tile));
        }
        if (TryComp<BiomeComponent>(ground, out var biome))
        {
            var set = undone.ToHashSet();
            foreach (var modified in biome.ModifiedTiles.Values) modified.RemoveWhere(tile => set.Contains(tile) && !initialPins.Contains(tile));
        }
        changed.ExceptWith(undone);
    }

    /// <summary>Tells the world-forming bar how far the sites and roads have got.</summary>
    private void Report(EntityUid ground, WFWorldFormingStage stage, float fraction)
    {
        _preload.ReportProgress(ground, "sites", stage, fraction);
    }

    /// <summary>
    /// Finishes once every claim on the ground, such as the cavern mouths, is in. It waits a tick first, for the claims
    /// raised after this handler; a failed claim is the preload's to report.
    /// </summary>
    private async Task ClaimsDone(List<Task> claims)
    {
        await _preload.NextTick();
        foreach (var claim in claims.ToList())
        {
            try
            {
                await claim;
            }
            catch (Exception)
            {
                // Logged with the holds.
            }
        }
    }

    /// <summary>A staged procedural layout validated before it touches the planet.</summary>
    internal sealed record StagedDungeon(EntityUid Map, Entity<MapGridComponent> Grid, HashSet<Vector2i> Tiles, Box2i Footprint, WFPlanetPoiSite Site, int Attempts, int Seed);

    /// <summary>Regenerates sealed layouts with deterministic seeds before placing any tiles on the planet.</summary>
    internal async Task<StagedDungeon> GenerateAccessibleDungeonAsync(WFPlanetPoiEntry entry, WFPlanetRoadNetwork? roads, Box2i footprint, int seed)
    {
        var config = _proto.Index(entry.Dungeon!.Value);
        await WarmAtlasesAsync(config);
        var retries = new Random(seed);
        var lastError = "Generated no usable exterior entrance.";
        for (var attempt = 0; attempt < 16; attempt++)
        {
            var generationSeed = attempt == 0 ? seed : retries.Next();
            var map = _maps.CreateMap(out var mapId, runMapInit: false);
            var grid = _maps.CreateGridEntity(mapId);
            grid.Comp.CanSplit = false;
            var retained = false;
            try
            {
                await _dungeon.GenerateDungeonAsync(config, config.ID, grid.Owner, grid.Comp, Vector2i.Zero, generationSeed);
                var tiles = new HashSet<Vector2i>();
                var enumerator = _maps.GetAllTilesEnumerator(grid.Owner, grid.Comp);
                while (enumerator.MoveNext(out var tile)) tiles.Add(tile.Value.GridIndices);
                if (tiles.Count == 0)
                    throw new InvalidOperationException($"{entry.Name} generated no tiles.");
                var bounds = BoundsOf(tiles);
                var clearance = roads != null && entry.Connection != WFPlanetPoiConnection.Isolated ? roads.Width / 2 + roads.ShoulderWidth + 2 : 0;
                var reserved = Union(footprint, new Box2i(bounds.Left - clearance, bounds.Bottom - clearance, bounds.Right + clearance, bounds.Top + clearance));
                var site = new WFPlanetPoiSite { Origin = Vector2i.Zero, Bounds = reserved, Connection = entry.Connection, Dungeon = entry.Dungeon, Name = entry.Name };
                if (roads != null && entry.Connection != WFPlanetPoiConnection.Isolated)
                {
                    EnsureComp<WFPlanetPoiComponent>(grid.Owner);
                    try
                    {
                        site.Sockets = ResolveSockets(grid.Owner, grid.Comp, entry, site, tiles, roads, new HashSet<Vector2i>(), Vector2.Zero, 4096, 0);
                        if (entry.Connection == WFPlanetPoiConnection.Through) ValidateThrough(grid.Owner, grid.Comp, site, tiles);
                    }
                    catch (InvalidOperationException error)
                    {
                        lastError = error.Message;
                        continue;
                    }
                }
                retained = true;
                return new StagedDungeon(map, grid, tiles, reserved, site, attempt + 1, generationSeed);
            }
            finally { if (!retained && !TerminatingOrDeleted(map)) QueueDel(map); }
        }
        throw new InvalidOperationException($"{entry.Name} had no usable entrance after 16 validated layouts: {lastError}");
    }
    private static Box2i Translate(Box2i b, Vector2i o) => new(b.Left + o.X, b.Bottom + o.Y, b.Right + o.X, b.Top + o.Y);
    private static bool Overlaps(Box2i a, Box2i b) => a.Left < b.Right && a.Right > b.Left && a.Bottom < b.Top && a.Top > b.Bottom;
    private static Box2i Union(Box2i a, Box2i b) => new(Math.Min(a.Left, b.Left), Math.Min(a.Bottom, b.Bottom), Math.Max(a.Right, b.Right), Math.Max(a.Top, b.Top));
    private static Box2i BoundsOf(HashSet<Vector2i> tiles) => new(tiles.Min(t => t.X), tiles.Min(t => t.Y), tiles.Max(t => t.X) + 1, tiles.Max(t => t.Y) + 1);
    private static bool Contains(Box2i b, Vector2i t) => t.X >= b.Left && t.X < b.Right && t.Y >= b.Bottom && t.Y < b.Top;

    /// <summary>
    /// Plans the world's roads. A site whose own entrance can't be joined up, say one set down hard against the rock, is
    /// left off the roads and the rest planned again, rather than the whole world going without. A priority site, a
    /// cargo hub or a city, is routed first; when it still fails the plan is tried again with other seeds, then with its
    /// other entrances first, and only the sites in its way are left off. The water is found first, a slice a tick, and
    /// the planner, which touches nothing else of the world, runs on the thread pool: on a big world it takes seconds.
    /// </summary>
    private async Task<WFPlanetRoadPlan> PlanRoadsAsync(EntityUid ground, WFPlanetPoiComponent record, Vector2 centre, float radius,
        WFPlanetPoiTablePrototype table, WFPlanetRoadNetwork roads, HashSet<Vector2i> blocked)
    {
        var seed = _random.Next();
        var waterTiles = await WaterTilesAsync(ground, centre, radius);
        var water = new Func<Vector2i, bool>(waterTiles.Contains);
        var walls = new HashSet<Vector2i>(blocked);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var retries = 0;
        while (true)
        {
            var sites = record.Sites.Select(site => new WFPlanetRoadSite(site.Bounds, site.Connection, site.Sockets, site.Priority)).ToList();
            // Off-road sites with a footpath: the roads come within reach of their doors.
            var doors = record.Sites.Where(site => site.Connection == WFPlanetPoiConnection.Isolated && site.TrailStart != null)
                .Select(site => site.TrailStart!.Position).ToList();
            var shape = new WFPlanetRoadShape(roads.Gates, roads.Exits, roads.Loops, roads.Reach);
            var plan = await Task.Run(() => WFPlanetRoadPlanner.Plan(centre, radius - 1, table.CentreClear, sites, walls, roads.Width,
                roads.ShoulderWidth, seed, water, shape, doors));
            if (TerminatingOrDeleted(ground))
                return plan;
            if (plan.Success || plan.FailedEntrance is not { } entrance)
            {
                Log.Info($"Roads on {ToPrettyString(ground)} planned in {watch.ElapsedMilliseconds} ms: {plan.Centreline.Count} tiles of centreline, {plan.Backbone.Count} on gates, exits and links.");
                return plan;
            }

            var failed = record.Sites.FirstOrDefault(site => site.Connection != WFPlanetPoiConnection.Isolated
                                                             && site.Sockets.Any(socket => socket.Position == entrance));
            if (failed == null)
                return plan;

            if (failed.Priority)
            {
                Log.Info($"{Loc.GetString(failed.Name)} on {ToPrettyString(ground)} missed the roads, try {retries + 1}: {plan.Error}");
                if (NextPriorityTry(record, failed, ref retries, ref seed))
                    continue;

                Log.Error($"{Loc.GetString(failed.Name)} on {ToPrettyString(ground)} can't reach the roads after {retries} tries; it stays off them.");
            }

            Log.Warning($"{Loc.GetString(failed.Name)} on {ToPrettyString(ground)} stays off the roads: {plan.Error}");
            failed.Sockets = new List<WFPlanetRoadSocket>();
            failed.AccessPaths.Clear();
            failed.Connection = WFPlanetPoiConnection.Isolated;
        }
    }

    /// <summary>Seeds a priority site's failed plan gets before its other entrances are tried first.</summary>
    private const int PrioritySeedTries = 4;

    /// <summary>
    /// Sets up the next try for a priority site that missed the roads: a new seed a few times, then each of its other
    /// entrances first in turn, then each nearest other site left off the roads. False once nothing is left to try.
    /// </summary>
    private bool NextPriorityTry(WFPlanetPoiComponent record, WFPlanetPoiSite failed, ref int retries, ref int seed)
    {
        retries++;
        if (retries < PrioritySeedTries)
        {
            seed = _random.Next();
            return true;
        }

        // A roadside site uses only its first entrance; another may face open ground.
        var turn = retries - PrioritySeedTries + 1;
        if (turn < failed.Sockets.Count)
        {
            failed.Sockets.Add(failed.Sockets[0]);
            failed.Sockets.RemoveAt(0);
            seed = _random.Next();
            return true;
        }

        // The ordinary site nearest its first entrance may be the one in its way.
        var at = failed.Sockets[0].Position;
        var blocker = record.Sites
            .Where(site => !site.Priority && site.Connection != WFPlanetPoiConnection.Isolated)
            .OrderBy(site => (new Vector2((site.Bounds.Left + site.Bounds.Right) / 2f, (site.Bounds.Bottom + site.Bounds.Top) / 2f) - new Vector2(at.X, at.Y)).LengthSquared())
            .FirstOrDefault();
        if (blocker == null || retries > PrioritySeedTries + failed.Sockets.Count + 4)
            return false;

        Log.Warning($"{Loc.GetString(blocker.Name)} stays off the roads to make way for {Loc.GetString(failed.Name)}.");
        blocker.Sockets = new List<WFPlanetRoadSocket>();
        blocker.AccessPaths.Clear();
        blocker.Connection = WFPlanetPoiConnection.Isolated;
        return true;
    }

    /// <summary>The ground's water tiles within the world's circle, found within the generation budget.</summary>
    private async Task<HashSet<Vector2i>> WaterTilesAsync(EntityUid ground, Vector2 centre, float radius)
    {
        var water = new HashSet<Vector2i>();
        var r = (int) MathF.Ceiling(radius);
        var middle = centre.Floored();
        for (var x = -r; x <= r; x++)
        {
            var half = (int) MathF.Ceiling(MathF.Sqrt(MathF.Max(0f, radius * radius - x * x)));
            for (var y = -half; y <= half; y++)
            {
                // A tile past the cheap noise gates costs a full biome lookup, so the budget is checked often.
                if (y % 32 == 0)
                    await _preload.Breathe();
                if (!TryComp<BiomeComponent>(ground, out var comp))
                    return water;

                var tile = middle + new Vector2i(x, y);
                if (_biome.WfIsWater((ground, comp), tile))
                    water.Add(tile);
            }
        }

        return water;
    }

    /// <summary>Keeps the complete footprint within the boundary and outside arrivals.</summary>
    private static bool Fits(Box2i b, Vector2 c, float radius, float clear)
    {
        foreach (var corner in new[] { new Vector2(b.Left, b.Bottom), new Vector2(b.Right, b.Bottom), new Vector2(b.Left, b.Top), new Vector2(b.Right, b.Top) })
            if (Vector2.DistanceSquared(corner, c) >= radius * radius)
                return false;
        var nearest = new Vector2(Math.Clamp(c.X, b.Left, b.Right), Math.Clamp(c.Y, b.Bottom, b.Top));
        return Vector2.DistanceSquared(nearest, c) >= clear * clear;
    }

    /// <summary>Keeps a full exterior road ring free while retaining the authored socket bounds.</summary>
    private static Box2i PlacementBounds(Box2i bounds, WFPlanetPoiEntry entry, WFPlanetRoadNetwork? roads) =>
        (entry.MapPath == null || entry.Connection == WFPlanetPoiConnection.Through) && entry.Connection != WFPlanetPoiConnection.Isolated && roads != null
            ? WFPlanetPoiPlacementPlanner.WithRoadClearance(bounds, roads.Width, roads.ShoulderWidth)
            : bounds;
    /// <summary>Searches a bounded number of origins for a collision-free footprint.</summary>
    private bool TryOrigin(Entity<BiomeComponent> ground, Vector2 centre, float radius, WFPlanetPoiTablePrototype table, WFPlanetPoiEntry entry, Box2i footprint, List<Box2i> reserved, out Vector2i origin)
    {
        origin = default;
        var reach = radius - table.EdgeMargin;
        if (reach <= table.CentreClear)
            return false;
        bool Available(Vector2i candidateOrigin)
        {
            var box = Translate(footprint, candidateOrigin);
            var placement = PlacementBounds(box, entry, table.Roads);
            if (!Fits(placement, centre, radius - 1, table.CentreClear) || reserved.Any(other => Overlaps(other, placement) || Vector2.DistanceSquared(new Vector2((other.Left+other.Right)/2f, (other.Bottom+other.Top)/2f), candidateOrigin) < table.Spacing * table.Spacing))
                return false;
            if (entry.MapPath != null && table.Roads is { } roads && entry.Connection != WFPlanetPoiConnection.Isolated
                && entry.Sockets.Any(socket => !ApproachFits(candidateOrigin + socket.Offset, socket.Direction, roads, centre, radius - 1, table.CentreClear,
                    tile => reserved.Any(other => Contains(other, tile)) || _biome.WfIsPinned(ground, tile) || _biome.WfIsWater(ground, tile))))
                return false;
            return !ground.Comp.ModifiedTiles.Values.Any(indices => indices.Any(tile => Contains(placement, tile)))
                   && !Flooded(ground, box);
        }
        for (var attempt = 0; attempt < AttemptsPerSite; attempt++)
        {
            var candidate = centre + _random.NextAngle().ToVec() * MathF.Sqrt(_random.NextFloat(table.CentreClear * table.CentreClear, reach * reach));
            origin = new Vector2i((int)MathF.Floor(candidate.X), (int)MathF.Floor(candidate.Y));
            if (Available(origin))
                return true;
        }
        foreach (var candidate in WFPlanetPoiPlacementPlanner.FallbackOrigins(centre, reach, footprint, _random.Next()))
        {
            if (!Available(candidate))
                continue;
            origin = candidate;
            return true;
        }
        return false;
    }

    /// <summary>
    /// The share of a bigger footprint that may be under water, the edge of a pond or lake: it is drained where the
    /// site is laid.
    /// </summary>
    private const float SiteWater = 0.03f;

    /// <summary>A footprint up to this many tiles, such as a house, keeps out of water altogether.</summary>
    private const int SmallSite = 48 * 48;

    /// <summary>A footprint over this many tiles, such as a city, is sampled every other tile.</summary>
    private const int LargeSite = 96 * 96;

    /// <summary>
    /// Whether a footprint stands in water: any tile of a small one, or more than a few percent of a bigger one, which
    /// can't hope to miss every pond.
    /// </summary>
    private bool Flooded(Entity<BiomeComponent> ground, Box2i box)
    {
        var area = box.Width * box.Height;
        var allowed = area <= SmallSite ? 0f : SiteWater;
        var step = area > LargeSite ? 2 : 1;
        var samples = 0;
        var wet = 0;
        for (var x = box.Left; x < box.Right; x += step)
        for (var y = box.Bottom; y < box.Top; y += step)
        {
            samples++;
            if (_biome.WfIsWater(ground, new Vector2i(x, y)))
                wet++;
        }
        return wet > samples * allowed;
    }

    /// <summary>Checks complete road and shoulder approaches against bounds and protected terrain.</summary>
    private static bool ApproachFits(Vector2i position, WFPlanetPoiDirection direction, WFPlanetRoadNetwork roads, Vector2 centre, float radius, float centreClear, Func<Vector2i, bool> blocked)
    {
        var vector = Direction(direction);
        var clearance = roads.Width / 2 + roads.ShoulderWidth;
        for (var step = 0; step <= clearance + 1; step++)
        for (var side = -clearance; side <= clearance; side++)
        {
            var tile = position + vector * step + new Vector2i(-vector.Y, vector.X) * side;
            var distance = Vector2.Distance(new Vector2(tile.X + 0.5f, tile.Y + 0.5f), centre);
            if (distance + 0.707107f > radius || distance - 0.707107f < centreClear || blocked(tile)) return false;
        }
        var end = position + vector * (clearance + 1);
        for (var x = -clearance; x <= clearance; x++)
        for (var y = -clearance; y <= clearance; y++)
        {
            var tile = end + new Vector2i(x, y);
            var distance = Vector2.Distance(new Vector2(tile.X + 0.5f, tile.Y + 0.5f), centre);
            if (distance + 0.707107f > radius || distance - 0.707107f < centreClear || blocked(tile)) return false;
        }
        return true;
    }

    /// <summary>Rejects malformed tables before any world mutation.</summary>
    private void ValidateTable(WFPlanetPoiTablePrototype table)
    {
        if (table.MinCount < 0 || table.MaxCount < table.MinCount || table.MaxCount > 128 || table.Entries.Count == 0 || table.Footprint < 1 || table.Footprint > 1024
            || !float.IsFinite(table.Spacing) || table.Spacing < 0 || !float.IsFinite(table.CentreClear) || table.CentreClear < 0
            || !float.IsFinite(table.EdgeMargin) || table.EdgeMargin < 0 || !float.IsFinite(table.RevealRange) || table.RevealRange <= 0)
            throw new InvalidOperationException("Invalid counts, footprint or distances.");
        foreach (var entry in table.Entries)
        {
            if (!float.IsFinite(entry.Weight) || entry.Weight < 0 || entry.MinCount < 0 || entry.MaxCount < entry.MinCount
                || (entry.Dungeon == null) == (entry.MapPath == null) || (entry.Dungeon is { } dungeon && !_proto.HasIndex(dungeon))
                || (entry.MapPath != null && (string.IsNullOrWhiteSpace(entry.MapPath) || entry.Footprint == null)))
                throw new InvalidOperationException($"Invalid entry {entry.Name}: specify one valid dungeon or map, finite weight and bounded counts; maps require a footprint.");
            if (entry.MapPath != null && table.Roads != null && entry.Connection != WFPlanetPoiConnection.Isolated
                && (entry.Sockets.Count < (entry.Connection == WFPlanetPoiConnection.Through ? 2 : 1)
                    || entry.Sockets.Any(socket => !Contains(entry.Footprint!.Value, socket.Offset) || Contains(entry.Footprint.Value, socket.Offset + Direction(socket.Direction)))))
                throw new InvalidOperationException($"Authored {entry.Name} needs outward boundary sockets.");
            if (entry.Footprint is { } box && (box.Right <= box.Left || box.Top <= box.Bottom || box.Right - box.Left > 2048 || box.Top - box.Bottom > 2048))
                throw new InvalidOperationException($"Invalid footprint for {entry.Name}.");
        }
        if (table.Entries.Sum(e => e.MinCount) > table.MaxCount || !table.Entries.Any(e => e.Weight > 0))
            throw new InvalidOperationException("Entry minimums exceed total count or all weights are zero.");
        if (table.Roads is { } roads)
        {
            if (roads.Width < 1 || roads.Width > 15 || roads.Width % 2 == 0 || roads.ShoulderWidth < 0 || roads.ShoulderWidth > 8)
                throw new InvalidOperationException("Invalid road width or shoulder width.");
            _ = _tiles[roads.Tile];
            if (roads.ShoulderTile != null) _ = _tiles[roads.ShoulderTile];
            _ = _tiles[roads.TrailTile];
        }
    }

    /// <summary>Honors guaranteed counts and maximums before weighted selection.</summary>
    private List<WFPlanetPoiEntry> SelectEntries(WFPlanetPoiTablePrototype table)
    {
        var result = new List<WFPlanetPoiEntry>();
        foreach (var entry in table.Entries)
            for (var i = 0; i < entry.MinCount; i++) result.Add(entry);
        var wanted = Math.Max(result.Count, _random.Next(table.MinCount, table.MaxCount+1));
        while (result.Count < wanted)
        {
            var eligible = table.Entries.Where(e => e.Weight > 0 && (e.MaxCount == null || result.Count(v => ReferenceEquals(v, e)) < e.MaxCount)).ToList();
            if (eligible.Count == 0) throw new InvalidOperationException("Entry maximums cannot satisfy the requested count.");
            var total = eligible.Sum(e => (double)e.Weight);
            var roll = _random.NextDouble() * total;
            var picked = eligible[^1];
            foreach (var entry in eligible) { roll -= entry.Weight; if (roll <= 0) { picked = entry; break; } }
            result.Add(picked);
        }
        return result.OrderByDescending(e => e.Footprint is { } b ? (long)(b.Right-b.Left)*(b.Top-b.Bottom) : 0).ToList();
    }

    /// <summary>
    /// Lays a site's level below on the level under the ground, in the site's frame, clearing the rock there first, and
    /// opens the site's floor over its stairs: a gap with floor all round it. With no level under the ground, there is
    /// no basement and the floor stays whole.
    /// </summary>
    private async Task<HashSet<Vector2i>> LayBelowAsync(EntityUid ground, MapGridComponent grid, WFPlanetNetworkComponent network, string path, Vector2i origin,
        Box2i footprint, int turns, HashSet<Vector2i> siteTiles, HashSet<EntityUid> created, List<uint> createdDecals)
    {
        var shafts = new HashSet<Vector2i>();
        if (network.LowerLayers.Count == 0 || !TryComp<MapGridComponent>(network.LowerLayers[0], out var lowerGrid))
            return shafts;

        var lower = network.LowerLayers[0];
        var lowerBiome = CompOrNull<BiomeComponent>(lower);
        var tiles = await LoadSiteAsync(lower, lowerGrid, path, origin, footprint, turns, created, createdDecals, new HashSet<Vector2i>(), clearFirst: lowerBiome != null);
        if (lowerBiome != null)
            _biome.WfPinTiles((lower, lowerBiome), tiles);

        foreach (var tile in tiles)
        {
            if (!siteTiles.Contains(tile)
                && siteTiles.Contains(tile + new Vector2i(1, 0)) && siteTiles.Contains(tile - new Vector2i(1, 0))
                && siteTiles.Contains(tile + new Vector2i(0, 1)) && siteTiles.Contains(tile - new Vector2i(0, 1)))
                shafts.Add(tile);
        }
        foreach (var shaft in shafts)
        {
            ClearForCity(ground, grid, new HashSet<Vector2i> { shaft });
            _maps.SetTile(ground, grid, shaft, Tile.Empty);
            _roof.SetRoof((ground, grid, null), shaft, false);
        }
        return shafts;
    }

    /// <summary>Loads an authored grid in isolation and merges it with its explicit roofs.</summary>
    private async Task<HashSet<Vector2i>> LoadSiteAsync(EntityUid ground, MapGridComponent grid, string path, Vector2i origin, Box2i footprint, int turns, HashSet<EntityUid> created, List<uint> createdDecals, HashSet<Vector2i> changed, bool clearFirst = false)
    {
        var stagingMap = _maps.CreateMap(out var stagingId, runMapInit: false);
        try
        {
            if (await LoadGridAsync(stagingId, new ResPath(path), whole: true) is not { } source)
                throw new InvalidOperationException($"Cannot load single-grid POI map {path}.");
            if (TerminatingOrDeleted(ground))
                return new HashSet<Vector2i>();
            var result = new HashSet<Vector2i>();
            var roofs = new HashSet<Vector2i>();
            TryComp<RoofComponent>(source.Owner, out var roof);
            var enumerator = _maps.GetAllTilesEnumerator(source.Owner, source.Comp);
            while (enumerator.MoveNext(out var tile))
            {
                var index = tile.Value.GridIndices;
                if (!Contains(footprint, index)) throw new InvalidOperationException($"Map {path} has tiles outside its declared footprint.");
                result.Add(TurnTile(index, turns) + origin);
                if (roof != null && _roof.IsRooved((source.Owner, source.Comp, roof), index)) roofs.Add(TurnTile(index, turns) + origin);
            }
            changed.UnionWith(result);
            // A layer with terrain of its own, such as a cavern's rock, makes way for the site.
            if (clearFirst)
                ClearForCity(ground, grid, result);
            await MergeSiteAsync(ground, grid, source, origin, footprint, created, createdDecals, turns);
            EnsureComp<RoofComponent>(ground);
            await SetRoofsAsync((ground, grid, null), result, roofs);
            return result;
        }
        finally { if (!TerminatingOrDeleted(stagingMap)) QueueDel(stagingMap); }
    }

    /// <summary>Merges tiles, source entities and decals while preserving their local offsets.</summary>
    private async Task MergeSiteAsync(EntityUid ground, MapGridComponent grid, Entity<MapGridComponent> source, Vector2i origin, Box2i footprint, HashSet<EntityUid> created, List<uint> createdDecals, int turns = 0)
    {
        var children = Transform(source.Owner).ChildEnumerator;
        while (children.MoveNext(out var child))
        {
            if (HasComp<MapComponent>(child) || HasComp<MapGridComponent>(child)) continue;
            var local = Transform(child).LocalPosition.Floored();
            if (!Contains(footprint, local) || _maps.GetTileRef(source.Owner, source.Comp, local).Tile.IsEmpty)
                throw new InvalidOperationException($"POI entity {ToPrettyString(child)} is outside its footprint or stands without a source tile.");
        }
        var decals = _decals.GetDecalsIntersecting(source.Owner, source.Comp.LocalAABB.Enlarged(1f));
        var sourceTiles = new List<Vector2i>();
        if (turns != 0)
        {
            var tiles = _maps.GetAllTilesEnumerator(source.Owner, source.Comp);
            while (tiles.MoveNext(out var tile))
                sourceTiles.Add(tile.Value.GridIndices);
        }
        var transferred = new HashSet<EntityUid>();
        CaptureChildren(source.Owner, transferred);
        created.UnionWith(transferred);
        // Engine re-anchoring assumes both grids already belong to the same map; the target is the map itself or a grid on it.
        var targetMap = Transform(ground).MapUid ?? ground;
        _transform.SetCoordinates(source.Owner, new EntityCoordinates(targetMap, targetMap == ground ? Vector2.Zero : _transform.GetWorldPosition(ground)));
        // A city's forty thousand tiles go down a few chunks a tick, not in the merge's one call.
        await LayMergeTilesAsync(ground, grid, source, origin, turns);
        if (TerminatingOrDeleted(ground) || TerminatingOrDeleted(source.Owner))
            return;
        var canSplit = grid.CanSplit;
        grid.CanSplit = false;
        try
        {
            using (_preload.Time($"merging {ToPrettyString(source.Owner)} into {ToPrettyString(ground)}"))
                _fixtures.Merge(ground, source.Owner, origin, Turns(turns), grid, source.Comp);
        }
        finally { grid.CanSplit = canSplit; }
        TurnTileStates(ground, grid, sourceTiles, origin, turns);
        UnturnFixedRotation(transferred);
        var beforeInit = new HashSet<EntityUid>();
        CaptureChildren(ground, beforeInit);
        try
        {
            foreach (var root in transferred)
            {
                if (TerminatingOrDeleted(root)) continue;
                await InitializeTransferredAsync(root, _maps.IsInitialized(targetMap), _maps.IsPaused(targetMap));
            }
        }
        finally
        {
            var afterInit = new HashSet<EntityUid>();
            CaptureChildren(ground, afterInit);
            afterInit.ExceptWith(beforeInit);
            created.UnionWith(afterInit);
        }
        if (TerminatingOrDeleted(ground))
            return;
        EnsureComp<DecalGridComponent>(ground);
        foreach (var (_, decal) in decals)
        {
            // A decal sits on its bottom-left corner, so it turns about its middle.
            var position = Turns(turns).RotateVec(decal.Coordinates + new Vector2(0.5f)) - new Vector2(0.5f) + origin;
            var turned = decal.WithCoordinates(position).WithRotation(decal.Angle + Turns(turns));
            if (!_decals.TryAddDecal(turned, new EntityCoordinates(ground, position), out var id))
                throw new InvalidOperationException($"Could not transfer POI decal {decal.Id} at {position}.");
            createdDecals.Add(id);
        }
    }

    /// <summary>
    /// Runs destination map initialization and pause state through transferred descendants, a slice a tick. They come
    /// paused from their staging map and stay so until every one is initialised; then they are let go, the root last.
    /// </summary>
    private async Task InitializeTransferredAsync(EntityUid root, bool initialized, bool paused)
    {
        var pending = new List<EntityUid> { root };
        for (var i = 0; i < pending.Count; i++)
        {
            await _preload.Breathe();
            var entity = pending[i];
            if (TerminatingOrDeleted(entity)) continue;
            var children = Transform(entity).ChildEnumerator;
            while (children.MoveNext(out var child)) pending.Add(child);
            var meta = MetaData(entity);
            if (initialized && meta.EntityLifeStage != EntityLifeStage.MapInitialized) EntityManager.RunMapInit(entity, meta);
        }
        for (var i = pending.Count - 1; i >= 0; i--)
        {
            await _preload.Breathe();
            if (!TerminatingOrDeleted(pending[i])) _meta.SetEntityPaused(pending[i], paused);
        }
    }

    /// <summary>Tracks roots transferred by a merge so a rejected generation can remove them.</summary>
    private void CaptureChildren(EntityUid source, HashSet<EntityUid> created)
    {
        var children = Transform(source).ChildEnumerator;
        while (children.MoveNext(out var child)) created.Add(child);
    }

    /// <summary>Resolves usable boundary entrances without demolishing authored entities.</summary>
    private List<WFPlanetRoadSocket> ResolveSockets(EntityUid ground, MapGridComponent grid, WFPlanetPoiEntry entry, WFPlanetPoiSite site, HashSet<Vector2i> tiles, WFPlanetRoadNetwork roads, HashSet<Vector2i> initialPins, Vector2 centre, float radius, float centreClear)
    {
        var sockets = new List<WFPlanetRoadSocket>();
        var otherBounds = Comp<WFPlanetPoiComponent>(ground).Sites.Select(other => other.Bounds).ToArray();
        var biome = CompOrNull<BiomeComponent>(ground);
        bool Water(Vector2i tile) => biome != null && _biome.WfIsWater((ground, biome), tile);
        if (entry.Sockets.Count > 0)
        {
            foreach (var socket in entry.Sockets)
            {
                var position = site.Origin + socket.Offset;
                var direction = Direction(socket.Direction);
                if (!Contains(site.Bounds, position) || Contains(site.Bounds, position+direction) || !tiles.Contains(position) || !Usable(ground, grid, position))
                    throw new InvalidOperationException($"{entry.Name} has an unusable or inward socket at {socket.Offset}.");
                sockets.Add(new WFPlanetRoadSocket(position, socket.Direction));
            }
        }
        else if (entry.MapPath == null)
        {
            var floor = tiles.Where(tile => Usable(ground, grid, tile)).ToHashSet();
            var components = new List<HashSet<Vector2i>>();
            var cardinalDirections = Enum.GetValues<WFPlanetPoiDirection>();
            while (floor.Count > 0)
            {
                var component = new HashSet<Vector2i>();
                var pending = new Queue<Vector2i>();
                pending.Enqueue(floor.First());
                while (pending.TryDequeue(out var current))
                {
                    if (!floor.Remove(current)) continue;
                    component.Add(current);
                    foreach (var cardinal in cardinalDirections)
                        if (floor.Contains(current + Direction(cardinal))) pending.Enqueue(current + Direction(cardinal));
                }
                components.Add(component);
            }
            var totalVisited = 0;
            foreach (var component in components.OrderByDescending(component => component.Count).ThenBy(component => component.Min(tile => tile.X)).ThenBy(component => component.Min(tile => tile.Y)))
            {
                sockets.Clear();
                site.AccessPaths.Clear();
                var frontier = new Queue<Vector2i>();
                var previous = new Dictionary<Vector2i, Vector2i>();
                var reached = new HashSet<Vector2i>(component);
                foreach (var tile in component.OrderBy(tile => tile.X).ThenBy(tile => tile.Y)) frontier.Enqueue(tile);
                var clearance = roads.Width / 2 + roads.ShoulderWidth;
                while (frontier.TryDequeue(out var current))
                {
                    foreach (var cardinal in cardinalDirections)
                    {
                        var direction = Direction(cardinal);
                        var next = current + direction;
                        if (Contains(site.Bounds, next))
                        {
                            if (totalVisited >= 250000 || reached.Contains(next) || initialPins.Contains(next) || !Unblocked(ground, grid, next)) continue;
                            reached.Add(next);
                            totalVisited++;
                            previous[next] = current;
                            frontier.Enqueue(next);
                            continue;
                        }
                        var clear = ApproachFits(current, cardinal, roads, centre, radius, centreClear, tile => initialPins.Contains(tile) || otherBounds.Any(bounds => Contains(bounds, tile)) || Water(tile));
                        for (var step = 0; step <= clearance + 1 && clear; step++)
                        for (var side = -clearance; side <= clearance; side++)
                        {
                            var check = current + direction * step + new Vector2i(-direction.Y, direction.X) * side;
                            if (initialPins.Contains(check) || !Unblocked(ground, grid, check)) { clear = false; break; }
                        }
                        if (!clear) continue;
                        var socket = new WFPlanetRoadSocket(current, cardinal);
                        var approach = new List<Vector2i> { current };
                        for (var point = current; previous.TryGetValue(point, out var parent); point = parent) approach.Add(parent);
                        approach.Reverse();
                        sockets.Add(socket);
                        site.AccessPaths[socket] = approach;
                    }
                }

                var required = entry.Connection == WFPlanetPoiConnection.Through ? 2 : 1;
                if (sockets.Select(socket => socket.Position).Distinct().Count() >= required
                    && (required == 1 || sockets.Select(socket => socket.Direction).Distinct().Count() >= 2))
                    break;
                if (totalVisited >= 250000) break;
            }
        }
        sockets = sockets.Distinct().ToList();
        var needed = entry.Connection == WFPlanetPoiConnection.Through ? 2 : 1;
        if (sockets.Select(s => s.Position).Distinct().Count() < needed)
            throw new InvalidOperationException($"{entry.Name} needs {needed} distinct usable boundary exits; no walls will be demolished.");
        if (entry.Connection == WFPlanetPoiConnection.Roadside)
            sockets = sockets.OrderBy(socket => site.AccessPaths.TryGetValue(socket, out var path) ? path.Count : 0).Take(1).ToList();
        if (entry.Connection == WFPlanetPoiConnection.Through && entry.MapPath == null)
        {
            var first = sockets[0];
            var second = sockets.FirstOrDefault(socket => socket.Direction != first.Direction && socket.Position != first.Position);
            if (second == null) throw new InvalidOperationException($"{entry.Name} requires distinct outward exit directions.");
            sockets = new List<WFPlanetRoadSocket> { first, second };
        }
        foreach (var unused in site.AccessPaths.Keys.Where(socket => !sockets.Contains(socket)).ToList())
            site.AccessPaths.Remove(unused);
        return sockets;
    }

    /// <summary>Checks authored through entrances share a usable on-site corridor.</summary>
    private void ValidateThrough(EntityUid ground, MapGridComponent grid, WFPlanetPoiSite site, HashSet<Vector2i> tiles)
    {
        var reachable = new HashSet<Vector2i>();
        var queue = new Queue<Vector2i>();
        Vector2i Entrance(WFPlanetRoadSocket socket) => site.AccessPaths.TryGetValue(socket, out var access) ? access[0] : socket.Position;
        queue.Enqueue(Entrance(site.Sockets[0]));
        while (queue.TryDequeue(out var current))
        {
            if (!reachable.Add(current)) continue;
            foreach (var direction in Enum.GetValues<WFPlanetPoiDirection>())
            {
                var next = current + Direction(direction);
                if (tiles.Contains(next) && !reachable.Contains(next) && Usable(ground, grid, next)) queue.Enqueue(next);
            }
        }
        if (site.Sockets.Any(socket => !reachable.Contains(Entrance(socket))))
            throw new InvalidOperationException($"{site.Name} through exits do not share a usable internal corridor.");
    }

    /// <summary>Accepts real floor and operable door tiles while rejecting solid obstacles.</summary>
    private bool Usable(EntityUid ground, MapGridComponent grid, Vector2i tile)
    {
        return !_maps.GetTileRef(ground, grid, tile).Tile.IsEmpty && Unblocked(ground, grid, tile);
    }

    /// <summary>Allows empty exterior ground and traversable door tiles, never solid obstacles.</summary>
    private bool Unblocked(EntityUid ground, MapGridComponent grid, Vector2i tile)
    {
        foreach (var entity in _maps.GetAnchoredEntities((ground, grid), tile))
        {
            if (HasComp<DoorComponent>(entity))
            {
                if (TryComp<DoorBoltComponent>(entity, out var bolt) && bolt.BoltsDown) return false;
                continue;
            }
            if (TryComp<PhysicsComponent>(entity, out var body) && body.CanCollide && (body.CollisionLayer & (int)CollisionGroup.MobMask) != 0) return false;
        }
        return true;
    }

    private static Vector2i Direction(WFPlanetPoiDirection direction) => direction switch
    {
        WFPlanetPoiDirection.North => new Vector2i(0,1), WFPlanetPoiDirection.East => new Vector2i(1,0),
        WFPlanetPoiDirection.South => new Vector2i(0,-1), WFPlanetPoiDirection.West => new Vector2i(-1,0),
        _ => throw new InvalidOperationException("Invalid socket direction."),
    };

    /// <summary>Tiles a road is laid in at a time, so a long one goes over a few ticks.</summary>
    private const int PaintBatch = 512;

    /// <summary>Writes and pins the full road batch with open sky; whatever the biome grew on an unpinned tile goes first.</summary>
    private async Task PaintRoadAsync(EntityUid ground, MapGridComponent grid, BiomeComponent biome, IEnumerable<Vector2i> positions, string prototype)
    {
        var tiles = positions.ToHashSet();
        var tile = new Tile(_tiles[prototype].TileId);
        var anchored = new List<EntityUid>();
        foreach (var position in tiles)
        {
            await _preload.Breathe();
            if (TerminatingOrDeleted(ground))
                return;
            if (_biome.WfIsPinned((ground, biome), position))
                continue;
            anchored.Clear();
            var enumerator = _maps.GetAnchoredEntitiesEnumerator(ground, grid, position);
            while (enumerator.MoveNext(out var uid))
                anchored.Add(uid.Value);
            foreach (var uid in anchored)
                QueueDel(uid);
        }
        // Pinned first, so nothing grows on a stretch still waiting for its road.
        _biome.WfPinTiles((ground, biome), tiles);
        foreach (var batch in tiles.Chunk(PaintBatch))
        {
            await _preload.Breathe();
            if (TerminatingOrDeleted(ground))
                return;
            _maps.SetTiles(ground, grid, batch.Select(position => (position, tile)).ToList());
            foreach (var position in batch) _roof.SetRoof((ground, grid, null), position, false);
        }
    }
}
