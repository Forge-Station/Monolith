using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Content.Server._WF.NpcCrew.Systems;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Robust.Shared.Map;

namespace Content.Server._WF.NpcCrew.HTN;

/// <summary>Selects an onboard hostile without scavenging weapons or pursuing other grids.</summary>
public sealed partial class WFCrewTargetOperator : HTNOperator
{
    private WFCrewWeaponSystem _weapons = default!;

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _weapons = sysManager.GetEntitySystem<WFCrewWeaponSystem>();
    }

    public override async Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(NPCBlackboard blackboard, CancellationToken cancelToken)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        if (_weapons.PickTarget(owner) is not { } target)
            return (false, null);

        return (true, new Dictionary<string, object>
        {
            { "Target", target },
            { "TargetCoordinates", new EntityCoordinates(target, Vector2.Zero) },
        });
    }

    /// <summary>A plan that is never run does not count as setting out after the target.</summary>
    public override void Startup(NPCBlackboard blackboard)
    {
        base.Startup(blackboard);
        if (blackboard.TryGetValue<EntityUid>("Target", out var target, IoCManager.Resolve<IEntityManager>()))
            _weapons.ClaimAdvance(blackboard.GetValue<EntityUid>(NPCBlackboard.Owner), target);
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        return blackboard.ContainsKey("Target")
            && _weapons.CanEngage(blackboard.GetValue<EntityUid>(NPCBlackboard.Owner), blackboard.GetValue<EntityUid>("Target"))
            ? HTNOperatorStatus.Finished
            : HTNOperatorStatus.Failed;
    }
}
