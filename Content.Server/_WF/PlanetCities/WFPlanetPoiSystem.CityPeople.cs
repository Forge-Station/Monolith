using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.Server.Spawners.Components;
using Content.Shared.Doors.Components;
using Content.Shared._WF.PlanetCities;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Random;

namespace Content.Server._WF.PlanetPois;

/// <summary>The people of a fixed city: residents indoors on every level, and where its fate's groups settle.</summary>
public sealed partial class WFPlanetPoiSystem
{
    [Dependency] private EntityLookupSystem _lookup = default!;

    private readonly HashSet<EntityUid> _overhang = new();

    /// <summary>One level of a built city as it stands on its world: its grid, every floor tile and the roofed ones.</summary>
    private sealed record CityFloor(int Depth, Entity<MapGridComponent> Grid, HashSet<Vector2i> Tiles, HashSet<Vector2i> Indoor);

    /// <summary>Free tiles of one level that people may be put on.</summary>
    private sealed class CityPool(CityFloor floor, List<Vector2i> tiles)
    {
        public CityFloor Floor { get; } = floor;
        public List<Vector2i> Tiles { get; } = tiles;
        public HashSet<Vector2i> Set { get; } = tiles.ToHashSet();
    }

    /// <summary>
    /// Puts a city's residents on its roofed floor: one on every level that has room, the rest in proportion to each
    /// level's floor, on bare tiles clear of the spawn points and a few tiles apart.
    /// </summary>
    private async Task SpawnCityResidentsAsync(WFFixedCityPrototype city, List<CityFloor> floors, HashSet<EntityUid> created)
    {
        if (city.Residents is not { } residents)
            return;

        var pools = CityPools(floors, floor => floor.Depth switch
        {
            0 => residents.Ground,
            > 0 => residents.Upper,
            _ => residents.Lower,
        }, floor => floor.Indoor);
        if (pools.Count == 0)
            return;
        await _preload.Breathe();

        var total = _random.Next(residents.Min, residents.Max + 1);
        var firsts = pools.ToList();
        _random.Shuffle(firsts);
        var weight = pools.Sum(pool => pool.Tiles.Count);
        var chosen = pools.ToDictionary(pool => pool, _ => new List<Vector2i>());
        var placed = 0;
        for (var i = 0; i < total; i++)
        {
            var pool = i < firsts.Count ? firsts[i] : PickPool(pools, weight);
            var taken = chosen[pool];
            if (PickSpacedTile(pool, taken, residents.Spacing) is not { } tile)
                continue;

            taken.Add(tile);
            await _preload.Breathe();
            if (TerminatingOrDeleted(pool.Floor.Grid.Owner))
                break;
            // The city has carried on since the pools were drawn up: a door may have shut on the tile.
            if (CityTileBlocked(pool.Floor.Grid, tile))
                continue;
            foreach (var id in _entityTables.GetSpawns(residents.Spawns))
            {
                created.Add(Spawn(id, new EntityCoordinates(pool.Floor.Grid.Owner, (Vector2) tile + new Vector2(0.5f))));
                placed++;
            }
        }

        Log.Info($"{city.ID}: {placed} residents over {pools.Count} levels.");
    }

    /// <summary>The levels a pick wants, each with its tiles that are free to stand on and clear of the spawn points.</summary>
    private List<CityPool> CityPools(List<CityFloor> floors, Func<CityFloor, bool> wanted, Func<CityFloor, IEnumerable<Vector2i>> tiles)
    {
        var spawnPoints = new Dictionary<EntityUid, List<Vector2>>();
        var points = EntityQueryEnumerator<SpawnPointComponent, TransformComponent>();
        while (points.MoveNext(out _, out _, out var xform))
        {
            if (xform.GridUid is { } gridUid)
            {
                if (!spawnPoints.TryGetValue(gridUid, out var list))
                    spawnPoints[gridUid] = list = new List<Vector2>();
                list.Add(xform.LocalPosition);
            }
        }

        var pools = new List<CityPool>();
        foreach (var floor in floors.Where(wanted))
        {
            var near = spawnPoints.GetValueOrDefault(floor.Grid.Owner) ?? new List<Vector2>();
            var free = tiles(floor)
                .Where(tile => !CityTileBlocked(floor.Grid, tile) && near.All(point => Vector2.Distance(point, (Vector2) tile) > FateSpawnClearance))
                .ToList();
            if (free.Count > 0)
                pools.Add(new CityPool(floor, free));
        }

        return pools;
    }

    /// <summary>A tile of the pool a few tiles from the ones taken if one turns up, otherwise any untaken one; none if all are taken.</summary>
    private Vector2i? PickSpacedTile(CityPool pool, List<Vector2i> taken, int spacing)
    {
        for (var attempt = 0; attempt < 24; attempt++)
        {
            var tile = _random.Pick(pool.Tiles);
            if (taken.All(other => Math.Max(Math.Abs(other.X - tile.X), Math.Abs(other.Y - tile.Y)) > spacing))
                return tile;
        }

        var untaken = pool.Tiles.Where(tile => !taken.Contains(tile)).ToList();
        return untaken.Count > 0 ? _random.Pick(untaken) : null;
    }

    /// <summary>A pool chosen by its share of the tiles.</summary>
    private CityPool PickPool(List<CityPool> pools, int weight)
    {
        var roll = _random.Next(weight);
        foreach (var pool in pools)
        {
            roll -= pool.Tiles.Count;
            if (roll < 0)
                return pool;
        }

        return pools[^1];
    }

    /// <summary>
    /// Whether a tile isn't bare floor: it has no tile, something solid or a door, open or not, is anchored on it, or a
    /// solid anchored next door (a cargo container, a big machine) reaches over its middle.
    /// </summary>
    private bool CityTileBlocked(Entity<MapGridComponent> grid, Vector2i tile)
    {
        if (_maps.GetTileRef(grid.Owner, grid.Comp, tile).Tile.IsEmpty)
            return true;

        var anchored = _maps.GetAnchoredEntitiesEnumerator(grid.Owner, grid.Comp, tile);
        while (anchored.MoveNext(out var uid))
        {
            if (TryComp<PhysicsComponent>(uid, out var body) && body.Hard && body.CanCollide || HasComp<DoorComponent>(uid))
                return true;
        }

        var middle = Box2.CenteredAround((Vector2) tile + new Vector2(0.5f), new Vector2(0.6f));
        _overhang.Clear();
        _lookup.GetLocalEntitiesIntersecting(grid.Owner, middle, _overhang, LookupFlags.Static);
        foreach (var uid in _overhang)
        {
            if (TryComp<PhysicsComponent>(uid, out var body) && body.Hard && body.CanCollide)
                return true;
        }

        return false;
    }
}
