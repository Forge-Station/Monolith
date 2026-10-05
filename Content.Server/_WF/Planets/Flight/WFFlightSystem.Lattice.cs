using System.Linq;
using System.Numerics;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server._WF.Planets.Flight;

public sealed partial class WFFlightSystem
{
    private static readonly Vector2i[] SeamNeighbours = { new(1, 0), new(-1, 0), new(0, 1), new(0, -1) };

    /// <summary>Keep the severed seam as exposed lattice owned by one section, after floor connectivity is split.</summary>
    private void RestoreCrashLattice(EntityUid original, EntityUid[] fragments)
    {
        if (!TryComp<WFCrashImpactComponent>(original, out var impact) || impact.LatticeSeam is not { } seam)
            return;
        impact.LatticeSeam = null; // Tile updates can trigger more events; never restore the same seam twice.
        if (!TryComp<MapGridComponent>(original, out var originalGrid))
            return;
        var sourceMatrix = _transform.GetWorldMatrix(original);
        var lattice = new Tile(_scarTiles["Lattice"].TileId);
        // Fixed priority, original first, keeps the seam on one side instead of interlocking teeth.
        var pieces = new[] { original }.Concat(fragments).Where(HasComp<MapGridComponent>).ToArray();
        var occupied = new Dictionary<EntityUid, HashSet<Vector2i>>();
        var additions = new Dictionary<EntityUid, List<(Vector2i, Tile)>>();
        foreach (var uid in pieces)
        {
            occupied[uid] = new();
            additions[uid] = new();
            var tiles = _map.GetAllTilesEnumerator(uid, Comp<MapGridComponent>(uid));
            while (tiles.MoveNext(out var tile))
                occupied[uid].Add(tile.Value.GridIndices);
        }
        var pending = seam.OrderBy(t => t.X).ThenBy(t => t.Y).ToList();
        while (pending.Count > 0)
        {
            var progress = false;
            for (var i = pending.Count - 1; i >= 0; i--)
            {
                var world = Vector2.Transform(((Vector2) pending[i] + new Vector2(0.5f)) * originalGrid.TileSize, sourceMatrix);
                foreach (var uid in pieces)
                {
                    var index = _map.WorldToTile(uid, Comp<MapGridComponent>(uid), world);
                    if (!SeamNeighbours.Any(offset => occupied[uid].Contains(index + offset)))
                        continue;
                    if (occupied[uid].Add(index))
                        additions[uid].Add((index, lattice));
                    pending.RemoveAt(i);
                    progress = true;
                    break;
                }
            }
            if (!progress)
                break; // Never create a detached lattice-only fragment.
        }
        foreach (var uid in pieces)
            if (additions[uid].Count > 0)
                _map.SetTiles(uid, Comp<MapGridComponent>(uid), additions[uid]);
    }
}
