using Content.Server._WF.NpcCrew.Components;
using Content.Server._WF.NpcCrew.Systems;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;

namespace Content.Server._WF.NpcCrew.HTN;

/// <summary>Operates a console until the gunner is displaced, interrupted, or incapacitated.</summary>
public sealed partial class WFTakeGunneryOperator : HTNOperator
{
    [Dependency] private IEntityManager _entities = default!;
    private WFGunnerDutySystem _gunners = default!;

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _gunners = sysManager.GetEntitySystem<WFGunnerDutySystem>();
    }

    public override void Startup(NPCBlackboard blackboard)
    {
        base.Startup(blackboard);
        if (blackboard.TryGetValue<EntityUid>(WFGunnerDutySystem.ConsoleKey, out var console, _entities))
            _gunners.TryTakeConsole(blackboard.GetValue<EntityUid>(NPCBlackboard.Owner), console);
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        return _entities.TryGetComponent<WFGunnerDutyComponent>(blackboard.GetValue<EntityUid>(NPCBlackboard.Owner), out var duty)
            && duty.AtConsole ? HTNOperatorStatus.Continuing : HTNOperatorStatus.Failed;
    }

    public override void TaskShutdown(NPCBlackboard blackboard, HTNOperatorStatus status)
    {
        base.TaskShutdown(blackboard, status);
        if (status != HTNOperatorStatus.Failed)
            _gunners.Release(blackboard.GetValue<EntityUid>(NPCBlackboard.Owner));
    }
}
