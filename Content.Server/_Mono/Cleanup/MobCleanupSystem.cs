using Content.Server.Ghost.Roles.Components;
using Content.Server.NPC.HTN;
using Content.Shared._Mono.CCVar;
using Robust.Shared.Configuration;

namespace Content.Server._Mono.Cleanup;

/// <summary>
///     Deletes mobs too far from players.
/// </summary>
public sealed partial class MobCleanupSystem : BaseCleanupSystem<HTNComponent>
{
    [Dependency] private CleanupHelperSystem _cleanup = default!;
    [Dependency] private IConfigurationManager _cfg = default!;

    private float _maxDistance;
    private float _maxGridDistance;

    private EntityQuery<GhostRoleComponent> _ghostQuery;
    private EntityQuery<CleanupImmuneComponent> _immuneQuery;
    private EntityQuery<TransformComponent> _xformQuery;

    public override void Initialize()
    {
        base.Initialize();

        _ghostQuery = GetEntityQuery<GhostRoleComponent>();
        _immuneQuery = GetEntityQuery<CleanupImmuneComponent>();
        _xformQuery = GetEntityQuery<TransformComponent>();

        Subs.CVar(_cfg, MonoCVars.MobCleanupDistance, val => _maxDistance = val, true);
        Subs.CVar(_cfg, MonoCVars.CleanupMaxGridDistance, val => _maxGridDistance = val, true);
    }

    /// <summary>
    ///     Forge-Change: cheap pre-filter to skip mobs that are clearly not cleanup
    ///     candidates (still on a grid, immune, ghost-role) before the proximity queries run.
    /// </summary>
    protected override bool ShouldEnqueue(EntityUid uid)
    {
        if (_immuneQuery.HasComp(uid) || _ghostQuery.HasComp(uid))
            return false;

        if (!_xformQuery.TryGetComponent(uid, out var xform))
            return false;

        return xform.GridUid == null;
    }

    protected override bool ShouldEntityCleanup(EntityUid uid)
    {
        var xform = Transform(uid);

        return xform.GridUid == null
            && !_immuneQuery.HasComp(uid)
            && !_ghostQuery.HasComp(uid)
            && !CleanupHelper.HasNearbyPlayers(xform.Coordinates, _maxDistance)
            && !CleanupHelper.HasNearbyGrids(xform.Coordinates, _maxGridDistance);
    }
}
