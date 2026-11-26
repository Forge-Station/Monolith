using Content.Server._WF.NpcCrew.Systems;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;

namespace Content.Server._WF.NpcCrew.HTN;

/// <summary>Reloads an empty crew weapon before selecting ranged combat.</summary>
public sealed partial class WFReloadOperator : HTNOperator
{
    private WFCrewWeaponSystem _weapons = default!;

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _weapons = sysManager.GetEntitySystem<WFCrewWeaponSystem>();
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        _weapons.TryReloadOrSwitch(blackboard.GetValue<EntityUid>(NPCBlackboard.Owner));
        return HTNOperatorStatus.Finished;
    }
}
