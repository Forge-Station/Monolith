using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;

namespace Content.Server._WF.NpcIntents;

/// <summary>Stands still while the intent system holds someone up; the system aims it.</summary>
public sealed partial class WFIntentHoldOperator : HTNOperator
{
    /// <inheritdoc/>
    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        return blackboard.ContainsKey(WFNpcIntentSystem.HoldKey) ? HTNOperatorStatus.Continuing : HTNOperatorStatus.Finished;
    }
}
