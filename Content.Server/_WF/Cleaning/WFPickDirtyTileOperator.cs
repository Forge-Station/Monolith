using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Content.Server.NPC;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Robust.Shared.Map;

namespace Content.Server._WF.Cleaning;

/// <summary>
/// Picks a nearby tile with cleanable decals on the NPC's grid and puts its centre into the target key.
/// </summary>
public sealed partial class WFPickDirtyTileOperator : HTNOperator
{
    [Dependency] private IEntityManager _entManager = default!;
    private WFDecalCleaningSystem _cleaning = default!;

    /// <summary>How far to look for dirt, in tiles.</summary>
    [DataField]
    public float Range = 10f;

    /// <summary>How many of the nearest dirty tiles the pick is made from.</summary>
    [DataField]
    public int Choices = 4;

    [DataField]
    public string TargetCoordinates = "TargetCoordinates";

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _cleaning = sysManager.GetEntitySystem<WFDecalCleaningSystem>();
    }

    public override Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(NPCBlackboard blackboard,
        CancellationToken cancelToken)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        var xform = _entManager.GetComponent<TransformComponent>(owner);

        if (xform.GridUid is not { } gridUid || xform.ParentUid != gridUid ||
            !_cleaning.TryPickDirtyTile(gridUid, xform.LocalPosition, Range, Choices, out var tile))
        {
            return Task.FromResult<(bool, Dictionary<string, object>?)>((false, null));
        }

        var target = new EntityCoordinates(gridUid, (Vector2) tile + new Vector2(0.5f, 0.5f));
        return Task.FromResult<(bool, Dictionary<string, object>?)>((true, new Dictionary<string, object>
        {
            { TargetCoordinates, target },
        }));
    }
}
