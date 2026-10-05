using Content.Shared.Parallax.Biomes;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Utility;

namespace Content.Server.Parallax;

/// <summary>Wolfgate access to BiomeComponent's private chunk loader and pin set.</summary>
public sealed partial class BiomeSystem
{
    /// <summary>Tile accumulator for <see cref="WfUnloadChunk"/>; the private unloader writes into the caller's list.</summary>
    private readonly List<(Vector2i, Tile)> _wfUnloadBuffer = new();

    /// <summary>Pins tiles so the biome never regenerates or unloads them again.</summary>
    public void WfPinTiles(Entity<BiomeComponent> biome, IReadOnlyCollection<Vector2i> indices)
    {
        foreach (var index in indices)
        {
            var chunkOrigin = SharedMapSystem.GetChunkIndices(index, ChunkSize) * ChunkSize;
            biome.Comp.ModifiedTiles.GetOrNew(chunkOrigin).Add(index);
        }
    }

    /// <summary>Whether one tile index is pinned against biome regeneration.</summary>
    public bool WfIsPinned(Entity<BiomeComponent> biome, Vector2i index)
    {
        var chunkOrigin = SharedMapSystem.GetChunkIndices(index, ChunkSize) * ChunkSize;

        return biome.Comp.ModifiedTiles.TryGetValue(chunkOrigin, out var modified) && modified.Contains(index);
    }

    /// <summary>
    /// Loads one biome chunk by hand and holds it: the planet unloader leaves it until <see cref="WfUnloadChunk"/>.
    /// Claims LoadedChunks first, as LoadChunk throws on a repeat.
    /// </summary>
    public bool WfLoadChunk(Entity<BiomeComponent, MapGridComponent> biome, Vector2i chunkOrigin)
    {
        _wfLayers.GetOrNew(biome.Owner).Held.Add(chunkOrigin);

        if (!biome.Comp1.LoadedChunks.Add(chunkOrigin))
            return false;

        LoadChunk(biome.Comp1, biome.Owner, biome.Comp2, chunkOrigin, biome.Comp1.Seed);
        return true;
    }

    /// <summary>
    /// Unloads one biome chunk by hand, emptying every unpinned tile and its decals, as the loader would: a planet
    /// layer's chunk goes by the planet unloader's rules.
    /// </summary>
    public bool WfUnloadChunk(Entity<BiomeComponent, MapGridComponent> biome, Vector2i chunkOrigin)
    {
        if (_wfLayers.TryGetValue(biome.Owner, out var layer))
            layer.Held.Remove(chunkOrigin);

        if (!biome.Comp1.LoadedChunks.Contains(chunkOrigin))
            return false;

        if (HasComp<Content.Shared._WF.Planets.WFPlanetLayerComponent>(biome.Owner)
            && _configManager.GetCVar(Content.Shared._WF.CCVar.PlanetCVars.TerrainUnload))
        {
            WfUnloadChunkPristine(biome.Comp1, biome.Owner, biome.Comp2, chunkOrigin);
            return true;
        }

        _wfUnloadBuffer.Clear();
        UnloadChunk(biome.Comp1, biome.Owner, biome.Comp2, chunkOrigin, biome.Comp1.Seed, _wfUnloadBuffer);
        return true;
    }
}
