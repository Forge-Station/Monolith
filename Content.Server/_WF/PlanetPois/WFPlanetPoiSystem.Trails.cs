using System.Linq;
using System.Threading.Tasks;
using System.Numerics;
using Content.Shared._WF.PlanetPois;
using Content.Shared.Parallax.Biomes;
using Robust.Shared.Map.Components;
using Robust.Shared.Random;

namespace Content.Server._WF.PlanetPois;

public sealed partial class WFPlanetPoiSystem
{
    /// <summary>Chance that a footpath tile is worn a tile wider to one side.</summary>
    private const float TrailWidening = 0.2f;

    /// <summary>
    /// Wears a footpath from each off-road site with a trail to the nearest road or earlier footpath, through open ground
    /// and its own yard but never another site or the landing zone. A site too far from any road goes without.
    /// </summary>
    private async Task LayTrailsAsync(EntityUid ground, MapGridComponent grid, BiomeComponent biome, WFPlanetPoiComponent record,
        WFPlanetRoadNetwork roads, Vector2 centre, float radius, float centreClear, HashSet<Vector2i> changed)
    {
        var targets = new HashSet<Vector2i>(record.RoadTiles);
        targets.UnionWith(record.ShoulderTiles);
        foreach (var site in record.Sites.ToList())
        {
            if (site.TrailStart is not { } start)
                continue;
            await _preload.Breathe();
            if (TerminatingOrDeleted(ground))
                return;

            bool Passable(Vector2i tile)
            {
                // Through its own yard, or across its own floor where nothing stands in the way.
                if (Contains(site.Bounds, tile))
                    return !site.Tiles.Contains(tile) || Unblocked(ground, grid, tile);
                return !_biome.WfIsPinned((ground, biome), tile) && !_biome.WfIsWater((ground, biome), tile)
                       && Vector2.DistanceSquared(new Vector2(tile.X + 0.5f, tile.Y + 0.5f), centre) >= centreClear * centreClear;
            }

            var path = WFPlanetTrailPlanner.Route(start.Position, targets, Passable, centre, radius, roads.TrailLength, _random.Next());
            if (path == null)
            {
                Log.Warning($"{site.Name} on {ToPrettyString(ground)} is too far from the roads for a footpath.");
                continue;
            }

            var worn = path.Where(tile => !targets.Contains(tile) && !site.Tiles.Contains(tile)).ToHashSet();
            foreach (var tile in worn.ToList())
            {
                if (!_random.Prob(TrailWidening))
                    continue;
                var side = tile + (_random.Prob(0.5f) ? new Vector2i(1, 0) : new Vector2i(0, 1));
                if (!targets.Contains(side) && !site.Tiles.Contains(side) && Passable(side) && Vector2.DistanceSquared(side, centre) < radius * radius)
                    worn.Add(side);
            }

            changed.UnionWith(worn);
            await PaintRoadAsync(ground, grid, biome, worn, roads.TrailTile);
            record.TrailTiles.UnionWith(worn);
            site.TrailLaid = true;
            targets.UnionWith(worn);
        }
    }
}
