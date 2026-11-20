using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Content.Server.NPC.Systems;
using Content.Shared.Interaction;
using Content.Shared.Mobs.Systems;
using Content.Shared.Physics;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Server._WF.Remade.Roster;

/// <summary>
/// Fights from range: closes in until it sees the target from the hold range, stands and lets its abilities fire, and backs
/// away when the target gets too close. Steering is handed to the NPC steering system.
/// </summary>
public sealed partial class WFSkirmishOperator : HTNOperator
{
    [Dependency] private IEntityManager _entManager = default!;
    [Dependency] private IGameTiming _timing = default!;
    private MobStateSystem _mobState = default!;
    private NPCSteeringSystem _steering = default!;
    private SharedInteractionSystem _interaction = default!;
    private SharedTransformSystem _transform = default!;

    /// <summary>The blackboard key holding the target.</summary>
    [DataField]
    public string TargetKey = "Target";

    /// <summary>It stops closing in once it sees the target from this far.</summary>
    [DataField]
    public float HoldRange = 9f;

    /// <summary>Nearer than this, it backs away.</summary>
    [DataField]
    public float BackOffRange = 4f;

    /// <summary>How far it steps back each time.</summary>
    [DataField]
    public float RetreatStep = 3f;

    /// <summary>Seconds between decisions.</summary>
    [DataField]
    public float Interval = 0.4f;

    private const string NextKey = "WFSkirmishNext";

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _mobState = sysManager.GetEntitySystem<MobStateSystem>();
        _steering = sysManager.GetEntitySystem<NPCSteeringSystem>();
        _interaction = sysManager.GetEntitySystem<SharedInteractionSystem>();
        _transform = sysManager.GetEntitySystem<SharedTransformSystem>();
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        if (!blackboard.TryGetValue<EntityUid>(TargetKey, out var target, _entManager)
            || _entManager.Deleted(target)
            || _mobState.IsDead(target))
        {
            return HTNOperatorStatus.Finished;
        }

        var now = _timing.CurTime;
        if (blackboard.TryGetValue<TimeSpan>(NextKey, out var next, _entManager) && now < next)
            return HTNOperatorStatus.Continuing;

        blackboard.SetValue(NextKey, now + TimeSpan.FromSeconds(Interval));
        var ownerPosition = _transform.GetWorldPosition(owner);
        var offset = _transform.GetWorldPosition(target) - ownerPosition;
        var distance = offset.Length();

        if (distance < BackOffRange && distance > 0.01f)
        {
            var destination = ownerPosition - offset / distance * RetreatStep;
            var coordinates = _transform.ToCoordinates(new MapCoordinates(destination, _transform.GetMapId(owner)));
            var steering = _steering.Register(owner, coordinates);
            steering.Range = 0.3f;
            steering.ArriveOnLineOfSight = false;
            steering.DirectMove = true;
            return HTNOperatorStatus.Continuing;
        }

        var seen = distance <= HoldRange && _interaction.InRangeUnobstructed(owner, target, HoldRange + 1f, CollisionGroup.Impassable);
        if (!seen)
        {
            var steering = _steering.Register(owner, _entManager.GetComponent<TransformComponent>(target).Coordinates);
            steering.Range = Math.Max(HoldRange - 1f, 1f);
            steering.ArriveOnLineOfSight = true;
            steering.DirectMove = false;
            return HTNOperatorStatus.Continuing;
        }

        _steering.Unregister(owner);
        return HTNOperatorStatus.Continuing;
    }

    public override void TaskShutdown(NPCBlackboard blackboard, HTNOperatorStatus status)
    {
        base.TaskShutdown(blackboard, status);
        Stop(blackboard);
    }

    public override void PlanShutdown(NPCBlackboard blackboard)
    {
        base.PlanShutdown(blackboard);
        Stop(blackboard);
    }

    private void Stop(NPCBlackboard blackboard)
    {
        blackboard.Remove<TimeSpan>(NextKey);
        _steering.Unregister(blackboard.GetValue<EntityUid>(NPCBlackboard.Owner));
    }
}
