using System.Threading;
using System.Threading.Tasks;
using Content.Server._WF.NpcCrew.Systems;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;

namespace Content.Server._WF.NpcCrew.HTN;

/// <summary>Plans a walk to an available ship-gun console.</summary>
public sealed partial class WFPickGunneryOperator : HTNOperator
{
    [Dependency] private IEntityManager _entities = default!;
    private WFGunnerDutySystem _gunners = default!;

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _gunners = sysManager.GetEntitySystem<WFGunnerDutySystem>();
    }

    public override async Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(NPCBlackboard blackboard, CancellationToken cancelToken)
    {
        if (!_gunners.TryFindConsole(blackboard.GetValue<EntityUid>(NPCBlackboard.Owner), out var console))
            return (false, null);

        return (true, new Dictionary<string, object>
        {
            { WFGunnerDutySystem.ConsoleKey, console },
            { WFGunnerDutySystem.CoordinatesKey, _entities.GetComponent<TransformComponent>(console).Coordinates },
        });
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime) => HTNOperatorStatus.Finished;
}
