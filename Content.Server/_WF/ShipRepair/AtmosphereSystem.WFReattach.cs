using Content.Server.Atmos.Components;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Robust.Shared.Map.Components;

namespace Content.Server.Atmos.EntitySystems;

public sealed partial class AtmosphereSystem
{
    /// <summary>A copy of the air on each of a grid's own tiles, by tile index.</summary>
    public Dictionary<Vector2i, GasMixture> WfCopyGridAir(EntityUid grid)
    {
        var air = new Dictionary<Vector2i, GasMixture>();
        if (!TryComp<GridAtmosphereComponent>(grid, out var atmos))
            return air;

        foreach (var (index, tile) in atmos.Tiles)
        {
            if (!tile.NoGridTile && !tile.MapAtmosphere && tile.Air is { Immutable: false } mixture)
                air[index] = mixture.Clone();
        }

        return air;
    }

    /// <summary>Gives tiles just added to a grid their air, revalidated at once so no tile starts as vacuum.</summary>
    public void WfSeedGridAir(EntityUid grid, Dictionary<Vector2i, GasMixture> air)
    {
        if (air.Count == 0
            || !TryComp<GridAtmosphereComponent>(grid, out var atmos)
            || !TryComp<GasTileOverlayComponent>(grid, out var overlay)
            || !TryComp<MapGridComponent>(grid, out var mapGrid))
        {
            return;
        }

        var ent = new Entity<GridAtmosphereComponent, GasTileOverlayComponent, MapGridComponent, TransformComponent>(
            grid, atmos, overlay, mapGrid, Transform(grid));
        var volume = GetVolumeForTiles(ent);
        TryComp(ent.Comp4.MapUid, out MapAtmosphereComponent? mapAtmos);

        // The same steps as revalidation, so neighbours agree on adjacency before equalization walks it.
        var tiles = new List<(TileAtmosphere Tile, GasMixture Mixture)>(air.Count);
        foreach (var (index, mixture) in air)
        {
            var tile = GetOrNewTile(grid, atmos, index);
            UpdateTileData(ent, mapAtmos, tile);
            tiles.Add((tile, mixture));
        }

        foreach (var (tile, _) in tiles)
        {
            UpdateAdjacentTiles(ent, tile, activate: true);
        }

        foreach (var (tile, mixture) in tiles)
        {
            UpdateTileAir(ent, tile, volume);
            if (tile.Air is { Immutable: false } tileAir)
                tileAir.CopyFrom(mixture);

            InvalidateVisuals(ent, tile);
        }
    }

    /// <summary>Cuts a tile leaving the grid atmosphere out of its neighbours' adjacency, so equalization can't walk into it.</summary>
    private void WfUnlinkTile(TileAtmosphere tile)
    {
        for (var i = 0; i < Atmospherics.Directions; i++)
        {
            if (tile.AdjacentTiles[i] is { } adjacent)
            {
                var opposite = i.ToOppositeIndex();
                if (adjacent.AdjacentTiles[opposite] == tile)
                {
                    adjacent.AdjacentTiles[opposite] = null;
                    adjacent.AdjacentBits &= ~(AtmosDirection) (1 << opposite);
                    if (!adjacent.AdjacentBits.IsFlagSet(adjacent.MonstermosInfo.CurrentTransferDirection))
                        adjacent.MonstermosInfo.CurrentTransferDirection = AtmosDirection.Invalid;
                }
            }

            tile.AdjacentTiles[i] = null;
        }

        tile.AdjacentBits = AtmosDirection.Invalid;
        tile.MonstermosInfo.CurrentTransferDirection = AtmosDirection.Invalid;
    }
}
