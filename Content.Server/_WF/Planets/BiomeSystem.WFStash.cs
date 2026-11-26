using Content.Shared._WF.CCVar;
using Content.Shared._WF.Planets;
using Content.Shared.Parallax.Biomes;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Timing;
using Robust.Shared.Utility;
using ChunkIndicesEnumerator = Robust.Shared.Map.Enumerators.ChunkIndicesEnumerator;

namespace Content.Server.Parallax;

/// <summary>
/// Puts away a preloaded ground's untouched biome entities where nobody is near, keeping its tiles and decals, and
/// lays them again from the recipe when someone comes back. The whole world's ground still exists; only the trees,
/// rocks and plants nobody can see do not.
/// </summary>
// A touched entity, a pinned tile and anything built stay, as the unloader keeps them. Stashed chunks stay in
// LoadedChunks, since their tiles are there; their LoadedEntities entry is gone until they are laid again.
public sealed partial class BiomeSystem
{
    [Dependency] private EntityLookupSystem _wfLookup = default!;

    /// <summary>Per stashed chunk to lay again this pass: how far from its nearest loader, and whether it goes at once.</summary>
    private readonly List<(int Distance, Vector2i Chunk, bool Now)> _wfRestores = new();

    private readonly HashSet<Vector2i> _wfStashKeep = new();
    private readonly HashSet<Vector2i> _wfOccupied = new();
    private readonly HashSet<EntityUid> _wfOccupants = new();
    private EntityUid _wfStashKeepMap;
    private GameTick _wfStashKeepTick;
    private BiomeComponent? _wfRestoreFor;

    /// <summary>Whether stashing is on for preloaded grounds.</summary>
    public bool WfStashEnabled => _configManager.GetCVar(PlanetCVars.TerrainStash);

    /// <summary>How many of a layer's chunks have their biome entities put away.</summary>
    public int WfStashedChunks(EntityUid layer)
    {
        return _wfLayers.TryGetValue(layer, out var state) ? state.Stashed.Count : 0;
    }

    /// <summary>Whether a chunk of a layer has its biome entities put away.</summary>
    public bool WfIsStashed(EntityUid layer, Vector2i chunkOrigin)
    {
        return _wfLayers.TryGetValue(layer, out var state) && state.Stashed.Contains(chunkOrigin);
    }

    /// <summary>
    /// Puts away a loaded chunk's untouched biome entities if nobody is near it and nothing built stands beside it;
    /// false when it stays as it is.
    /// </summary>
    public bool WfStashIfIdle(Entity<BiomeComponent, MapGridComponent> ground, Vector2i chunkOrigin)
    {
        if (!ground.Comp1.LoadedEntities.ContainsKey(chunkOrigin))
            return false;

        WfRefreshStashKeep(ground.Owner);
        if (_wfStashKeep.Contains(chunkOrigin))
            return false;

        return WfStashChunk(ground.Comp1, ground.Owner, ground.Comp2, chunkOrigin, checkTiles: false);
    }

    /// <summary>Pins every tile of a chunk that is no longer the one the biome grows there.</summary>
    private void WfPinChangedTiles(BiomeComponent biome, EntityUid map, MapGridComponent grid, Vector2i chunk, HashSet<Vector2i> modified)
    {
        for (var x = chunk.X; x < chunk.X + ChunkSize; x++)
        for (var y = chunk.Y; y < chunk.Y + ChunkSize; y++)
        {
            var tile = new Vector2i(x, y);
            if (modified.Contains(tile))
                continue;

            if (!TryGetBiomeTile(tile, biome.Layers, biome.Seed, null, out var grown)
                || !_mapSystem.TryGetTileRef(map, grid, tile, out var current)
                || current.Tile != grown.Value)
                modified.Add(tile);
        }
    }

    /// <summary>What near players and under hulls keeps its entities, worked out once a tick per layer.</summary>
    private void WfRefreshStashKeep(EntityUid map)
    {
        if (_wfStashKeepMap == map && _wfStashKeepTick == _wfTiming.CurTick)
            return;

        _wfStashKeepMap = map;
        _wfStashKeepTick = _wfTiming.CurTick;
        _wfStashKeep.Clear();

        foreach (var session in _playerManager.Sessions)
        {
            if (session.AttachedEntity is not { } body
                || !_xformQuery.TryGetComponent(body, out var xform)
                || xform.MapUid != map)
                continue;

            var area = _loadArea.Enlarged(WfKeepRing * ChunkSize).Translated(_transform.GetWorldPosition(xform));
            var chunks = new ChunkIndicesEnumerator(area, ChunkSize);
            while (chunks.MoveNext(out var index))
            {
                _wfStashKeep.Add(index.Value * ChunkSize);
            }
        }

        // WfKeepUnderHulls fills the unloader's own set; borrowed here and handed back empty.
        _wfKeep.Clear();
        WfKeepUnderHulls(map);
        _wfStashKeep.UnionWith(_wfKeep);
        _wfKeep.Clear();
    }

    /// <summary>Puts away one chunk's untouched biome entities; a room beside it keeps them, as it keeps its walls.</summary>
    private bool WfStashChunk(BiomeComponent biome, EntityUid map, MapGridComponent grid, Vector2i chunk, bool checkTiles = true)
    {
        if (_atmos.WfHasBuiltTile(map, chunk - Vector2i.One, chunk + new Vector2i(ChunkSize, ChunkSize)))
            return false;

        biome.ModifiedTiles.TryGetValue(chunk, out var modified);
        modified ??= new HashSet<Vector2i>();

        try
        {
            WfUnloadEntities(biome, map, grid, chunk, modified);

            // Ground changed since it grew, dug out or floored over, grows nothing when the rest comes back. The
            // preload's own stash skips this: what it laid over the biome's ground is pinned already.
            if (checkTiles)
                WfPinChangedTiles(biome, map, grid, chunk, modified);
        }
        finally
        {
            if (modified.Count == 0)
                biome.ModifiedTiles.Remove(chunk);
            else
                biome.ModifiedTiles[chunk] = modified;
        }

        var state = _wfLayers.GetOrNew(map);
        state.Stashed.Add(chunk);
        state.IdleSince.Remove(chunk);
        state.Queued.Remove(chunk);
        return true;
    }

    /// <summary>Notes a stashed chunk a loader wants back; it is laid in <see cref="WfRestoreStashed"/>.</summary>
    private void WfNoteRestore(BiomeComponent biome, EntityUid map, Vector2i chunk)
    {
        if (!_wfLayers.TryGetValue(map, out var state) || !state.Stashed.Contains(chunk))
            return;

        if (!ReferenceEquals(_wfRestoreFor, biome))
        {
            _wfRestoreFor = biome;
            _wfRestores.Clear();
        }

        if (_wfLoadLoaders == null)
        {
            _wfRestores.Add((0, chunk, true));
            return;
        }

        var block = WfBlockOf(chunk);
        _wfRestores.Add((WfBlockDistance(block, _wfLoadLoaders), chunk, _wfNearBlocks.Contains(block)));
    }

    /// <summary>Lays what a layer's loaders want back: those beside them at once, the rest nearest first within the pass's budget.</summary>
    private void WfRestoreStashed(BiomeComponent biome, EntityUid map, MapGridComponent grid)
    {
        if (!ReferenceEquals(_wfRestoreFor, biome) || _wfRestores.Count == 0)
            return;

        _wfRestoreFor = null;
        _wfRestores.Sort(static (a, b) =>
        {
            var order = b.Now.CompareTo(a.Now);
            if (order == 0)
                order = a.Distance.CompareTo(b.Distance);
            if (order == 0)
                order = a.Chunk.X.CompareTo(b.Chunk.X);
            return order != 0 ? order : a.Chunk.Y.CompareTo(b.Chunk.Y);
        });

        try
        {
            var laid = 0;
            foreach (var (_, chunk, now) in _wfRestores)
            {
                if (!now && laid > 0 && _wfLoadSpentMs >= _wfLoadBudgetMs)
                    break;

                _wfLoadWatch.Restart();
                WfRestoreChunk(biome, map, grid, chunk);
                _wfLoadSpentMs += _wfLoadWatch.Elapsed.TotalMilliseconds;
                laid++;
            }
        }
        catch (Exception e)
        {
            Log.Error($"Laying stashed chunks of {ToPrettyString(map)} again failed. {e}");
        }
        finally
        {
            _wfRestores.Clear();
        }
    }

    /// <summary>Lays a stashed chunk's biome entities again, round whoever stands where one would go.</summary>
    private void WfRestoreChunk(BiomeComponent biome, EntityUid map, MapGridComponent grid, Vector2i chunk)
    {
        if (!_wfLayers.TryGetValue(map, out var state) || !state.Stashed.Remove(chunk) || biome.LoadedEntities.ContainsKey(chunk))
            return;

        biome.ModifiedTiles.TryGetValue(chunk, out var modified);
        modified ??= _tilePool.Get();

        // Someone who walked onto a stashed tile isn't walled in: the tile goes without, this time only.
        WfOccupiedTiles(map, grid, chunk);
        _wfOccupied.ExceptWith(modified);
        modified.UnionWith(_wfOccupied);

        try
        {
            LoadEntities(biome, map, grid, chunk, biome.Seed, modified);
        }
        finally
        {
            modified.ExceptWith(_wfOccupied);
            FinalizeChunk(biome, chunk, modified);
        }
    }

    /// <summary>Fills <see cref="_wfOccupied"/> with the chunk's tiles under something solid that moves.</summary>
    private void WfOccupiedTiles(EntityUid map, MapGridComponent grid, Vector2i chunk)
    {
        _wfOccupied.Clear();
        _wfOccupants.Clear();
        var box = new Box2(chunk, chunk + new Vector2i(ChunkSize, ChunkSize));
        _wfLookup.GetLocalEntitiesIntersecting(map, box, _wfOccupants, LookupFlags.Dynamic);

        foreach (var uid in _wfOccupants)
        {
            if (!TryComp<PhysicsComponent>(uid, out var body) || !body.CanCollide || !body.Hard
                || !_xformQuery.TryGetComponent(uid, out var xform) || xform.ParentUid != map)
                continue;

            _wfOccupied.Add(_mapSystem.LocalToTile(map, grid, xform.Coordinates));
        }
    }
}
