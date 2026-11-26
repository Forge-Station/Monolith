using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Content.Shared.Fluids;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server._WF.Cleaning;

/// <summary>
/// Washes the cleanable decals off the tile at the target key, as a cleanbot does once it reaches it.
/// </summary>
public sealed partial class WFCleanTileOperator : HTNOperator
{
    [Dependency] private IEntityManager _entManager = default!;
    private WFDecalCleaningSystem _cleaning = default!;
    private SharedAudioSystem _audio = default!;
    private SharedMapSystem _map = default!;
    private SharedTransformSystem _transform = default!;

    [DataField]
    public string TargetKey = "TargetCoordinates";

    /// <summary>Reagent units one wash may spend; each decal costs a quarter unit.</summary>
    [DataField]
    public float Budget = 10f;

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _cleaning = sysManager.GetEntitySystem<WFDecalCleaningSystem>();
        _audio = sysManager.GetEntitySystem<SharedAudioSystem>();
        _map = sysManager.GetEntitySystem<SharedMapSystem>();
        _transform = sysManager.GetEntitySystem<SharedTransformSystem>();
    }

    public override void TaskShutdown(NPCBlackboard blackboard, HTNOperatorStatus status)
    {
        base.TaskShutdown(blackboard, status);
        blackboard.Remove<EntityCoordinates>(TargetKey);
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        if (!blackboard.TryGetValue<EntityCoordinates>(TargetKey, out var target, _entManager) ||
            _transform.GetGrid(target) is not { } gridUid ||
            !_entManager.TryGetComponent<MapGridComponent>(gridUid, out var grid))
        {
            return HTNOperatorStatus.Failed;
        }

        var tile = _map.TileIndicesFor(gridUid, grid, target);
        if (_cleaning.CleanTile(gridUid, grid, tile, Budget) <= 0f)
            return HTNOperatorStatus.Failed;

        if (_entManager.TryGetComponent<AbsorbentComponent>(owner, out var absorbent))
            _audio.PlayPvs(absorbent.PickupSound, target);

        return HTNOperatorStatus.Finished;
    }
}
