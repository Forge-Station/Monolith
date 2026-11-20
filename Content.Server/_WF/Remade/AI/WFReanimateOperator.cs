using Content.Server._WF.Remade.Conversion;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;

namespace Content.Server._WF.Remade.AI;

/// <summary>An Infector drives its proboscis into a corpse and stays with it until it turns.</summary>
public sealed partial class WFReanimateOperator : HTNOperator
{
    [Dependency] private IEntityManager _entManager = default!;
    private SharedAudioSystem _audio = default!;
    private WFRemadeConversionSystem _conversion = default!;

    [DataField(required: true)]
    public string TargetKey = default!;

    [DataField]
    public SoundSpecifier Sound = new SoundCollectionSpecifier("WFRemadeInfectorWork");

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _audio = sysManager.GetEntitySystem<SharedAudioSystem>();
        _conversion = sysManager.GetEntitySystem<WFRemadeConversionSystem>();
    }

    public override void Startup(NPCBlackboard blackboard)
    {
        base.Startup(blackboard);
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        if (!blackboard.TryGetValue<EntityUid>(TargetKey, out var corpse, _entManager))
            return;

        var source = _entManager.TryGetComponent<WFRemadeComponent>(owner, out var remade) ? remade.Source : null;
        if (_conversion.TryStartConversion(corpse, source, owner))
            _audio.PlayPvs(Sound, owner);
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        base.Update(blackboard, frameTime);
        if (!blackboard.TryGetValue<EntityUid>(TargetKey, out var corpse, _entManager)
            || _entManager.Deleted(corpse))
        {
            return HTNOperatorStatus.Finished;
        }

        return _entManager.HasComponent<WFRemadeConvertingComponent>(corpse)
            ? HTNOperatorStatus.Continuing
            : HTNOperatorStatus.Failed;
    }

    public override void TaskShutdown(NPCBlackboard blackboard, HTNOperatorStatus status)
    {
        base.TaskShutdown(blackboard, status);
        blackboard.Remove<EntityUid>(TargetKey);
    }
}
