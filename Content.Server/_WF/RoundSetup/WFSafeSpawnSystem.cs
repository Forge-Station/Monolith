using Content.Server.Atmos.EntitySystems;
using Content.Server.Shuttles.Components;
using Content.Shared.Atmos;
using Content.Shared.Maps;
using Content.Shared.Physics;
using Content.Shared.SubFloor;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Random;

namespace Content.Server._WF.RoundSetup;

/// <summary>Picks crew-start tiles inside a hull: breathable, clear of machinery and never on exterior plating.</summary>
public sealed partial class WFSafeSpawnSystem : EntitySystem
{
    private const float MinPressure = 80f;
    private const float MaxPressure = 160f;
    private const float MinTemperature = 263f;
    private const float MaxTemperature = 323f;
    private const float MinOxygen = 0.16f;
    private const float MinBreathable = 0.95f;

    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private AtmosphereSystem _atmos = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private TurfSystem _turf = default!;

    /// <summary>
    /// Picks a random tile from the best tier: breathable and clear, breathable, enclosed and clear, enclosed,
    /// beside the shuttle console. The later tiers cover airless hulls and one-seat pods. Tiles holding a gun are
    /// never used.
    /// </summary>
    public bool TryFindSafeFloor(EntityUid grid, out EntityCoordinates coordinates)
    {
        coordinates = default;
        if (!TryComp<MapGridComponent>(grid, out var mapGrid) || Transform(grid).MapUid is not { } map)
            return false;

        var consoles = new HashSet<Vector2i>();
        var consoleQuery = EntityQueryEnumerator<ShuttleConsoleComponent, TransformComponent>();
        while (consoleQuery.MoveNext(out _, out var consoleXform))
        {
            if (consoleXform.GridUid == grid && consoleXform.Anchored)
                consoles.Add(_map.TileIndicesFor(grid, mapGrid, consoleXform.Coordinates));
        }

        var best = int.MaxValue;
        var count = 0;
        foreach (var tileRef in _map.GetAllTiles(grid, mapGrid))
        {
            if (_turf.IsSpace(tileRef) || _turf.IsTileBlocked(tileRef, CollisionGroup.MobMask))
                continue;
            var index = tileRef.GridIndices;
            var anchored = GetAnchored(grid, mapGrid, index);
            if (anchored == Anchored.Gun)
                continue;
            var tier = HasSafeAir(grid, map, index) ? 0 : IsEnclosed(grid, mapGrid, index) ? 2 : -1;
            if (tier >= 0 && anchored == Anchored.Fixture)
                tier++;
            else if (tier < 0 && NearConsole(consoles, index))
                tier = 4;
            if (tier < 0)
                continue;
            if (tier > best)
                continue;
            if (tier < best)
            {
                best = tier;
                count = 0;
            }
            if (_random.Next(++count) == 0)
                coordinates = _turf.GetTileCenter(tileRef);
        }
        return count > 0;
    }

    private enum Anchored : byte
    {
        None,
        Fixture,
        Gun,
    }

    /// <summary>Classifies what is anchored above the floor; pipes and cables under it don't count.</summary>
    private Anchored GetAnchored(EntityUid grid, MapGridComponent mapGrid, Vector2i index)
    {
        var result = Anchored.None;
        var anchored = _map.GetAnchoredEntities(grid, mapGrid, index);
        while (anchored.MoveNext(out var ent))
        {
            if (HasComp<GunComponent>(ent))
                return Anchored.Gun;
            if (!HasComp<SubFloorHideComponent>(ent))
                result = Anchored.Fixture;
        }
        return result;
    }

    /// <summary>Requires near-standard air, mostly oxygen and nitrogen, so gas stores don't count.</summary>
    private bool HasSafeAir(EntityUid grid, EntityUid map, Vector2i index)
    {
        if (_atmos.IsTileSpace(grid, map, index) || _atmos.GetTileMixture(grid, map, index) is not { } air ||
            air.TotalMoles <= 0f || air.Pressure is < MinPressure or > MaxPressure ||
            air.Temperature is < MinTemperature or > MaxTemperature)
            return false;
        var oxygen = air.GetMoles(Gas.Oxygen);
        return oxygen / air.TotalMoles >= MinOxygen && (oxygen + air.GetMoles(Gas.Nitrogen)) / air.TotalMoles >= MinBreathable;
    }

    /// <summary>Puts a pod's crew in its cockpit.</summary>
    private static bool NearConsole(HashSet<Vector2i> consoles, Vector2i index)
    {
        foreach (var console in consoles)
        {
            if (Math.Abs(console.X - index.X) <= 1 && Math.Abs(console.Y - index.Y) <= 1)
                return true;
        }
        return false;
    }

    /// <summary>Requires deck on all eight sides, so the tile can't be exterior plating.</summary>
    private bool IsEnclosed(EntityUid grid, MapGridComponent mapGrid, Vector2i index)
    {
        for (var x = -1; x <= 1; x++)
        {
            for (var y = -1; y <= 1; y++)
            {
                if (x == 0 && y == 0)
                    continue;
                if (!_map.TryGetTileRef(grid, mapGrid, index + new Vector2i(x, y), out var neighbour) ||
                    neighbour.Tile.IsEmpty || _turf.IsSpace(neighbour))
                    return false;
            }
        }
        return true;
    }
}
