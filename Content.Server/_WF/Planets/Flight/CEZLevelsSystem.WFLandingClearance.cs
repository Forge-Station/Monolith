using System.Numerics;
using Content.Server.Parallax;
using Content.Shared._CE.ZLevels.Core.EntitySystems;
using Content.Shared._WF.LandingGuide;
using Content.Shared._WF.Planets;
using Content.Shared.Shuttles.Components;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;

namespace Content.Server._CE.ZLevels.Core;

public sealed partial class CEZLevelsSystem
{
    [Dependency] private BiomeSystem _wfLandingBiome = default!;
    /// <summary>Clearance, in metres, broken around an impacting hull's tiles.</summary>
    public const float WFLandingClearance = 1f;

    /// <summary>
    /// Clearance, in metres, broken around a hull that set down gently: what it would rest against, such as a tree
    /// beside it, and no more. Left standing, those shove the hull about as it settles.
    /// </summary>
    public const float WFSetDownClearance = 0.25f;

    private readonly List<CESharedZLevelsSystem.CEWallContact> _wfLandedContacts = new();

    /// <summary>Clear breathing room around a hull that came down, without touching other grids or occupants.</summary>
    /// <param name="grownOnly">Break only what the planet grew, such as trees and rock, and leave what someone built.</param>
    public void WfClearLandingObstacles(EntityUid hull, bool reportImpacts = false, float clearance = WFLandingClearance,
        bool grownOnly = false)
    {
        if (HasComp<WFDetachedTerrainComponent>(hull) || !WfHasSkidGround(hull)
            || !TryComp<MapGridComponent>(hull, out var hullGrid)
            || Transform(hull).MapUid is not { } ground
            || !TryComp<MapGridComponent>(ground, out var groundGrid))
            return;

        var bounds = _transform.GetWorldMatrix(hull).TransformBox(hullGrid.LocalAABB).Enlarged(clearance + 1f);
        var obstacles = new HashSet<EntityUid>();
        foreach (var obstacle in _map.GetAnchoredEntities(ground, groundGrid, new Box2Rotated(bounds, Angle.Zero)))
        {
            if (TerminatingOrDeleted(obstacle) || Transform(obstacle).GridUid != ground
                || !TryComp<FixturesComponent>(obstacle, out var fixtures) || !WFLandingSurveySystem.IsSolid(fixtures))
                continue;

            var pos = _transform.GetWorldPosition(obstacle);

            if (grownOnly
                && !(TryComp<Content.Shared.Parallax.Biomes.BiomeComponent>(ground, out var biome)
                     && _wfLandingBiome.WfIsBiomeSpawned((ground, biome), obstacle, _map.WorldToTile(ground, groundGrid, pos))))
                continue;
            var half = new Vector2(groundGrid.TileSize * 0.5f + clearance);
            // Query real hull tiles so empty corners and large gaps between wings are preserved, even at an angle.
            foreach (var tile in _map.GetTilesIntersecting(hull, hullGrid, new Box2(pos - half, pos + half)))
            {
                if (tile.Tile.IsEmpty)
                    continue;
                obstacles.Add(obstacle);
                break;
            }
        }

        foreach (var obstacle in obstacles)
        {
            WfBreakLandingObstacle(hull, (ground, groundGrid), obstacle, reportImpacts);
        }
    }

    /// <summary>
    /// Breaks anything solid a hull that just came down overlaps and would keep being shoved out of, grown or placed,
    /// so no way of landing leaves a hull wedged. A wall it barely touches stays; the hull slides off that by itself.
    /// </summary>
    public void WfBreakLandedContacts(EntityUid hull)
    {
        if (HasComp<WFDetachedTerrainComponent>(hull) || !WfHasSkidGround(hull)
            || Transform(hull).MapUid is not { } ground
            || !TryComp<MapGridComponent>(ground, out var groundGrid))
            return;

        _wfLandedContacts.Clear();
        GetWallContacts(hull, _wfLandedContacts);

        var margin = (0.5f - WFLandingSurveySystem.ObstacleHalfExtent) * groundGrid.TileSize;
        foreach (var contact in _wfLandedContacts)
        {
            var overlapX = MathF.Min(contact.ShipTile.Right, contact.WallTile.Right) - MathF.Max(contact.ShipTile.Left, contact.WallTile.Left);
            var overlapY = MathF.Min(contact.ShipTile.Top, contact.WallTile.Top) - MathF.Max(contact.ShipTile.Bottom, contact.WallTile.Bottom);
            var wall = contact.Wall;

            if (TerminatingOrDeleted(wall) || EntityManager.IsQueuedForDeletion(wall)
                || Transform(wall).GridUid != ground || HasComp<FTLSmashImmuneComponent>(wall))
                continue;

            // Crushable things go at any overlap; a wall only where the survey would have called it under the hull.
            var least = _wfLandingSurvey.IsCrushable(wall) ? WallPenetrationDeadZone : margin;
            if (overlapX > least && overlapY > least)
                WfBreakLandingObstacle(hull, (ground, groundGrid), wall, false);
        }
    }

    private void WfBreakLandingObstacle(EntityUid hull, Entity<MapGridComponent> ground, EntityUid obstacle, bool reportImpact)
    {
        var index = _map.WorldToTile(ground, ground.Comp, _transform.GetWorldPosition(obstacle));
        var min = new Vector2(index.X, index.Y) * ground.Comp.TileSize;
        var reserved = new List<(Vector2i, Tile)>();
        _wfLandingBiome.ReserveTiles(ground, new Box2(min, min + new Vector2(ground.Comp.TileSize)), reserved, mapGrid: ground.Comp);
        if (reportImpact)
            _wfFlight.GroundObstacleImpact(hull, _transform.GetWorldPosition(obstacle));
        _wfDestructible.BreakEntity(obstacle);
        if (!TerminatingOrDeleted(obstacle))
            QueueDel(obstacle);
    }
}
