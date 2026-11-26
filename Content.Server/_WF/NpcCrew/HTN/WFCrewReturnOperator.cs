using System.Threading;
using System.Threading.Tasks;
using Content.Server._WF.NpcCrew.Components;
using Content.Server._WF.NpcCrew.Systems;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;

namespace Content.Server._WF.NpcCrew.HTN;

/// <summary>Returns displaced crew to their assigned ship.</summary>
public sealed partial class WFCrewReturnOperator : HTNOperator
{
    [Dependency] private IEntityManager _entities = default!;

    public override async Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(NPCBlackboard blackboard, CancellationToken cancelToken)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        if (!_entities.TryGetComponent<WFCrewComponent>(owner, out var crew) || crew.Post is not { } post
            || !post.IsValid(_entities) || _entities.GetComponent<TransformComponent>(owner).GridUid == post.EntityId)
            return (false, null);

        var next = _entities.System<WFCrewWorkSystem>().Waypoint(owner, post, crew.PostRange);
        return (true, new Dictionary<string, object> { { "WFCrewReturn", next } });
    }
}
