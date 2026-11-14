using System.Numerics;
using Robust.Shared.Map;

namespace Content.Server._WF.Leviathan;

/// <summary>
/// Drags each tail along behind its head like a chain: every segment keeps its spacing from the one ahead, so the
/// body follows the path the head took. Server-side only; the segments are visuals and collide with nothing.
/// </summary>
public sealed class WFTailedEntitySystem : EntitySystem
{
    [Dependency] private SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFTailedEntityComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnShutdown(Entity<WFTailedEntityComponent> ent, ref ComponentShutdown args)
    {
        foreach (var segment in ent.Comp.Segments)
            QueueDel(segment);
        ent.Comp.Segments.Clear();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<WFTailedEntityComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var tail, out var xform))
        {
            if (xform.MapUid is not { } map)
                continue;

            var (head, headRotation) = _transform.GetWorldPositionRotation(xform);
            if (tail.Segments.Count == 0 || !Exists(tail.Segments[0]) || Transform(tail.Segments[0]).MapUid != map)
            {
                Respawn(tail, map, head, headRotation);
                continue;
            }

            var ahead = head;
            foreach (var segment in tail.Segments)
            {
                if (!Exists(segment))
                    continue;

                var position = _transform.GetWorldPosition(segment);
                var back = position - ahead;
                // Keep the spacing from the segment ahead; a segment right on top of it trails straight back.
                var direction = back.LengthSquared() > 0.0001f ? Vector2.Normalize(back) : -headRotation.ToWorldVec();
                position = ahead + direction * tail.Spacing;
                _transform.SetWorldPositionRotation(segment, position, (-direction).ToWorldAngle());
                ahead = position;
            }
        }
    }

    /// <summary>Lays the body out straight behind the head: when it first appears, or after it changed map.</summary>
    private void Respawn(WFTailedEntityComponent tail, EntityUid map, Vector2 head, Angle rotation)
    {
        foreach (var segment in tail.Segments)
            QueueDel(segment);
        tail.Segments.Clear();

        var back = -rotation.ToWorldVec();
        for (var i = 1; i <= tail.Amount; i++)
        {
            var segment = Spawn(tail.Prototype, new EntityCoordinates(map, head + back * tail.Spacing * i));
            _transform.SetWorldRotation(segment, rotation);
            tail.Segments.Add(segment);
        }
    }
}
