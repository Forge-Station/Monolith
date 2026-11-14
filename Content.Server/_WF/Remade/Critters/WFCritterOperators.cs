using System.Threading;
using System.Threading.Tasks;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;

namespace Content.Server._WF.Remade.Critters;

/// <summary>Picks cover for a hider and puts it, where it is, and how close to get on the blackboard.</summary>
public sealed partial class WFPickCoverOperator : HTNOperator
{
    [Dependency] private IEntityManager _entManager = default!;
    private WFCoverSystem _cover = default!;

    [DataField(required: true)]
    public string TargetKey = default!;

    [DataField(required: true)]
    public string CoordinatesKey = default!;

    [DataField(required: true)]
    public string RangeKey = default!;

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _cover = sysManager.GetEntitySystem<WFCoverSystem>();
    }

    public override async Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(NPCBlackboard blackboard, CancellationToken cancelToken)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        if (!_entManager.TryGetComponent<WFHiderComponent>(owner, out var hider)
            || _cover.PickCover((owner, hider)) is not { } cover)
        {
            return (false, null);
        }

        return (true, new Dictionary<string, object>
        {
            { TargetKey, cover },
            { CoordinatesKey, _entManager.GetComponent<TransformComponent>(cover).Coordinates },
            { RangeKey, hider.Reach },
        });
    }
}

/// <summary>Climbs under the picked cover and stays there until the plan is dropped: prey in reach, a hunt order, a wound.</summary>
public sealed partial class WFHideOperator : HTNOperator
{
    [Dependency] private IEntityManager _entManager = default!;
    private WFCoverSystem _cover = default!;

    [DataField(required: true)]
    public string TargetKey = default!;

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _cover = sysManager.GetEntitySystem<WFCoverSystem>();
    }

    public override void Startup(NPCBlackboard blackboard)
    {
        base.Startup(blackboard);
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        if (_entManager.TryGetComponent<WFHiderComponent>(owner, out var hider)
            && blackboard.TryGetValue<EntityUid>(TargetKey, out var cover, _entManager))
        {
            _cover.Hide((owner, hider), cover);
        }
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        base.Update(blackboard, frameTime);
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        return _cover.IsHiding(owner) ? HTNOperatorStatus.Continuing : HTNOperatorStatus.Failed;
    }

    public override void TaskShutdown(NPCBlackboard blackboard, HTNOperatorStatus status)
    {
        base.TaskShutdown(blackboard, status);
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        if (_entManager.TryGetComponent<WFHiderComponent>(owner, out var hider))
            _cover.Emerge((owner, hider), 0f);

        blackboard.Remove<EntityUid>(TargetKey);
    }
}

/// <summary>Picks a spot against a wall for a hugger and puts it, and how close to get, on the blackboard.</summary>
public sealed partial class WFPickWallOperator : HTNOperator
{
    [Dependency] private IEntityManager _entManager = default!;
    private WFWallClingSystem _cling = default!;

    [DataField(required: true)]
    public string CoordinatesKey = default!;

    [DataField(required: true)]
    public string RangeKey = default!;

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _cling = sysManager.GetEntitySystem<WFWallClingSystem>();
    }

    public override async Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(NPCBlackboard blackboard, CancellationToken cancelToken)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        if (!_entManager.TryGetComponent<WFWallHuggerComponent>(owner, out var hugger)
            || _cling.PickSpot((owner, hugger)) is not { } spot)
        {
            return (false, null);
        }

        return (true, new Dictionary<string, object>
        {
            { CoordinatesKey, spot },
            { RangeKey, hugger.Reach },
        });
    }
}

/// <summary>Stands still until a better plan comes along.</summary>
public sealed partial class WFHoldOperator : HTNOperator
{
    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        return HTNOperatorStatus.Continuing;
    }
}
