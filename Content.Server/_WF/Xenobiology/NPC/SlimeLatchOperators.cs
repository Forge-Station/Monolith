using System.Threading;
using System.Threading.Tasks;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Content.Server.NPC.Pathfinding;
using Content.Shared._WF.Xenobiology.Components;
using Content.Shared._WF.Xenobiology.Systems;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC.Systems;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;

namespace Content.Server._WF.Xenobiology.NPC;

/// <summary>
/// Picks a hostile in range for a hungry or grown slime to latch onto. Tamers are spared unless the slime is grown and
/// hungry.
/// </summary>
public sealed partial class PickSlimeLatchTargetOperator : HTNOperator
{
    [Dependency] private IEntityManager _ent = default!;

    private HungerSystem _hunger = default!;
    private MobStateSystem _mobState = default!;
    private NpcFactionSystem _factions = default!;
    private PathfindingSystem _pathfinding = default!;
    private SlimeLatchSystem _latch = default!;

    [DataField(required: true)]
    public string RangeKey = string.Empty;

    [DataField(required: true)]
    public string TargetKey = string.Empty;

    [DataField]
    public string LatchKey = string.Empty;

    [DataField]
    public string PathfindKey = NPCBlackboard.PathfindKey;

    /// <summary>
    /// Hunger above which a slime counts as fed.
    /// </summary>
    [DataField]
    public float Peckish = 25f;

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _hunger = sysManager.GetEntitySystem<HungerSystem>();
        _mobState = sysManager.GetEntitySystem<MobStateSystem>();
        _factions = sysManager.GetEntitySystem<NpcFactionSystem>();
        _pathfinding = sysManager.GetEntitySystem<PathfindingSystem>();
        _latch = sysManager.GetEntitySystem<SlimeLatchSystem>();
    }

    public override async Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(NPCBlackboard blackboard,
        CancellationToken cancelToken)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        if (!blackboard.TryGetValue<float>(RangeKey, out var range, _ent)
            || !_ent.TryGetComponent<SlimeComponent>(owner, out var slime)
            || !_ent.TryGetComponent<MobGrowthComponent>(owner, out var growth)
            || !_ent.TryGetComponent<HungerComponent>(owner, out var hunger)
            || _latch.IsLatched((owner, slime)))
            return (false, null);

        var full = _hunger.GetHunger(hunger) > Peckish;
        if (growth.IsFirstStage && full)
            return (false, null);

        var targets = new List<EntityUid>();
        foreach (var entity in _factions.GetNearbyHostiles(owner, range))
        {
            if (_ent.HasComponent<BeingLatchedComponent>(entity)
                || _ent.HasComponent<SlimeDamageOvertimeComponent>(entity)
                || _mobState.IsDead(entity)
                || entity == slime.Tamer && (full || growth.IsFirstStage))
                continue;

            targets.Add(entity);
        }

        foreach (var target in targets)
        {
            if (!_ent.TryGetComponent<TransformComponent>(target, out var xform))
                continue;

            var path = await _pathfinding.GetPath(owner, target, range, cancelToken);
            if (path.Result != PathResult.Path)
                continue;

            return (true, new Dictionary<string, object>
            {
                { TargetKey, xform.Coordinates },
                { LatchKey, target },
                { PathfindKey, path },
            });
        }

        return (false, null);
    }
}

/// <summary>
/// Latches onto the target in <see cref="LatchKey"/>.
/// </summary>
public sealed partial class SlimeLatchOperator : HTNOperator
{
    [Dependency] private IEntityManager _ent = default!;

    private SlimeLatchSystem _latch = default!;

    [DataField]
    public string LatchKey = string.Empty;

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _latch = sysManager.GetEntitySystem<SlimeLatchSystem>();
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        base.Update(blackboard, frameTime);
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        if (!blackboard.TryGetValue<EntityUid>(LatchKey, out var target, _ent)
            || !_ent.TryGetComponent<SlimeComponent>(owner, out var slime))
            return HTNOperatorStatus.Failed;

        if (_latch.IsLatched((owner, slime), target))
            return HTNOperatorStatus.Finished;

        if (_ent.HasComponent<BeingLatchedComponent>(target))
            return HTNOperatorStatus.Continuing;

        return _latch.NpcTryLatch((owner, slime), target) ? HTNOperatorStatus.Continuing : HTNOperatorStatus.Failed;
    }
}
