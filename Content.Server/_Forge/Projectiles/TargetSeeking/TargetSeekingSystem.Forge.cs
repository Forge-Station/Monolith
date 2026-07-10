using System.Numerics;
using Robust.Shared.Map.Components;

namespace Content.Server._Mono.Projectiles.TargetSeeking;

public sealed partial class TargetSeekingSystem
{
    private Vector2 GetTargetWorldPosition(EntityUid target, TransformComponent xform)
    {
        if (TryComp<MapGridComponent>(target, out var grid))
            return Vector2.Transform(grid.LocalAABB.Center, _transform.GetWorldMatrix(xform));

        return _transform.GetWorldPosition(xform);
    }
}
