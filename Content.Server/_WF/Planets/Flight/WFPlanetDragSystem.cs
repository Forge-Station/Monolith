using Content.Server._NF.Shuttles.Components;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._WF.Planets.Flight;
using Content.Shared._WF.Planets;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.Server._WF.Planets.Flight;

/// <summary>Speed cap and heavy damping on planet layers so hulls don't outrun terrain streaming.</summary>
// A per-tick sweep, not subscriptions: grids change maps through transit, FTL and CE hops this module doesn't own.
public sealed partial class WFPlanetDragSystem : EntitySystem
{
    [Dependency] private SharedPhysicsSystem _physics = default!;

    /// <summary>Speed cap multiplier for a hull that has lost lift and is gliding.</summary>
    public const float LiftLostAllowance = 1.5f;

    /// <inheritdoc/>
    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<MapGridComponent, PhysicsComponent, TransformComponent>();

        while (query.MoveNext(out var uid, out _, out var body, out var xform))
        {
            // A z-layer map is itself a grid: never drag the layer.
            if (HasComp<MapComponent>(uid))
                continue;

            if (!TryGetLimits(uid, xform, out var maxSpeed, out var damping))
            {
                Release(uid, body);
                continue;
            }

            Hold(uid, body, damping);
            Clamp(uid, body, maxSpeed);
        }
    }

    /// <summary>The speed cap and damping for this grid, or false when it should not be dragged.</summary>
    private bool TryGetLimits(EntityUid grid, TransformComponent xform, out float maxSpeed, out float damping)
    {
        maxSpeed = 0f;
        damping = 0f;

        // Transit gaps have no layer marker, so falls are never dragged.
        if (xform.MapUid is not { } mapUid || !TryComp<WFPlanetLayerComponent>(mapUid, out var layer))
            return false;

        // The ground layer is left to CE friction and skids.
        if (HasComp<CEZGroundLayerComponent>(mapUid) || (TryComp<CEZMapComponent>(mapUid, out var zMap) && zMap.Depth == 0))
            return false;

        if (HasComp<WFDetachedTerrainComponent>(grid) || HasComp<ForceAnchorComponent>(grid))
            return false;

        if (TryComp<WFOrbitLayerComponent>(mapUid, out var orbit))
        {
            maxSpeed = orbit.MaxSpeed;
            damping = orbit.LinearDamping;
            return true;
        }

        maxSpeed = layer.MaxSpeed;
        damping = layer.LinearDamping;
        return true;
    }

    /// <summary>Applies the layer's damping, saving the grid's own the first time.</summary>
    private void Hold(EntityUid grid, PhysicsComponent body, float damping)
    {
        if (!TryComp<WFPlanetDragComponent>(grid, out var comp))
        {
            comp = AddComp<WFPlanetDragComponent>(grid);
            comp.OriginalDamping = body.LinearDamping;
        }

        _physics.SetLinearDamping(grid, body, damping);
    }

    /// <summary>Restores the grid's own damping once it leaves a dragged layer.</summary>
    private void Release(EntityUid grid, PhysicsComponent body)
    {
        if (!TryComp<WFPlanetDragComponent>(grid, out var comp))
            return;

        _physics.SetLinearDamping(grid, body, comp.OriginalDamping);
        RemComp<WFPlanetDragComponent>(grid);
    }

    /// <summary>Clamps a grid to the layer's speed cap; damping alone doesn't bound speed.</summary>
    private void Clamp(EntityUid grid, PhysicsComponent body, float maxSpeed)
    {
        if (maxSpeed <= 0f)
            return;

        if (HasComp<WFLiftLostComponent>(grid))
            maxSpeed *= LiftLostAllowance;

        var speedSquared = body.LinearVelocity.LengthSquared();

        // Skip non-finite speed; scaling it would write NaN velocity.
        if (!float.IsFinite(speedSquared) || speedSquared <= maxSpeed * maxSpeed)
            return;

        _physics.SetLinearVelocity(grid, body.LinearVelocity * (maxSpeed / MathF.Sqrt(speedSquared)), body: body);
    }
}
