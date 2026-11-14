using Content.Shared._WF.Planets;
using Robust.Shared.Noise;

namespace Content.Shared.Parallax.Biomes;

/// <summary>Wolfgate additions to biome generation: the noise copy cache <c>GetNoise</c> reads through.</summary>
public abstract partial class SharedBiomeSystem
{
    [Dependency] private WFBiomeNoiseCacheSystem _wfNoiseCache = default!;

    /// <summary>The seeded, cached copy of a layer's noise that generation reads.</summary>
    protected FastNoiseLite WfSeededNoise(FastNoiseLite noise, int seed) => GetNoise(noise, seed);
}
