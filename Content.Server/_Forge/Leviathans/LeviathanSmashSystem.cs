using System.Numerics;
using Content.Shared._Forge.Leviathans.Components;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Ghost;
using Content.Shared.Mobs.Components;
using Content.Shared.Tag;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.Server._Forge.Leviathans;

/// <summary>
/// Chews or crushes shuttle tiles around a leviathan body.
/// </summary>
public sealed partial class LeviathanSmashSystem : EntitySystem
{
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private TagSystem _tag = default!;

    private static readonly ProtoId<TagPrototype> WallTag = "Wall";
    private static readonly ProtoId<TagPrototype> WindowTag = "Window";

    private List<Entity<MapGridComponent>> _grids = new();
    private readonly List<EntityUid> _anchored = new();

    public void Chew(EntityUid user, MapCoordinates coords, float radius)
    {
        Smash(user, coords, radius, null, destroyTiles: true, wallsOnly: false);
    }

    /// <summary>
    /// Instant singularity-style deletion: tiles, walls, windows, machines — gone.
    /// </summary>
    public void Swallow(EntityUid user, MapCoordinates coords, float radius)
    {
        Smash(user, coords, radius, null, destroyTiles: true, wallsOnly: false);

        foreach (var ent in _lookup.GetEntitiesInRange(coords, radius, LookupFlags.Static | LookupFlags.Sundries | LookupFlags.Uncontained))
        {
            if (!CanSmash(user, ent))
                continue;

            QueueDel(ent);
        }
    }

    public void Crush(EntityUid user, MapCoordinates coords, float radius, DamageSpecifier damage)
    {
        Smash(user, coords, radius, damage, destroyTiles: false, wallsOnly: true);
    }

    public void Smash(
        EntityUid user,
        MapCoordinates coords,
        float radius,
        DamageSpecifier? damage,
        bool destroyTiles,
        bool wallsOnly)
    {
        if (radius <= 0f)
            return;

        _grids.Clear();
        var half = new Vector2(radius, radius);
        var box = new Box2(coords.Position - half, coords.Position + half);
        _map.FindGridsIntersecting(coords.MapId, box, ref _grids, includeMap: false);

        var circle = new Circle(coords.Position, radius);

        foreach (var grid in _grids)
        {
            if (grid.Owner == user)
                continue;

            foreach (var tile in _map.GetTilesIntersecting(grid.Owner, grid.Comp, circle))
            {
                _anchored.Clear();
                foreach (var ent in _map.GetAnchoredEntities(grid.Owner, grid.Comp, tile.GridIndices))
                    _anchored.Add(ent);

                foreach (var ent in _anchored)
                {
                    if (!CanSmash(user, ent))
                        continue;

                    if (wallsOnly &&
                        !_tag.HasTag(ent, WallTag) &&
                        !_tag.HasTag(ent, WindowTag))
                        continue;

                    if (destroyTiles)
                    {
                        QueueDel(ent);
                        continue;
                    }

                    if (damage != null)
                        _damageable.TryChangeDamage(ent, damage, ignoreResistances: true, origin: user);
                }

                if (destroyTiles && !tile.Tile.IsEmpty)
                    _map.SetTile(grid.Owner, grid.Comp, tile.GridIndices, Tile.Empty);
            }
        }
    }

    public bool TryGetGridAabb(EntityUid gridUid, MapGridComponent grid, out Box2 worldAabb)
    {
        worldAabb = _lookup.GetWorldAABB(gridUid);
        return worldAabb.Width > 0.01f && worldAabb.Height > 0.01f;
    }

    public MapCoordinates GetMapCoordinates(EntityUid uid)
    {
        return _transform.GetMapCoordinates(uid);
    }

    private bool CanSmash(EntityUid user, EntityUid target)
    {
        if (target == user || TerminatingOrDeleted(target))
            return false;

        if (HasComp<GhostComponent>(target) ||
            HasComp<GodmodeComponent>(target) ||
            HasComp<MapGridComponent>(target) ||
            HasComp<MapComponent>(target) ||
            HasComp<MobStateComponent>(target) ||
            HasComp<VoidWormSegmentComponent>(target) ||
            HasComp<VoidDrakeComponent>(target) ||
            HasComp<VoidWormComponent>(target) ||
            HasComp<VoidMedusaComponent>(target))
            return false;

        return true;
    }
}
