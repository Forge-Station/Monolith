using System.Numerics;
using System.Threading.Tasks;
using Content.Shared._WF.CCVar;
using Content.Shared.Light.Components;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server._WF.PlanetPois;

/// <summary>
/// Lays a big site's ground a few chunks a tick ahead of its grid merge. The engine merge sets every tile in one call,
/// which for a city of forty thousand tiles is most of a second; with the tiles already there it only has the entities
/// left to move, and finds every tile unchanged.
/// </summary>
public sealed partial class WFPlanetPoiSystem
{
    /// <summary>Sets the tiles the merge will set, one source chunk at a time within the generation budget.</summary>
    private async Task LayMergeTilesAsync(EntityUid ground, MapGridComponent grid, Entity<MapGridComponent> source, Vector2i origin, int turns)
    {
        var chunks = new Dictionary<Vector2i, List<(Vector2i Indices, Tile Tile)>>();
        var count = 0;
        var tiles = _maps.GetAllTilesEnumerator(source.Owner, source.Comp);

        while (tiles.MoveNext(out var tileRef))
        {
            var chunk = SharedMapSystem.GetChunkIndices(tileRef.Value.GridIndices, MapGridComponent.DefaultChunkSize);
            if (!chunks.TryGetValue(chunk, out var list))
                chunks[chunk] = list = new List<(Vector2i, Tile)>();

            list.Add((tileRef.Value.GridIndices, tileRef.Value.Tile));
            count++;
        }

        var threshold = _cfg.GetCVar(PlanetCVars.MergeSliceTiles);
        if (threshold <= 0 || count <= threshold)
            return;

        // The same transform the merge applies, so every tile it finds is already as it would have set it.
        var matrix = Matrix3Helpers.CreateTransform(origin, Turns(turns));
        var half = grid.TileSizeHalfVector;

        foreach (var list in chunks.Values)
        {
            await _preload.Breathe();

            if (TerminatingOrDeleted(ground) || TerminatingOrDeleted(source.Owner))
                return;

            var slice = new List<(Vector2i, Tile)>(list.Count);
            foreach (var (indices, tile) in list)
            {
                var placed = Vector2.Transform(new Vector2(indices.X, indices.Y) + half, matrix);
                slice.Add((placed.Floored(), tile));
            }

            _maps.SetTiles(ground, grid, slice);
        }
    }

    /// <summary>Roofs or opens each tile, a slice a tick: forty thousand of them at once is a long tick.</summary>
    private async Task SetRoofsAsync(Entity<MapGridComponent?, RoofComponent?> grid, IEnumerable<Vector2i> tiles, ISet<Vector2i> roofed)
    {
        var done = 0;

        foreach (var tile in tiles)
        {
            _roof.SetRoof(grid, tile, roofed.Contains(tile));

            if (++done % 512 != 0)
                continue;

            await _preload.Breathe();
            if (TerminatingOrDeleted(grid.Owner))
                return;
        }
    }
}
