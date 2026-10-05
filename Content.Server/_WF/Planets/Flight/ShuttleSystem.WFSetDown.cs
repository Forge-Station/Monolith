using System.Numerics;
using Content.Shared._WF.Planets;
using Content.Shared.Damage;
using Content.Shared.Database;
using Content.Shared.Mobs.Components;
using Content.Shared.Physics;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;

namespace Content.Server.Shuttles.Systems;

public sealed partial class ShuttleSystem
{
    /// <summary>Blunt damage to a mob a hull comes down on.</summary>
    public const float WfSetDownDamage = 40f;

    /// <summary>How long a mob a hull came down on is left on the ground.</summary>
    public static readonly TimeSpan WfSetDownStun = TimeSpan.FromSeconds(4);

    /// <summary>How many tiles out a mob under a hull is looked a clear spot for.</summary>
    private const int WfSetDownReach = 48;

    private readonly HashSet<EntityUid> _wfSetDownOn = new();
    private GameTick _wfSetDownTick;

    /// <summary>
    /// A hull coming down on a planet layer hurts a mob under it and shoves it clear, where flattening would gib or
    /// delete it. False for anything that is no mob or on no planet, which the caller flattens as before.
    /// </summary>
    private bool WfSetDownOn(EntityUid hull, EntityUid uid, EntityUid map)
    {
        if (!HasComp<MobStateComponent>(uid) || !HasComp<WFPlanetLayerComponent>(map))
            return false;

        if (_wfSetDownTick != _gameTiming.CurTick)
        {
            _wfSetDownTick = _gameTiming.CurTick;
            _wfSetDownOn.Clear();
        }

        // A hull has many fixtures and a mob can lie under several: hurt once, but moved from under each.
        if (_wfSetDownOn.Add(uid))
        {
            var damage = new DamageSpecifier();
            damage.DamageDict.Add("Blunt", WfSetDownDamage);
            _damageSys.TryChangeDamage(uid, damage, origin: hull);
            _stuns.TryParalyze(uid, WfSetDownStun, true);
            _logger.Add(LogType.ShuttleImpact, LogImpact.Medium,
                $"{ToPrettyString(uid):player} was under {ToPrettyString(hull)} as it came down at {Transform(uid).Coordinates:coordinates}");
        }

        if (WfClearSpot(hull, uid, map) is { } spot)
            _transform.SetCoordinates(uid, new EntityCoordinates(map, spot));

        return true;
    }

    /// <summary>The middle of the nearest ground tile to a mob that no hull is over and nothing blocks, ring by ring.</summary>
    private Vector2? WfClearSpot(EntityUid hull, EntityUid uid, EntityUid map)
    {
        if (!TryComp<MapGridComponent>(map, out var ground) || !TryComp<MapGridComponent>(hull, out var hullGrid))
            return null;

        var origin = _mapSystem.WorldToTile(map, ground, _transform.GetWorldPosition(uid));

        for (var ring = 1; ring <= WfSetDownReach; ring++)
        {
            for (var x = -ring; x <= ring; x++)
            for (var y = -ring; y <= ring; y++)
            {
                if (Math.Max(Math.Abs(x), Math.Abs(y)) != ring)
                    continue;

                var index = origin + new Vector2i(x, y);

                if (!_mapSystem.TryGetTileRef(map, ground, index, out var tile) || tile.Tile.IsEmpty)
                    continue;

                var centre = _mapSystem.GridTileToWorldPos(map, ground, index);

                if (WfUnderHull(hull, hullGrid, map, centre) || _turf.IsTileBlocked(tile, CollisionGroup.MobMask))
                    continue;

                return centre;
            }
        }

        return null;
    }

    /// <summary>Whether the tile round a spot on a map is under the hull coming down, or any other.</summary>
    private bool WfUnderHull(EntityUid hull, MapGridComponent hullGrid, EntityUid map, Vector2 centre)
    {
        var half = new Vector2(0.5f);

        // The hull's own tiles, as its place among the map's grids may not be set yet.
        foreach (var tile in _mapSystem.GetTilesIntersecting(hull, hullGrid, new Box2(centre - half, centre + half)))
        {
            if (!tile.Tile.IsEmpty)
                return true;
        }

        return _mapManager.TryFindGridAt(map, centre, out var at, out _) && at != map;
    }
}
