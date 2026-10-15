// #Forge-change START (KIAS): Ограниченный запрос читает внутренние atmospheric tiles без раскрытия коллекции.
using Content.Server.Atmos.Components;
using Content.Shared.Atmos;

namespace Content.Server.Atmos.EntitySystems;

public sealed partial class AtmosphereSystem
{
    public bool IsSealedSafeRoom(GridAtmosphereComponent grid, Vector2i coordinates, int limit = 625)
    {
        if (!grid.Tiles.TryGetValue(coordinates, out var start)) return false;
        var visited = new HashSet<TileAtmosphere> { start };
        var pending = new Queue<TileAtmosphere>();
        pending.Enqueue(start);
        while (pending.TryDequeue(out var tile))
        {
            if (tile.Space || tile.Air == null || grid.InvalidatedCoords.Contains(tile.GridIndices)
                || !IsMixtureProbablySafe(tile.Air)) return false;
            for (var i = 0; i < Atmospherics.Directions; i++)
            {
                if (!tile.AdjacentBits.IsFlagSet((AtmosDirection) (1 << i))) continue;
                var adjacent = tile.AdjacentTiles[i];
                if (adjacent == null) return false;
                if (!visited.Add(adjacent)) continue;
                if (visited.Count > limit) return false;
                pending.Enqueue(adjacent);
            }
        }
        return true;
    }
}
// #Forge-change END (KIAS)
