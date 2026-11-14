using Content.Shared.Parallax.Biomes;
using Robust.Shared.Map;

namespace Content.Server.Parallax;

/// <summary>Cavern access to BiomeComponent's chunk bookkeeping.</summary>
public sealed partial class BiomeSystem
{
    /// <summary>Whether an entity's view may generate terrain: anything but a ghost without the loading tag.</summary>
    public bool WfCanLoad(EntityUid uid)
    {
        return CanLoad(uid);
    }

    /// <summary>Half the side of the box around a loader whose chunks load, in tiles.</summary>
    public float WfLoadRange => _loadRange;

    /// <summary>Whether the biome chunk holding this tile index is loaded.</summary>
    public bool WfIsChunkLoaded(Entity<BiomeComponent> biome, Vector2i index)
    {
        var chunkOrigin = SharedMapSystem.GetChunkIndices(index, ChunkSize) * ChunkSize;

        return biome.Comp.LoadedChunks.Contains(chunkOrigin);
    }

    /// <summary>
    /// Whether an entity is one the biome spawned on this tile: one it still tracks, or one an unload kept where it
    /// grew.
    /// </summary>
    public bool WfIsBiomeSpawned(Entity<BiomeComponent> biome, EntityUid uid, Vector2i index)
    {
        var chunkOrigin = SharedMapSystem.GetChunkIndices(index, ChunkSize) * ChunkSize;

        return biome.Comp.LoadedEntities.TryGetValue(chunkOrigin, out var loaded)
               && loaded.TryGetValue(uid, out var tile)
               && tile == index
               || TryComp<Content.Server._WF.Planets.WFBiomeGrownComponent>(uid, out var grown) && grown.Tile == index;
    }
}
