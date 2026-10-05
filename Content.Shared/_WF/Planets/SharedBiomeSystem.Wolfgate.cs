using Content.Shared._WF.Planets;

namespace Content.Shared.Parallax.Biomes;

/// <summary>Wolfgate additions to biome generation: the noise copy cache <c>GetNoise</c> reads through.</summary>
public abstract partial class SharedBiomeSystem
{
    [Dependency] private WFBiomeNoiseCacheSystem _wfNoiseCache = default!;
}
