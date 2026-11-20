using Robust.Server.Player;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;

namespace Content.Server._WF.Planets;

/// <summary>Builds a hull-wide audio filter: players on the grid plus those hovering over it.</summary>
// AddInGrid alone misses ghosts, which parent to the map rather than the grid.
public sealed partial class WFGridAudienceSystem : EntitySystem
{
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    /// <summary>Margin, in tiles, around the hull's bounds that still counts as aboard.</summary>
    public const float OverheadMargin = 4f;

    /// <summary>Everyone aboard the grid or hovering over it.</summary>
    public Filter Aboard(EntityUid grid)
    {
        var filter = Filter.Empty().AddInGrid(grid, EntityManager);

        if (!HasComp<MapGridComponent>(grid) || Transform(grid).MapUid is not { } map)
            return filter;

        var bounds = _lookup.GetWorldAABB(grid).Enlarged(OverheadMargin);

        foreach (var session in _players.Sessions)
        {
            if (session.AttachedEntity is not { } ent || !TryComp<TransformComponent>(ent, out var xform))
                continue;

            if (xform.GridUid == grid || xform.MapUid != map)
                continue;

            if (bounds.Contains(_transform.GetWorldPosition(xform)))
                filter.AddPlayer(session);
        }

        return filter;
    }
}
