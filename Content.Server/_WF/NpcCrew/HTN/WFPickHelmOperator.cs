using System.Threading;
using System.Threading.Tasks;
using Content.Server._WF.NpcCrew.Systems;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;

namespace Content.Server._WF.NpcCrew.HTN;

/// <summary>
/// Picks the helm a pilot works and writes it and its coordinates to the blackboard. Fails at planning when the
/// grid has no usable helm, so the pilot idles instead.
/// </summary>
public sealed partial class WFPickHelmOperator : HTNOperator
{
    [Dependency] private IEntityManager _entManager = default!;

    private WFPilotDutySystem _pilot = default!;

    /// <summary>Takes the nearest helm however it is turned, for the branch tried when no square one can be reached.</summary>
    [DataField]
    public bool AnyHelm;

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _pilot = sysManager.GetEntitySystem<WFPilotDutySystem>();
    }

    public override async Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(NPCBlackboard blackboard,
        CancellationToken cancelToken)
    {
        if (!_pilot.TryFindHelm(blackboard.GetValue<EntityUid>(NPCBlackboard.Owner), out var helm, !AnyHelm))
            return (false, null);

        return (true, new Dictionary<string, object>
        {
            { WFPilotDutySystem.HelmKey, helm },
            { WFPilotDutySystem.HelmCoordinatesKey, _entManager.GetComponent<TransformComponent>(helm).Coordinates },
        });
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        if (!_pilot.TryFindHelm(blackboard.GetValue<EntityUid>(NPCBlackboard.Owner), out var helm, !AnyHelm))
            return HTNOperatorStatus.Failed;

        blackboard.SetValue(WFPilotDutySystem.HelmKey, helm);
        blackboard.SetValue(WFPilotDutySystem.HelmCoordinatesKey, _entManager.GetComponent<TransformComponent>(helm).Coordinates);
        return HTNOperatorStatus.Finished;
    }
}
