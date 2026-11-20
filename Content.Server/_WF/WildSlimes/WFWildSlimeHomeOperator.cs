using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Content.Server.NPC;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Robust.Shared.Map;
using Robust.Shared.Random;

namespace Content.Server._WF.WildSlimes;

/// <summary>Plans a walk back into its patch for a wild slime that has strayed past its leash.</summary>
public sealed partial class WFWildSlimeHomeOperator : HTNOperator
{
    [Dependency] private IEntityManager _ent = default!;
    [Dependency] private IRobustRandom _random = default!;

    private SharedTransformSystem _transform = default!;

    [DataField]
    public string TargetKey = "TargetCoordinates";

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _transform = sysManager.GetEntitySystem<SharedTransformSystem>();
    }

    public override Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(NPCBlackboard blackboard,
        CancellationToken cancelToken)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        if (!_ent.TryGetComponent<WFWildSlimeComponent>(owner, out var wild)
            || !_ent.TryGetComponent<TransformComponent>(owner, out var xform)
            || xform.MapUid != wild.HomeMap)
            return Task.FromResult<(bool, Dictionary<string, object>?)>((false, null));

        var position = _transform.GetWorldPosition(xform);
        if (Vector2.Distance(position, wild.Home) <= wild.Leash)
            return Task.FromResult<(bool, Dictionary<string, object>?)>((false, null));

        var spot = wild.Home + _random.NextVector2(wild.Leash * 0.5f);
        return Task.FromResult<(bool, Dictionary<string, object>?)>((true, new Dictionary<string, object>
        {
            { TargetKey, new EntityCoordinates(wild.HomeMap, spot) },
        }));
    }
}
