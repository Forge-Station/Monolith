using Content.Shared._WF.Planets;
using Content.Shared.Parallax.Biomes;

namespace Content.Server._CE.ZLevels.Core;

public sealed partial class CEZLevelsSystem
{
    /// <inheritdoc/>
    // Called once the tile is known to be empty. Pinned, it is a hole wherever it is; on a loaded chunk it is one the
    // hole queue has yet to pin. Anything else is ground whose chunk nobody has loaded, or that was unloaded.
    protected override bool WfUnloadedGround(EntityUid grid, Vector2i tile)
    {
        if (!HasComp<WFPlanetLayerComponent>(grid) || !TryComp<BiomeComponent>(grid, out var biome))
            return false;

        Entity<BiomeComponent> ground = (grid, biome);
        return !_wfLandingBiome.WfIsPinned(ground, tile) && !_wfLandingBiome.WfIsChunkLoaded(ground, tile);
    }
}
