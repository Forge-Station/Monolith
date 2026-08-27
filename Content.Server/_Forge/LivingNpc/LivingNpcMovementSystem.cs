using System.Numerics;
using Content.Server.NPC.Components;
using Content.Server.NPC.Systems;
using Content.Shared._Forge.LivingNpc.Components;
using Robust.Shared.Map;

namespace Content.Server._Forge.LivingNpc;

public sealed partial class LivingNpcMovementSystem : EntitySystem
{
    [Dependency] private NPCSteeringSystem _steering = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    public void MoveTo(EntityUid uid, EntityCoordinates destination, float range = 0.85f)
    {
        var steering = _steering.Register(uid, destination);
        steering.Range = range;
    }

    public void MoveToEntity(EntityUid uid, EntityUid target, float range = 1.2f)
    {
        MoveTo(uid, new EntityCoordinates(target, Vector2.Zero), range);
    }

    public void Stop(EntityUid uid)
    {
        if (HasComp<NPCSteeringComponent>(uid))
            _steering.Unregister(uid);
    }

    public bool Arrived(EntityUid uid)
    {
        return TryComp<NPCSteeringComponent>(uid, out var steering) && steering.Status == SteeringStatus.InRange;
    }

    public bool NoPath(EntityUid uid)
    {
        return TryComp<NPCSteeringComponent>(uid, out var steering) && steering.Status == SteeringStatus.NoPath;
    }

    public bool InRange(EntityUid uid, EntityUid target, float range)
    {
        var a = _transform.GetWorldPosition(uid);
        var b = _transform.GetWorldPosition(target);
        return (a - b).LengthSquared() <= range * range;
    }

    public float Distance(EntityUid uid, EntityUid target)
    {
        return (_transform.GetWorldPosition(uid) - _transform.GetWorldPosition(target)).Length();
    }
}
