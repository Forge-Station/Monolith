using Content.Server._WF.Planets.Atmosphere;
using Content.Server.Atmos.Components;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server.Atmos.EntitySystems;

public sealed partial class AtmosphereSystem
{
    /// <summary>Gives a planet layer's ground grid an atmosphere that simulates only what is built on it.</summary>
    public void WfAddTerrainAtmosphere(Entity<MapGridComponent> ground, HashSet<int> openTiles)
    {
        var terrain = EnsureComp<WFTerrainAtmosphereComponent>(ground);
        terrain.OpenTiles.UnionWith(openTiles);

        var atmos = EnsureComp<GridAtmosphereComponent>(ground);

        // Startup queued every loaded tile; only what is already built needs a look.
        atmos.InvalidatedCoords.Clear();
        var tiles = _map.GetAllTilesEnumerator(ground, ground.Comp);

        while (tiles.MoveNext(out var tile))
        {
            if (!terrain.OpenTiles.Contains(tile.Value.Tile.TypeId))
                atmos.InvalidatedCoords.Add(tile.Value.GridIndices);
        }
    }

    /// <summary>
    /// Whether any tile in a tile box of a planet layer, corners included, is built on: one the layer's atmosphere
    /// simulates rather than leaves to the planet's air.
    /// </summary>
    public bool WfHasBuiltTile(EntityUid grid, Vector2i from, Vector2i to)
    {
        if (!WfIsTerrain(grid) || !_atmosQuery.TryComp(grid, out var atmos))
            return false;

        var tiles = atmos.Tiles;

        for (var x = from.X; x <= to.X; x++)
        for (var y = from.Y; y <= to.Y; y++)
        {
            if (tiles.TryGetValue(new Vector2i(x, y), out var tile) && !tile.NoGridTile)
                return true;
        }

        return false;
    }

    /// <summary>Whether the grid is a planet layer's ground.</summary>
    private bool WfIsTerrain(EntityUid grid)
    {
        return HasComp<WFTerrainAtmosphereComponent>(grid);
    }

    /// <summary>Whether a tile is a planet layer's bare ground, which shares the map's air instead of holding its own.</summary>
    private bool WfIsBareGround(EntityUid grid, Tile tile)
    {
        return TryComp<WFTerrainAtmosphereComponent>(grid, out var terrain) && terrain.OpenTiles.Contains(tile.TypeId);
    }

    /// <summary>Whether a tile is bare or missing ground of a planet layer that atmos isn't tracking, so nothing needs revalidating.</summary>
    private bool WfIsUntrackedGround(EntityUid grid, Vector2i index)
    {
        if (!TryComp<WFTerrainAtmosphereComponent>(grid, out var terrain)
            || !_atmosQuery.TryComp(grid, out var atmos)
            || atmos.Tiles.ContainsKey(index)
            || !TryComp<MapGridComponent>(grid, out var mapGrid))
        {
            return false;
        }

        return !_map.TryGetTile(mapGrid, index, out var tile) || tile.IsEmpty || terrain.OpenTiles.Contains(tile.TypeId);
    }

    /// <summary>Fills a tile newly built on a planet layer with the air around it, or the map's air, instead of a vacuum.</summary>
    private void WfFillTerrainTile(
        Entity<GridAtmosphereComponent, GasTileOverlayComponent, MapGridComponent, TransformComponent> ent,
        TileAtmosphere tile,
        AirtightData airtight)
    {
        // FixVacuum has already averaged the neighbours in.
        if (airtight.FixVacuum || tile.Air is not { Immutable: false } air || !WfIsTerrain(ent))
            return;

        var neighbours = 0;

        foreach (var adjacent in tile.AdjacentTiles)
        {
            if (adjacent?.Air != null)
                neighbours++;
        }

        if (neighbours > 0)
        {
            // An equal share of each neighbour, put back afterwards. GridFixTileVacuum does the same but leaves
            // the tile at room temperature, which is a gust on a hot or cold world.
            foreach (var adjacent in tile.AdjacentTiles)
            {
                if (adjacent?.Air == null)
                    continue;

                var share = adjacent.Air.RemoveRatio(1f / neighbours);
                Merge(air, share);
                Merge(adjacent.Air, share);
            }

            return;
        }

        if (!TryComp<MapAtmosphereComponent>(ent.Comp4.MapUid, out var map) || map.Space)
            return;

        var volume = air.Volume;
        air.CopyFrom(map.Mixture);

        if (map.Mixture.Volume > 0f && !MathHelper.CloseTo(volume, map.Mixture.Volume))
            air.Multiply(volume / map.Mixture.Volume);

        air.Volume = volume;
    }
}
