using System;
using System.Collections.Generic;
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

        state = new AtmosChunkState();
        state.NextColdCycle = atmosphere.UpdateCounter;
        atmosphere.Chunks[chunk] = state;
        return state;
    }

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
    }

    private void AddInvalidatedTile(GridAtmosphereComponent atmosphere, Vector2i tile)
    {
        atmosphere.InvalidatedCoords.Add(tile);
        TouchChunk(atmosphere, tile);
        GetOrCreateChunkState(atmosphere, GetAtmosChunk(tile)).InvalidatedCoords.Add(tile);
        MarkChunkHaloDirty(atmosphere, tile);
    }

    private void MarkChunkHaloDirty(GridAtmosphereComponent atmosphere, Vector2i tile)
    {
        var chunk = GetAtmosChunk(tile);
        var localX = Math.Abs(tile.X % SharedGasTileOverlaySystem.ChunkSize);
        var localY = Math.Abs(tile.Y % SharedGasTileOverlaySystem.ChunkSize);

        if (localX == 0)
            GetOrCreateChunkState(atmosphere, chunk + new Vector2i(-1, 0)).LastTouchedCycle = atmosphere.UpdateCounter;
        else if (localX == SharedGasTileOverlaySystem.ChunkSize - 1)
            GetOrCreateChunkState(atmosphere, chunk + new Vector2i(1, 0)).LastTouchedCycle = atmosphere.UpdateCounter;

        if (localY == 0)
            GetOrCreateChunkState(atmosphere, chunk + new Vector2i(0, -1)).LastTouchedCycle = atmosphere.UpdateCounter;
        else if (localY == SharedGasTileOverlaySystem.ChunkSize - 1)
            GetOrCreateChunkState(atmosphere, chunk + new Vector2i(0, 1)).LastTouchedCycle = atmosphere.UpdateCounter;
    }

    private void AddChunkTile(HashSet<TileAtmosphere> globalSet, HashSet<TileAtmosphere> chunkSet, TileAtmosphere tile)
    {
        globalSet.Add(tile);
        chunkSet.Add(tile);
    }

    private void RemoveChunkTile(HashSet<TileAtmosphere> globalSet, HashSet<TileAtmosphere> chunkSet, TileAtmosphere tile)
    {
        globalSet.Remove(tile);
        chunkSet.Remove(tile);
    }

    private void RefreshInterestChunks()
    {
        foreach (var set in _interestChunks.Values)
            set.Clear();
        _interestChunks.Clear();
        _interestSessions.Clear();

        foreach (var session in _playerManager.Sessions)
        {
            if (session.Status != SessionStatus.InGame)
                continue;

            _interestSessions.Add(session);
        }

        foreach (var session in _interestSessions)
        {
            var chunks = _chunkingSystem.GetChunksForSession(
                session,
                SharedGasTileOverlaySystem.ChunkSize,
                _chunkIndexPool,
                _chunkViewerPool);

            foreach (var (grid, indices) in chunks)
            {
                if (!_interestChunks.TryGetValue(grid, out var aggregate))
                {
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
