using Content.Server._WF.Planets;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.Planets;
using Content.Shared.Chemistry.Components.SolutionManager;
using Content.Shared.Parallax.Biomes;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;
using Robust.Shared.Utility;
using ChunkIndicesEnumerator = Robust.Shared.Map.Enumerators.ChunkIndicesEnumerator;

namespace Content.Server.Parallax;

/// <summary>
/// Unloads planet terrain nobody has been near for a while. Upstream's unloader takes one chunk per biome about every
/// thirty seconds and keeps every entity that differs from its prototype at all, which is nearly all of them, so a
/// planet only ever grew.
/// </summary>
// What stays: the loaded area and a ring past it, ground under and beside any hull, chunks loaded by hand, and
// anything beside a tile someone built on. A biome entity goes only if nothing has touched it since it spawned.
public sealed partial class BiomeSystem
{
    [Dependency] private IGameTiming _wfTiming = default!;

    /// <summary>How many chunks past the loaded area stay loaded, so its edge doesn't reload at every step back.</summary>
    public const int WfKeepRing = 2;

    /// <summary>How many chunks around a hull's bounds stay loaded: a parked ship keeps its ground.</summary>
    public const int WfHullRing = 1;

    /// <summary>Seconds between looks over one layer for chunks that have fallen idle.</summary>
    public const float WfScanInterval = 1f;

    /// <summary>Ticks after it spawned in which a biome entity's own start-up may still have marked it changed.</summary>
    public const uint WfPristineSlack = 1;

    private bool _wfUnloadEnabled;
    private double _wfUnloadIdle;
    private double _wfUnloadBudgetMs;

    private readonly Dictionary<EntityUid, WfLayerUnload> _wfLayers = new();
    private readonly List<EntityUid> _wfDue = new();
    private readonly List<EntityUid> _wfGone = new();
    private readonly List<Vector2i> _wfStale = new();
    private readonly HashSet<Vector2i> _wfKeep = new();
    private readonly List<(Vector2i, Tile)> _wfTiles = new();
    private readonly Stopwatch _wfWatch = new();
    private EntityUid _wfTurnLayer;
    private uint _wfTurn;

    /// <summary>
    /// True while this unloader deletes a chunk's own entities, or the loader a marker it never tracked: neither needs
    /// bookkeeping as it goes.
    /// </summary>
    private bool _wfUnloading;

    /// <summary>One planet layer's idle chunks.</summary>
    private sealed class WfLayerUnload
    {
        public TimeSpan NextScan;

        /// <summary>When each loaded chunk outside the kept set was first seen there.</summary>
        public readonly Dictionary<Vector2i, TimeSpan> IdleSince = new();

        /// <summary>Chunks idle long enough, oldest first.</summary>
        public readonly Queue<Vector2i> Due = new();

        public readonly HashSet<Vector2i> Queued = new();

        /// <summary>Chunks loaded by hand, which stay until unloaded by hand.</summary>
        public readonly HashSet<Vector2i> Held = new();

        /// <summary>Chunks whose unload threw; they are left alone.</summary>
        public readonly HashSet<Vector2i> Broken = new();

        /// <summary>What this layer's spawner rolls are salted with, drawn the first time one is made.</summary>
        public int? RollSalt;
    }

    /// <summary>How many of a layer's loaded chunks are waiting out their idle time or queued to unload.</summary>
    public int WfIdleChunks(EntityUid layer)
    {
        return _wfLayers.TryGetValue(layer, out var state) ? state.IdleSince.Count + state.Queued.Count : 0;
    }

    /// <summary>How many of a layer's chunks have run out their idle time and wait their turn to unload.</summary>
    public int WfQueuedChunks(EntityUid layer)
    {
        return _wfLayers.TryGetValue(layer, out var state) ? state.Queued.Count : 0;
    }

    /// <summary>Reads the pass's settings and picks the layer whose turn it is to unload.</summary>
    // One layer a pass, in turn, so a busy cavern can't spend the whole budget every time.
    private void WfBeginUnload()
    {
        _wfLoadFor = null;
        _wfLoadSpentMs = 0;
        _wfLoadBudgetMs = _configManager.GetCVar(PlanetCVars.TerrainLoadBudget);
        _wfTurnLayer = EntityUid.Invalid;
        _wfUnloadEnabled = _configManager.GetCVar(PlanetCVars.TerrainUnload);
        _wfUnloadIdle = _configManager.GetCVar(PlanetCVars.TerrainUnloadIdle);
        _wfUnloadBudgetMs = _configManager.GetCVar(PlanetCVars.TerrainUnloadBudget);
        _wfDue.Clear();
        _wfGone.Clear();

        // Whether or not anything unloads: a layer's state also holds the salt its rock is rolled with.
        foreach (var (uid, layer) in _wfLayers)
        {
            if (!_biomeQuery.HasComp(uid))
                _wfGone.Add(uid);
            else if (layer.Due.Count > 0)
                _wfDue.Add(uid);
        }

        foreach (var uid in _wfGone)
        {
            _wfLayers.Remove(uid);
        }

        if (!_wfUnloadEnabled || _wfDue.Count == 0)
            return;

        _wfDue.Sort((a, b) => a.Id.CompareTo(b.Id));
        _wfTurnLayer = _wfDue[(int) (_wfTurn++ % (uint) _wfDue.Count)];
    }

    /// <summary>Looks after one planet layer's unloading; false for any other biome, which upstream's unloader keeps.</summary>
    private bool WfUnload(BiomeComponent biome, EntityUid map, MapGridComponent grid)
    {
        if (!_wfUnloadEnabled || !HasComp<WFPlanetLayerComponent>(map))
            return false;

        var layer = _wfLayers.GetOrNew(map);
        var now = _wfTiming.CurTime;

        try
        {
            if (now >= layer.NextScan)
            {
                layer.NextScan = now + TimeSpan.FromSeconds(WfScanInterval);
                WfScan(biome, map, layer, now);
            }

            if (map == _wfTurnLayer)
                WfUnloadDue(biome, map, grid, layer);
        }
        catch (Exception e)
        {
            // Thrown out of Update, this would leave the active sets behind and stop every biome loading.
            Log.Error($"Unloading terrain of {ToPrettyString(map)} failed. {e}");
        }

        return true;
    }

    /// <summary>Starts or clears each loaded chunk's idle clock, and queues those that have run it out.</summary>
    private void WfScan(BiomeComponent biome, EntityUid map, WfLayerUnload layer, TimeSpan now)
    {
        _wfKeep.Clear();

        foreach (var chunk in _activeChunks[biome])
        {
            for (var x = -WfKeepRing; x <= WfKeepRing; x++)
            for (var y = -WfKeepRing; y <= WfKeepRing; y++)
            {
                _wfKeep.Add(chunk + new Vector2i(x, y) * ChunkSize);
            }
        }

        WfKeepUnderWatchers(map);
        WfKeepUnderHulls(map);
        _wfKeep.UnionWith(layer.Held);

        var loaded = biome.LoadedChunks;

        foreach (var chunk in loaded)
        {
            if (_wfKeep.Contains(chunk) || layer.Broken.Contains(chunk))
            {
                // Out of the queue as well: its entry there is skipped when its turn comes.
                layer.IdleSince.Remove(chunk);
                layer.Queued.Remove(chunk);
                continue;
            }

            if (!layer.IdleSince.TryGetValue(chunk, out var since))
            {
                layer.IdleSince[chunk] = now;
                continue;
            }

            if ((now - since).TotalSeconds >= _wfUnloadIdle && layer.Queued.Add(chunk))
                layer.Due.Enqueue(chunk);
        }

        // Chunks something else unloaded leave their clocks behind.
        if (layer.IdleSince.Count <= loaded.Count)
            return;

        _wfStale.Clear();
        foreach (var chunk in layer.IdleSince.Keys)
        {
            if (!loaded.Contains(chunk))
                _wfStale.Add(chunk);
        }

        foreach (var chunk in _wfStale)
        {
            layer.IdleSince.Remove(chunk);
        }
    }

    /// <summary>Keeps what is loaded round every player who is no loader: a ghost loads no ground, and loses none it is watching.</summary>
    private void WfKeepUnderWatchers(EntityUid map)
    {
        foreach (var session in _playerManager.Sessions)
        {
            if (session.AttachedEntity is not { } watcher
                || CanLoad(watcher)
                || !_xformQuery.TryGetComponent(watcher, out var xform)
                || xform.MapUid != map)
                continue;

            var chunks = new ChunkIndicesEnumerator(_loadArea.Translated(_transform.GetWorldPosition(xform)), ChunkSize);

            while (chunks.MoveNext(out var index))
            {
                _wfKeep.Add(index.Value * ChunkSize);
            }
        }
    }

    /// <summary>Keeps the chunks under and beside every hull on a layer.</summary>
    // A parked ship is no loader. With its ground gone it reads as unsupported and whatever held it lets go.
    private void WfKeepUnderHulls(EntityUid map)
    {
        var grids = EntityQueryEnumerator<MapGridComponent, TransformComponent>();

        while (grids.MoveNext(out var uid, out var hull, out var xform))
        {
            if (uid == map || xform.MapUid != map)
                continue;

            var bounds = _transform.GetWorldMatrix(xform).TransformBox(hull.LocalAABB).Enlarged(WfHullRing * ChunkSize);
            var chunks = new ChunkIndicesEnumerator(bounds, ChunkSize);

            while (chunks.MoveNext(out var index))
            {
                _wfKeep.Add(index.Value * ChunkSize);
            }
        }
    }

    /// <summary>Unloads a layer's due chunks, oldest first, until the pass's time is spent; one always goes.</summary>
    private void WfUnloadDue(BiomeComponent biome, EntityUid map, MapGridComponent grid, WfLayerUnload layer)
    {
        var active = _activeChunks[biome];
        _wfWatch.Restart();

        // A hull or a watcher can have come since the scan that queued these, and the next scan is up to a second off.
        _wfKeep.Clear();
        WfKeepUnderWatchers(map);
        WfKeepUnderHulls(map);

        while (layer.Due.TryDequeue(out var chunk))
        {
            // Kept since it was queued: the scan took it out of the queue, and it waits its idle time out again.
            if (!layer.Queued.Remove(chunk))
                continue;

            // Whatever happens next, its clock starts again if it is still loaded and idle.
            layer.IdleSince.Remove(chunk);

            if (!biome.LoadedChunks.Contains(chunk)
                || layer.Held.Contains(chunk)
                || _wfKeep.Contains(chunk)
                || WfNearActive(active, chunk))
                continue;

            // A room keeps the natural walls round it: with them gone it would vent to the planet's air.
            if (_atmos.WfHasBuiltTile(map, chunk - Vector2i.One, chunk + new Vector2i(ChunkSize, ChunkSize)))
                continue;

            try
            {
                WfUnloadChunkPristine(biome, map, grid, chunk);
            }
            catch (Exception e)
            {
                // Thrown out of Update, this would leave the active sets behind and stop every biome loading.
                layer.Broken.Add(chunk);
                Log.Error($"Unloading chunk {chunk} of {ToPrettyString(map)} failed; it stays as it is. {e}");
            }

            if (_wfWatch.Elapsed.TotalMilliseconds >= _wfUnloadBudgetMs)
                break;
        }
    }

    /// <summary>Whether a loader has come back within the kept ring of a chunk since it was queued.</summary>
    private static bool WfNearActive(HashSet<Vector2i> active, Vector2i chunk)
    {
        for (var x = -WfKeepRing; x <= WfKeepRing; x++)
        for (var y = -WfKeepRing; y <= WfKeepRing; y++)
        {
            if (active.Contains(chunk + new Vector2i(x, y) * ChunkSize))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Unloads one chunk as upstream does, except for which of the biome's own entities go: those nothing has touched
    /// since they spawned, rather than those identical to their prototype.
    /// </summary>
    private void WfUnloadChunkPristine(BiomeComponent biome, EntityUid map, MapGridComponent grid, Vector2i chunk)
    {
        biome.ModifiedTiles.TryGetValue(chunk, out var modified);
        modified ??= new HashSet<Vector2i>();
        _wfTiles.Clear();

        // Not loaded from here on, whatever is reached: z-physics reads each tile as it empties, and one emptied on a
        // loaded chunk is a hole.
        biome.LoadedChunks.Remove(chunk);

        try
        {
            UnloadDecals(biome, map, chunk, modified);
            WfPinDecalTiles(map, chunk, modified);
            WfUnloadEntities(biome, map, grid, chunk, modified);
            UnloadTiles(biome, map, grid, chunk, biome.Seed, modified, _wfTiles);
        }
        finally
        {
            if (modified.Count == 0)
                biome.ModifiedTiles.Remove(chunk);
            else
                biome.ModifiedTiles[chunk] = modified;
        }
    }

    /// <summary>Pins each tile still holding a decal once the biome's own are gone, such as a crayon mark or gore.</summary>
    // An emptied tile takes its decals with it, and only the biome's own come back.
    private void WfPinDecalTiles(EntityUid map, Vector2i chunk, HashSet<Vector2i> modified)
    {
        var end = chunk + new Vector2i(ChunkSize, ChunkSize);

        foreach (var (_, decal) in _decals.GetDecalsIntersecting(map, new Box2(chunk, end)))
        {
            var tile = new Vector2i((int) Math.Floor(decal.Coordinates.X), (int) Math.Floor(decal.Coordinates.Y));

            // The box takes in the next chunks' edges.
            if (tile.X >= chunk.X && tile.X < end.X && tile.Y >= chunk.Y && tile.Y < end.Y)
                modified.Add(tile);
        }
    }

    /// <summary>Deletes a chunk's untouched biome entities; the tile of any other is kept as it is.</summary>
    private void WfUnloadEntities(BiomeComponent biome, EntityUid map, MapGridComponent grid, Vector2i chunk, HashSet<Vector2i> modified)
    {
        if (!biome.LoadedEntities.Remove(chunk, out var loaded))
            return;

        _chunkLoaderEntitiesToDelete.Clear();

        foreach (var (uid, tile) in loaded)
        {
            if (!Deleted(uid)
                && _xformQuery.TryGetComponent(uid, out var xform)
                && xform.Anchored
                && _mapSystem.LocalToTile(map, grid, xform.Coordinates) == tile)
            {
                // A pinned tile grows nothing back, so what stands on one stays.
                if (!modified.Contains(tile) && WfIsPristine(uid, xform, map, grid, tile))
                {
                    _chunkLoaderEntitiesToDelete.Add(uid);
                    continue;
                }

                // Still the biome's own where it grew, though the chunk's next load won't list it.
                EnsureComp<WFBiomeGrownComponent>(uid).Tile = tile;
            }

            modified.Add(tile);
        }

        _wfUnloading = true;

        try
        {
            foreach (var uid in _chunkLoaderEntitiesToDelete)
            {
                Del(uid);
            }
        }
        finally
        {
            _wfUnloading = false;
        }
    }

    /// <summary>
    /// Whether nothing has happened to a biome entity since it spawned: nothing marked it changed, nothing is inside or
    /// stuck in it but its own untouched liquid, and nothing else is fixed to its tile.
    /// </summary>
    // Upstream asks whether it still matches its prototype, which a rock wall never does once it has started up.
    private bool WfIsPristine(EntityUid uid, TransformComponent xform, EntityUid map, MapGridComponent grid, Vector2i tile)
    {
        // A spawner that stays has laid things beside itself, and would lay them again each time it came back.
        if (HasComp<WFBiomeKeepComponent>(uid) || WfIsStandingSpawner(uid))
            return false;

        if (!WfIsUntouched(uid))
            return false;

        // Water keeps its pool as an entity inside it; drawing from the pool marks the pool, not the water.
        var children = xform.ChildEnumerator;

        while (children.MoveNext(out var child))
        {
            if (!TryComp<ContainedSolutionComponent>(child, out var pool)
                || pool.Container != uid
                || !WfIsUntouched(child)
                || !_xformQuery.TryGetComponent(child, out var childXform)
                || childXform.ChildCount != 0)
                return false;
        }

        // A light on a rock wall or a cable run under it stays where it is, so the rock has to as well.
        var anchored = _mapSystem.GetAnchoredEntitiesEnumerator(map, grid, tile);
        var count = 0;

        while (anchored.MoveNext(out _))
        {
            if (++count > 1)
                return false;
        }

        return true;
    }

    /// <summary>Whether nothing has marked an entity changed since the tick it spawned in, or the one after.</summary>
    private bool WfIsUntouched(EntityUid uid)
    {
        var meta = MetaData(uid);
        return meta.EntityLastModifiedTick.Value <= meta.CreationTick.Value + WfPristineSlack;
    }
}
