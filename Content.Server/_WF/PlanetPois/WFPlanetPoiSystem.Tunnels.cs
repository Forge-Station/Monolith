using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.Shared._WF.PlanetPois;
using Content.Shared.Light.Components;
using Content.Shared.Parallax.Biomes;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Random;

namespace Content.Server._WF.PlanetPois;

public sealed partial class WFPlanetPoiSystem
{
    /// <summary>Tiles of road a tunnel keeps from every bend, junction and end.</summary>
    private const int TunnelClear = 4;

    /// <summary>
    /// Walls a few straight stretches of road in rock and roofs them over: dark, close, with webs and bones along the
    /// edges and something waiting inside. A tunnel keeps off the sites, and a footpath that meets the road inside one
    /// leaves a gap in its wall.
    /// </summary>
    private async Task DigTunnelsAsync(EntityUid ground, MapGridComponent grid, BiomeComponent biome, WFPlanetPoiComponent record,
        WFPlanetRoadNetwork roads, WFPlanetRoadPlan plan, HashSet<EntityUid> created, HashSet<Vector2i> changed)
    {
        if (roads.Tunnels is not { } tunnels)
            return;

        var half = roads.Width / 2 + roads.ShoulderWidth;
        var runs = StraightStretches(plan.Centreline, tunnels.MinLength);
        _random.Shuffle(runs);
        var count = _random.Next(tunnels.MinCount, tunnels.MaxCount + 1);
        var open = new HashSet<Vector2i>(plan.RoadTiles);
        open.UnionWith(plan.ShoulderTiles);
        open.UnionWith(record.TrailTiles);

        foreach (var (run, axis) in runs)
        {
            if (record.Tunnels.Count >= count)
                break;

            var length = Math.Min(run.Count, _random.Next(tunnels.MinLength, tunnels.MaxLength + 1));
            var start = _random.Next(run.Count - length + 1);
            var along = run.GetRange(start, length);
            var across = axis == WFRoadAxis.EastWest ? new Vector2i(0, 1) : new Vector2i(1, 0);

            var road = new HashSet<Vector2i>();
            var rock = new HashSet<Vector2i>();
            foreach (var tile in along)
            {
                for (var k = -half - tunnels.Thickness; k <= half + tunnels.Thickness; k++)
                {
                    var cell = tile + across * k;
                    if (Math.Abs(k) <= half)
                        road.Add(cell);
                    else if (!open.Contains(cell))
                        rock.Add(cell);
                }
            }

            if (road.Concat(rock).Any(cell => record.Sites.Any(site => Contains(site.Bounds, cell))))
                continue;

            await TunnelAsync(ground, grid, biome, record, tunnels, road, rock, along, across, half, created, changed);
            if (TerminatingOrDeleted(ground))
                return;
        }
    }

    private async Task TunnelAsync(EntityUid ground, MapGridComponent grid, BiomeComponent biome, WFPlanetPoiComponent record,
        WFRoadTunnels tunnels, HashSet<Vector2i> road, HashSet<Vector2i> rock, List<Vector2i> along, Vector2i across,
        int half, HashSet<EntityUid> created, HashSet<Vector2i> changed)
    {
        await _preload.Breathe();
        if (TerminatingOrDeleted(ground))
            return;

        // Street furniture goes; nobody lit this stretch.
        for (var i = record.Furniture.Count - 1; i >= 0; i--)
        {
            var piece = record.Furniture[i];
            if (TerminatingOrDeleted(piece) || !road.Contains(_maps.TileIndicesFor(ground, grid, Transform(piece).Coordinates)))
                continue;
            record.Furniture.RemoveAt(i);
            created.Remove(piece);
            QueueDel(piece);
        }

        // The rock: the world's own ground under it, whatever grew there cleared.
        var floor = new List<(Vector2i, Tile)>();
        var anchored = new List<EntityUid>();
        foreach (var cell in rock)
        {
            anchored.Clear();
            var enumerator = _maps.GetAnchoredEntitiesEnumerator(ground, grid, cell);
            while (enumerator.MoveNext(out var uid))
                anchored.Add(uid.Value);
            foreach (var uid in anchored)
                QueueDel(uid);
            if (_maps.GetTileRef(ground, grid, cell).Tile.IsEmpty && _biome.TryGetBiomeTile(ground, grid, cell, out var natural))
                floor.Add((cell, natural.Value));
        }
        _maps.SetTiles(ground, grid, floor);
        // Pinned first, so nothing grows on rock still waiting for its wall.
        _biome.WfPinTiles((ground, biome), rock);
        foreach (var cell in rock)
        {
            await _preload.Breathe();
            if (TerminatingOrDeleted(ground))
                return;
            created.Add(Spawn(tunnels.Wall, new EntityCoordinates(ground, cell + new Vector2(0.5f))));
        }

        EnsureComp<RoofComponent>(ground);
        foreach (var cell in road.Concat(rock))
            _roof.SetRoof((ground, grid, null), cell, true);
        changed.UnionWith(rock);
        await _preload.Breathe();

        // Clutter hugs the walls, leaving the middle of the road clear.
        var edges = along.SelectMany(tile => new[] { tile + across * half, tile - across * half }).ToList();
        foreach (var cell in edges)
        {
            if (tunnels.Clutter.Count == 0 || !_random.Prob(tunnels.ClutterChance))
                continue;
            await _preload.Breathe();
            if (TerminatingOrDeleted(ground))
                return;
            created.Add(Spawn(_random.Pick(tunnels.Clutter), new EntityCoordinates(ground, cell + new Vector2(0.5f))));
        }

        if (tunnels.Lurkers is { } lurkers)
        {
            var inside = road.ToList();
            var count = _random.Next(tunnels.MinLurkers, tunnels.MaxLurkers + 1);
            for (var i = 0; i < count; i++)
            {
                foreach (var id in _entityTables.GetSpawns(lurkers))
                {
                    await _preload.Breathe();
                    if (TerminatingOrDeleted(ground))
                        return;
                    created.Add(Spawn(id, new EntityCoordinates(ground, (Vector2) _random.Pick(inside) + new Vector2(0.5f))));
                }
            }
        }

        record.Tunnels.Add(road);
    }

    /// <summary>The straight runs of road at least <paramref name="minLength"/> long, clear of bends and junctions, in order.</summary>
    private static List<(List<Vector2i> Run, WFRoadAxis Axis)> StraightStretches(IReadOnlySet<Vector2i> centreline, int minLength)
    {
        var straight = WFRoadDressingPlanner.StraightRuns(centreline, TunnelClear);
        var runs = new List<(List<Vector2i>, WFRoadAxis)>();
        var seen = new HashSet<Vector2i>();
        foreach (var (tile, axis) in straight.OrderBy(pair => pair.Key.X).ThenBy(pair => pair.Key.Y))
        {
            if (!seen.Add(tile))
                continue;
            var step = axis == WFRoadAxis.EastWest ? new Vector2i(1, 0) : new Vector2i(0, 1);
            var run = new List<Vector2i> { tile };
            for (var next = tile + step; straight.TryGetValue(next, out var nextAxis) && nextAxis == axis; next += step)
            {
                seen.Add(next);
                run.Add(next);
            }
            if (run.Count >= minLength)
                runs.Add((run, axis));
        }
        return runs;
    }
}
