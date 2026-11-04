using Content.Shared.Movement.Components;
using Content.Shared.Parallax.Biomes;
using Content.Shared.Parallax.Biomes.Layers;
using Robust.Shared.Map.Components;
using Robust.Shared.Noise;
using Robust.Shared.Prototypes;

namespace Content.Server.Parallax;

/// <summary>Where a biome grows standing water, read from its recipe without loading anything.</summary>
public sealed partial class BiomeSystem
{
    /// <summary>One noise test a tile passes on its way down to a water layer.</summary>
    private readonly record struct WfWaterGate(FastNoiseLite Noise, bool Invert, float Threshold);

    /// <summary>Layer lists kept before the gate cache starts over.</summary>
    private const int WfWaterCacheSize = 64;

    private readonly Dictionary<List<IBiomeLayer>, List<WfWaterGate[]>> _wfWaterGates = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, bool> _wfWaterProtos = new();

    /// <summary>
    /// Whether the biome grows standing water on a tile: a lake or pond's floor-occluding pool, not lava or plasma. Reads
    /// the recipe alone, so it answers before the chunk exists, and ignores pins and whatever is on the tile now.
    /// </summary>
    public bool WfIsWater(Entity<BiomeComponent> biome, Vector2i indices)
    {
        var layers = biome.Comp.Layers;
        var seed = biome.Comp.Seed;
        var open = false;
        foreach (var path in WfWaterGates(layers))
        {
            var pass = true;
            foreach (var gate in path)
            {
                var value = WfSeededNoise(gate.Noise, seed).GetNoise(indices.X, indices.Y);
                if ((gate.Invert ? -value : value) >= gate.Threshold)
                    continue;
                pass = false;
                break;
            }
            if (!pass)
                continue;
            open = true;
            break;
        }

        // Most ground fails every gate, which spares the full lookup.
        if (!open
            || !TryGetBiomeTile(indices, layers, seed, (Entity<MapGridComponent>?) null, out var tile)
            || !TryGetEntity(indices, layers, tile.Value, seed, (Entity<MapGridComponent>?) null, out var entity))
            return false;
        return WfIsWaterProto(entity);
    }

    /// <summary>Every chain of layer thresholds that leads to a layer able to grow water.</summary>
    private List<WfWaterGate[]> WfWaterGates(List<IBiomeLayer> layers)
    {
        if (_wfWaterGates.TryGetValue(layers, out var cached))
            return cached;
        if (_wfWaterGates.Count >= WfWaterCacheSize)
            _wfWaterGates.Clear();

        var paths = new List<WfWaterGate[]>();
        WfCollectWaterGates(layers, new List<WfWaterGate>(), paths, 0);
        _wfWaterGates[layers] = paths;
        return paths;
    }

    private void WfCollectWaterGates(List<IBiomeLayer> layers, List<WfWaterGate> chain, List<WfWaterGate[]> paths, int depth)
    {
        if (depth > 8)
            return;
        foreach (var layer in layers)
        {
            var gate = new WfWaterGate(layer.Noise, layer.Invert, layer.Threshold);
            switch (layer)
            {
                case BiomeEntityLayer entities when entities.Entities.Exists(WfIsWaterProto):
                    paths.Add([..chain, gate]);
                    break;
                case BiomeMetaLayer meta when ProtoManager.TryIndex<BiomeTemplatePrototype>(meta.Template, out var template):
                    chain.Add(gate);
                    WfCollectWaterGates(template.Layers, chain, paths, depth + 1);
                    chain.RemoveAt(chain.Count - 1);
                    break;
            }
        }
    }

    /// <summary>Standing water: the pools that hide the floor under them, as lakes, ponds and seas do.</summary>
    private bool WfIsWaterProto(string id)
    {
        if (_wfWaterProtos.TryGetValue(id, out var water))
            return water;
        water = ProtoManager.TryIndex<EntityPrototype>(id, out var proto)
                && proto.Components.ContainsKey(Factory.GetComponentName<FloorOccluderComponent>());
        _wfWaterProtos[id] = water;
        return water;
    }
}
