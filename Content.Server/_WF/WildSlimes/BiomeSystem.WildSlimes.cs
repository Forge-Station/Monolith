using Content.Shared.Parallax.Biomes.Layers;

namespace Content.Server.Parallax;

public sealed partial class BiomeSystem
{
    /// <summary>Whether a tile passes one layer's own noise test, as the generator reads it.</summary>
    public bool WfPassesLayer(IBiomeLayer layer, int seed, Vector2i index)
    {
        var value = WfSeededNoise(layer.Noise, seed).GetNoise(index.X, index.Y);
        return (layer.Invert ? -value : value) >= layer.Threshold;
    }
}
