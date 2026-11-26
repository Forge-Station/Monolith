using System.Threading;
using System.Threading.Tasks;
using Content.Server._WF.NpcCrew.Systems;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;

namespace Content.Server._WF.NpcCrew.HTN;

/// <summary>Plans a physical work destination and performs one job step after walking there.</summary>
public sealed partial class WFCrewWorkOperator : HTNOperator
{
    private WFCrewWorkSystem _work = default!;

    [DataField]
    public bool Perform;

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _work = sysManager.GetEntitySystem<WFCrewWorkSystem>();
    }

    public override async Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(NPCBlackboard blackboard, CancellationToken cancelToken)
    {
        if (!_work.Destination(blackboard.GetValue<EntityUid>(NPCBlackboard.Owner), out var destination))
            return (false, null);

        return (true, new Dictionary<string, object> { { "WFCrewWorkDestination", destination } });
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        return !Perform || _work.Perform(blackboard.GetValue<EntityUid>(NPCBlackboard.Owner))
            ? HTNOperatorStatus.Finished
            : HTNOperatorStatus.Continuing;
    }
}
