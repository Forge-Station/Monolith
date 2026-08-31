using System.Numerics;
using Content.Client.Decals.Overlays;
using Content.Shared.CCVar;
using Content.Shared.Decals;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Configuration;
using Robust.Shared.GameStates;
using Robust.Shared.Map.Enumerators;
using Robust.Shared.Utility;
using static Content.Shared.Decals.DecalGridComponent;

namespace Content.Client.Decals
{
    public sealed partial class DecalSystem : SharedDecalSystem
    {
        [Dependency] private readonly IOverlayManager _overlayManager = default!;
        [Dependency] private readonly SpriteSystem _sprites = default!;
        [Dependency] private readonly IConfigurationManager _cfg = default!;

        private DecalOverlay? _overlay;

        private HashSet<uint> _removedUids = new();
        private readonly List<Vector2i> _removedChunks = new();

        public override void Initialize()
        {
            base.Initialize();

            _overlay = new DecalOverlay(_sprites, EntityManager, PrototypeManager);
            _overlay.MaxPerTileDraw = _cfg.GetCVar(CCVars.DecalsMaxPerTile);
            _overlay.RemoveIdenticalDuplicates = _cfg.GetCVar(CCVars.DecalsClientDeduplicateIdentical);
            _overlayManager.AddOverlay(_overlay);

            Subs.CVar(_cfg, CCVars.DecalsMaxPerTile, v =>
            {
                if (_overlay == null)
                    return;
                _overlay.MaxPerTileDraw = v;
                _overlay.ClearPreparedCache();
            }, true);

            Subs.CVar(_cfg, CCVars.DecalsClientDeduplicateIdentical, v =>
            {
                if (_overlay == null)
                    return;
                _overlay.RemoveIdenticalDuplicates = v;
                _overlay.ClearPreparedCache();
            }, true);

            SubscribeLocalEvent<DecalGridComponent, ComponentHandleState>(OnHandleState);
            SubscribeLocalEvent<DecalGridComponent, ComponentRemove>(OnDecalGridRemoved); // Forge-Change: drop overlay cache on grid remove
            SubscribeNetworkEvent<DecalChunkUpdateEvent>(OnChunkUpdate);
        }

        // Forge-Change-Start: prepared decal chunks leaked after the grid entity was deleted.
        private void OnDecalGridRemoved(EntityUid uid, DecalGridComponent component, ComponentRemove args)
        {
            _overlay?.RemoveGrid(uid);
        }
        // Forge-Change-End

        public void ToggleOverlay()
        {
            if (_overlay == null)
                return;

            if (_overlayManager.HasOverlay<DecalOverlay>())
            {
                _overlayManager.RemoveOverlay(_overlay);
            }
            else
            {
                _overlayManager.AddOverlay(_overlay);
            }
        }

        public override void Shutdown()
        {
            base.Shutdown();

            if (_overlay == null)
                return;

            _overlayManager.RemoveOverlay(_overlay);
        }

        protected override void OnDecalPrototypesReloaded(Robust.Shared.Prototypes.PrototypesReloadedEventArgs args)
        {
            // Sprite paths or snap-cardinals flags may have changed — clear the overlay caches.
            _overlay?.ClearPreparedCache();
        }

        protected override void OnDecalRemoved(EntityUid gridId, uint decalId, DecalGridComponent component, Vector2i indices, DecalChunk chunk)
        {
            base.OnDecalRemoved(gridId, decalId, component, indices, chunk);
            DebugTools.Assert(chunk.Decals.ContainsKey(decalId));
            chunk.Decals.Remove(decalId);
        }

        private void OnHandleState(EntityUid gridUid, DecalGridComponent gridComp, ref ComponentHandleState args)
        {
            // is this a delta or full state?
            _removedChunks.Clear();
            Dictionary<Vector2i, DecalChunk> modifiedChunks;

            switch (args.Current)
            {
                case DecalGridDeltaState delta:
                {
                    modifiedChunks = delta.ModifiedChunks;
                    foreach (var key in gridComp.ChunkCollection.ChunkCollection.Keys)
                    {
                        if (!delta.AllChunks.Contains(key))
                            _removedChunks.Add(key);
                    }

                    break;
                }
                case DecalGridState state:
                {
                    modifiedChunks = state.Chunks;
                    foreach (var key in gridComp.ChunkCollection.ChunkCollection.Keys)
                    {
                        if (!state.Chunks.ContainsKey(key))
                            _removedChunks.Add(key);
                    }

                    break;
                }
                default:
                    return;
            }

            if (_removedChunks.Count > 0)
                RemoveChunks(gridUid, gridComp, _removedChunks);

            if (modifiedChunks.Count > 0)
            {
                UpdateChunks(gridUid, gridComp, modifiedChunks);
                _overlay?.InvalidateGridChunks(gridUid, modifiedChunks.Keys);
            }
        }

        private void OnChunkUpdate(DecalChunkUpdateEvent ev)
        {
            foreach (var (netGrid, updatedGridChunks) in ev.Data)
            {
                if (updatedGridChunks.Count == 0)
                    continue;

                var gridId = GetEntity(netGrid);

                if (!TryComp(gridId, out DecalGridComponent? gridComp))
                {
                    Log.Error($"Received decal information for an entity without a decal component: {ToPrettyString(gridId)}");
                    continue;
                }

                ApplyChunkDeltas(gridId, gridComp, updatedGridChunks);
            }

            // Now we'll cull old chunks out of range as the server will send them to us anyway.
            foreach (var (netGrid, chunks) in ev.RemovedChunks)
            {
                if (chunks.Count == 0)
                    continue;

                var gridId = GetEntity(netGrid);

                if (!TryComp(gridId, out DecalGridComponent? gridComp))
                {
                    Log.Error($"Received decal information for an entity without a decal component: {ToPrettyString(gridId)}");
                    continue;
                }

                RemoveChunks(gridId, gridComp, chunks);
            }
        }

        private void ApplyChunkDeltas(EntityUid gridId, DecalGridComponent gridComp, Dictionary<Vector2i, DecalChunkDelta> updatedGridChunks)
        {
            var chunkCollection = gridComp.ChunkCollection.ChunkCollection;
            var touched = new List<Vector2i>(updatedGridChunks.Count);

            foreach (var (indices, delta) in updatedGridChunks)
            {
                if (!chunkCollection.TryGetValue(indices, out var chunk) || delta.ResetChunk)
                {
                    chunk = new DecalChunk();
                    chunkCollection[indices] = chunk;
                }

                if (delta.RemovedDecals.Count > 0)
                {
                    foreach (var removedUid in delta.RemovedDecals)
                    {
                        if (!chunk.Decals.ContainsKey(removedUid))
                            continue;

                        OnDecalRemoved(gridId, removedUid, gridComp, indices, chunk);
                        gridComp.DecalIndex.Remove(removedUid);
                    }
                }

                foreach (var (uid, netDecal) in delta.Upserts)
                {
                    var decal = FromNetDecalData(indices, netDecal);
                    chunk.Decals[uid] = decal;
                    gridComp.DecalIndex[uid] = indices;
                }

                if (chunk.Decals.Count == 0)
                    chunkCollection.Remove(indices);

                touched.Add(indices);
            }

            if (touched.Count > 0)
                _overlay?.InvalidateGridChunks(gridId, touched);
        }

        private Decal FromNetDecalData(Vector2i chunkIndices, NetDecalData netDecal)
        {
            var coords = new Vector2(
                chunkIndices.X * ChunkSize + DequantizeDecalCoord(netDecal.RelX),
                chunkIndices.Y * ChunkSize + DequantizeDecalCoord(netDecal.RelY));

            return new Decal(
                coords,
                GetDecalPrototypeId(netDecal.PrototypeNetId),
                netDecal.Color,
                netDecal.Angle,
                netDecal.ZIndex,
                netDecal.Cleanable);
        }

        private void UpdateChunks(EntityUid gridId, DecalGridComponent gridComp, Dictionary<Vector2i, DecalChunk> updatedGridChunks)
        {
            var chunkCollection = gridComp.ChunkCollection.ChunkCollection;

            // Update any existing data / remove decals we didn't receive data for.
            foreach (var (indices, newChunkData) in updatedGridChunks)
            {
                if (chunkCollection.TryGetValue(indices, out var chunk))
                {
                    _removedUids.Clear();
                    _removedUids.UnionWith(chunk.Decals.Keys);
                    _removedUids.ExceptWith(newChunkData.Decals.Keys);
                    foreach (var removedUid in _removedUids)
                    {
                        OnDecalRemoved(gridId, removedUid, gridComp, indices, chunk);
                        gridComp.DecalIndex.Remove(removedUid);
                    }
                }

                chunkCollection[indices] = newChunkData;

                foreach (var (uid, decal) in newChunkData.Decals)
                {
                    gridComp.DecalIndex[uid] = indices;
                }
            }
        }

        private void RemoveChunks(EntityUid gridId, DecalGridComponent gridComp, IEnumerable<Vector2i> chunks)
        {
            var chunkCollection = gridComp.ChunkCollection.ChunkCollection;

            foreach (var index in chunks)
            {
                if (!chunkCollection.TryGetValue(index, out var chunk))
                    continue;

                foreach (var decalId  in chunk.Decals.Keys)
                {
                    OnDecalRemoved(gridId, decalId, gridComp, index, chunk);
                    gridComp.DecalIndex.Remove(decalId);
                }

                chunkCollection.Remove(index);
            }

            _overlay?.InvalidateGridChunks(gridId, chunks);
        }

        // Forge-Change-Start: allow the mapping palette eyedropper to read decals on the client.
        public override HashSet<(uint Index, Decal Decal)> GetDecalsInRange(EntityUid gridId, Vector2 position, float distance = 0.75f, Func<Decal, bool>? validDelegate = null)
        {
            var decalIds = new HashSet<(uint, Decal)>();
            var chunkCollection = ChunkCollection(gridId);
            var chunkIndices = GetChunkIndices(position);
            if (chunkCollection == null || !chunkCollection.TryGetValue(chunkIndices, out var chunk))
                return decalIds;

            foreach (var (uid, decal) in chunk.Decals)
            {
                if ((position - decal.Coordinates - new Vector2(0.5f, 0.5f)).Length() > distance)
                    continue;

                if (validDelegate == null || validDelegate(decal))
                    decalIds.Add((uid, decal));
            }

            return decalIds;
        }

        /// <summary>
        ///     Eyedropper lookup: neighbouring chunks included, so clicks near chunk edges still hit.
        /// </summary>
        public HashSet<(uint Index, Decal Decal)> GetDecalsNear(EntityUid gridId, Vector2 position, float range = 1.25f)
        {
            var decalIds = new HashSet<(uint, Decal)>();
            var chunkCollection = ChunkCollection(gridId);
            if (chunkCollection == null)
                return decalIds;

            var rangeVec = new Vector2(range, range);
            var bounds = new Box2(position - rangeVec, position + rangeVec);
            var chunks = new ChunkIndicesEnumerator(bounds, ChunkSize);

            while (chunks.MoveNext(out var chunkOrigin))
            {
                if (!chunkCollection.TryGetValue(chunkOrigin.Value, out var chunk))
                    continue;

                foreach (var (uid, decal) in chunk.Decals)
                {
                    // Sprites are drawn from Coordinates as the bottom-left; keep anything the cursor could sit on.
                    var origin = decal.Coordinates;
                    if (position.X < origin.X - 0.15f || position.Y < origin.Y - 0.15f)
                        continue;
                    if (position.X > origin.X + range || position.Y > origin.Y + range)
                        continue;

                    decalIds.Add((uid, decal));
                }
            }

            return decalIds;
        }
        // Forge-Change-End
    }
}
