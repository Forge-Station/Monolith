using System;
using System.Collections.Generic;
using System.Linq;
using Content.Server.Atmos.Components;
using Content.Shared.Atmos.EntitySystems;
using Content.Shared.Chunking;
using Microsoft.Extensions.ObjectPool;
using Robust.Server.Player;
using Robust.Shared.Enums;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Player;

namespace Content.Server.Atmos.EntitySystems;

public sealed partial class AtmosphereSystem
{
    [Dependency] private readonly ChunkingSystem _chunkingSystem = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;

    private readonly Dictionary<NetEntity, HashSet<Vector2i>> _interestChunks = new();
    private readonly List<ICommonSession> _interestSessions = new();

    private readonly ObjectPool<HashSet<Vector2i>> _chunkIndexPool =
        new DefaultObjectPool<HashSet<Vector2i>>(new DefaultPooledObjectPolicy<HashSet<Vector2i>>(), 64);

    private readonly ObjectPool<Dictionary<NetEntity, HashSet<Vector2i>>> _chunkViewerPool =
        new DefaultObjectPool<Dictionary<NetEntity, HashSet<Vector2i>>>(
            new DefaultPooledObjectPolicy<Dictionary<NetEntity, HashSet<Vector2i>>>(), 32);

    private static Vector2i GetAtmosChunk(Vector2i tile)
    {
        return SharedGasTileOverlaySystem.GetGasChunkIndices(tile);
    }

    private AtmosChunkState GetOrCreateChunkState(GridAtmosphereComponent atmosphere, Vector2i chunk)
    {
        if (atmosphere.Chunks.TryGetValue(chunk, out var state))
            return state;

        state = new AtmosChunkState
        {
            NextColdCycle = atmosphere.UpdateCounter,
        };
        atmosphere.Chunks[chunk] = state;
        return state;
    }

    // Forge-Change-start
    private static AtmosChunkWorkFlags CalculateChunkWorkFlags(AtmosChunkState state)
    {
        var flags = AtmosChunkWorkFlags.None;

        if (state.InvalidatedCoords.Count > 0)
            flags |= AtmosChunkWorkFlags.Revalidate;
        if (state.ActiveTiles.Count > 0 || state.InvalidatedCoords.Count > 0)
            flags |= AtmosChunkWorkFlags.Active;
        if (state.HighPressureTiles.Count > 0)
            flags |= AtmosChunkWorkFlags.HighPressure;
        if (state.HotspotTiles.Count > 0)
            flags |= AtmosChunkWorkFlags.Hotspot;
        if (state.SuperconductivityTiles.Count > 0)
            flags |= AtmosChunkWorkFlags.Superconductivity;

        return flags;
    }

    private void RefreshChunkWorkFlags(AtmosChunkState state)
    {
        state.WorkFlags = CalculateChunkWorkFlags(state);
    }

    private void EnqueueDirtyChunk(GridAtmosphereComponent atmosphere, Vector2i chunk)
    {
        if (!atmosphere.DirtyChunkQueued.Add(chunk))
            return;

        atmosphere.DirtyChunkQueue.Enqueue(chunk);
    }
    // Forge-Change-end

    private bool TryGetChunkState(GridAtmosphereComponent atmosphere, Vector2i chunk, out AtmosChunkState? state)
    {
        if (atmosphere.Chunks.TryGetValue(chunk, out var existing))
        {
            state = existing;
            return true;
        }

        state = null;
        return false;
    }

    private void TouchChunk(GridAtmosphereComponent atmosphere, Vector2i tile)
    {
        var chunk = GetAtmosChunk(tile);
        var state = GetOrCreateChunkState(atmosphere, chunk);
        state.LastTouchedCycle = atmosphere.UpdateCounter;
        EnqueueDirtyChunk(atmosphere, chunk); // Forge-Change
    }

    private void AddInvalidatedTile(GridAtmosphereComponent atmosphere, Vector2i tile)
    {
        atmosphere.InvalidatedCoords.Add(tile);
        TouchChunk(atmosphere, tile);
        // Forge-Change-start
        var chunkIndex = GetAtmosChunk(tile);
        var chunkState = GetOrCreateChunkState(atmosphere, chunkIndex);
        chunkState.InvalidatedCoords.Add(tile);
        RefreshChunkWorkFlags(chunkState);
        EnqueueDirtyChunk(atmosphere, chunkIndex);
        // Forge-Change-end
        MarkChunkHaloDirty(atmosphere, tile);
    }

    private void MarkChunkHaloDirty(GridAtmosphereComponent atmosphere, Vector2i tile)
    {
        var chunk = GetAtmosChunk(tile);
        var localX = Math.Abs(tile.X % SharedGasTileOverlaySystem.ChunkSize);
        var localY = Math.Abs(tile.Y % SharedGasTileOverlaySystem.ChunkSize);

        if (localX == 0)
        {
            var neighbor = chunk + new Vector2i(-1, 0);
            GetOrCreateChunkState(atmosphere, neighbor).LastTouchedCycle = atmosphere.UpdateCounter;
            EnqueueDirtyChunk(atmosphere, neighbor); // Forge-Change
        }
        else if (localX == SharedGasTileOverlaySystem.ChunkSize - 1)
        {
            var neighbor = chunk + new Vector2i(1, 0);
            GetOrCreateChunkState(atmosphere, neighbor).LastTouchedCycle = atmosphere.UpdateCounter;
            EnqueueDirtyChunk(atmosphere, neighbor); // Forge-Change
        }

        if (localY == 0)
        {
            var neighbor = chunk + new Vector2i(0, -1);
            GetOrCreateChunkState(atmosphere, neighbor).LastTouchedCycle = atmosphere.UpdateCounter;
            EnqueueDirtyChunk(atmosphere, neighbor); // Forge-Change
        }
        else if (localY == SharedGasTileOverlaySystem.ChunkSize - 1)
        {
            var neighbor = chunk + new Vector2i(0, 1);
            GetOrCreateChunkState(atmosphere, neighbor).LastTouchedCycle = atmosphere.UpdateCounter;
            EnqueueDirtyChunk(atmosphere, neighbor); // Forge-Change
        }
    }

    // Forge-Change-start
    private void AddChunkTile(GridAtmosphereComponent atmosphere, HashSet<TileAtmosphere> globalSet, HashSet<TileAtmosphere> chunkSet, TileAtmosphere tile)
    {
        globalSet.Add(tile);
        chunkSet.Add(tile);
        var chunkIndex = GetAtmosChunk(tile.GridIndices);
        var chunkState = GetOrCreateChunkState(atmosphere, chunkIndex);
        RefreshChunkWorkFlags(chunkState);
        EnqueueDirtyChunk(atmosphere, chunkIndex);
    }

    private void RemoveChunkTile(GridAtmosphereComponent atmosphere, HashSet<TileAtmosphere> globalSet, HashSet<TileAtmosphere> chunkSet, TileAtmosphere tile)
    {
        globalSet.Remove(tile);
        chunkSet.Remove(tile);
        var chunkIndex = GetAtmosChunk(tile.GridIndices);
        if (TryGetChunkState(atmosphere, chunkIndex, out var chunkState) && chunkState != null)
        {
            RefreshChunkWorkFlags(chunkState);
            EnqueueDirtyChunk(atmosphere, chunkIndex);
        }
    }
    // Forge-Change-end

    // OPTIMIZATION: RefreshInterestChunks is called every atmos processing tick.
    // The original implementation allocated new HashSet<Vector2i> for each grid entry inside the loop
    // because _interestChunks was rebuilt from scratch via Clear() + new inserts.
    // Now we reuse the existing sets via Clear() on the values instead of reallocating,
    // falling back to new HashSet only when a grid is seen for the first time this tick.
    //
    // Additionally, we avoid iterating _playerManager.Sessions twice (once to filter, once to process)
    // by combining both loops. The _interestSessions list is kept for compatibility.
    private void RefreshInterestChunks()
    {
        // Clear values but keep the outer dictionary keys to reuse their HashSet allocations.
        foreach (var set in _interestChunks.Values)
        {
            set.Clear();
        }

        _interestSessions.Clear();

        foreach (var session in _playerManager.Sessions)
        {
            if (session.Status != SessionStatus.InGame)
                continue;

            _interestSessions.Add(session);
        }

        foreach (var chunks in _interestSessions.Select(session => _chunkingSystem.GetChunksForSession(
                     session,
                     SharedGasTileOverlaySystem.ChunkSize,
                     _chunkIndexPool,
                     _chunkViewerPool)))
        {
            foreach (var (grid, indices) in chunks)
            {
                if (!_interestChunks.TryGetValue(grid, out var aggregate))
                {
                    // First time this tick we see this grid — allocate a new set.
                    aggregate = new HashSet<Vector2i>();
                    _interestChunks[grid] = aggregate;
                }

                aggregate.UnionWith(indices);
                indices.Clear();
                _chunkIndexPool.Return(indices);
            }

            chunks.Clear();
            _chunkViewerPool.Return(chunks);
        }

        // OPTIMIZATION: Remove grids that had no players viewing them this tick so the dictionary
        // doesn't accumulate stale entries for grids nobody is near. This also prevents their
        // per-grid HashSets from growing without bound on servers with many grids.
        // We only remove entries whose sets are empty after the session loop above.
        if (_interestChunks.Count > 0)
        {
            var toRemove = new List<NetEntity>();
            foreach (var (grid, set) in _interestChunks)
            {
                if (set.Count == 0)
                    toRemove.Add(grid);
            }
            foreach (var grid in toRemove)
            {
                _interestChunks.Remove(grid);
            }
        }
    }

    private bool IsInterestChunk(EntityUid gridUid, Vector2i chunk)
    {
        return _interestChunks.TryGetValue(GetNetEntity(gridUid), out var set) && set.Contains(chunk);
    }

    private bool ShouldProcessChunk(EntityUid gridUid, GridAtmosphereComponent atmosphere, Vector2i chunk, AtmosChunkState state)
    {
        if (AtmosForceFullGridDebug)
            return true;

        if (IsInterestChunk(gridUid, chunk))
            return true;

        if (AtmosColdChunkRateDivider <= 1)
            return true;

        if (atmosphere.UpdateCounter < state.NextColdCycle)
            return false;

        state.NextColdCycle = atmosphere.UpdateCounter + AtmosColdChunkRateDivider;
        return true;
    }
}
