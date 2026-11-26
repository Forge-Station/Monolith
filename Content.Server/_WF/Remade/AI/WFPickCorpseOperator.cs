using System.Threading;
using System.Threading.Tasks;
using Content.Server._WF.Remade.Conversion;
using Content.Server._WF.Remade.Limbs;
using Content.Server.NPC;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Content.Shared._WF.Remade.Limbs;
using Content.Shared.Mobs.Components;
using Robust.Shared.Map;

namespace Content.Server._WF.Remade.AI;

/// <summary>Finds the nearest corpse, crew or animal, that can still turn and puts it, and where it lies, on the blackboard.</summary>
public sealed partial class WFPickCorpseOperator : HTNOperator
{
    [Dependency] private IEntityManager _entManager = default!;
    private EntityLookupSystem _lookup = default!;
    private SharedTransformSystem _transform = default!;
    private WFRemadeConversionSystem _conversion = default!;
    private WFRemadeLimbsSystem _limbs = default!;

    [DataField(required: true)]
    public string TargetKey = default!;

    [DataField(required: true)]
    public string CoordinatesKey = default!;

    [DataField]
    public float Range = 24f;

    private readonly HashSet<Entity<MobStateComponent>> _bodies = new();

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _lookup = sysManager.GetEntitySystem<EntityLookupSystem>();
        _transform = sysManager.GetEntitySystem<SharedTransformSystem>();
        _conversion = sysManager.GetEntitySystem<WFRemadeConversionSystem>();
        _limbs = sysManager.GetEntitySystem<WFRemadeLimbsSystem>();
    }

    public override async Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(NPCBlackboard blackboard, CancellationToken cancelToken)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        if (!_limbs.Can(owner, WFRemadeLimbStat.Reanimate))
            return (false, null);

        var coordinates = _entManager.GetComponent<TransformComponent>(owner).Coordinates;

        _bodies.Clear();
        _lookup.GetEntitiesInRange(coordinates, Range, _bodies);

        var position = _transform.GetWorldPosition(owner);
        EntityUid? nearest = null;
        var best = float.MaxValue;
        foreach (var body in _bodies)
        {
            if (!_conversion.CanConvert(body))
                continue;

            var distance = (_transform.GetWorldPosition(body) - position).LengthSquared();
            if (distance < best)
            {
                best = distance;
                nearest = body;
            }
        }

        if (nearest is not { } corpse)
            return (false, null);

        EntityCoordinates target = _entManager.GetComponent<TransformComponent>(corpse).Coordinates;
        return (true, new Dictionary<string, object>
        {
            { TargetKey, corpse },
            { CoordinatesKey, target },
        });
    }
}
