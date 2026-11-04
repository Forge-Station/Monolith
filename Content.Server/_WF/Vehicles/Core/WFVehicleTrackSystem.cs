using System.Numerics;
using Content.Server.Decals;
using Content.Shared._WF.Vehicles.Core;
using Content.Shared._WF.Vehicles.Grid.Components;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Timing;
using PhysicsTransform = Robust.Shared.Physics.Transform;

namespace Content.Server._WF.Vehicles.Core;

/// <summary>Distance-spaced, cleanable tread marks with a bounded lifetime and global count.</summary>
public sealed class WFVehicleTrackSystem : EntitySystem
{
    [Dependency] private DecalSystem _decals = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    private readonly Dictionary<EntityUid, (EntityUid Grid, Vector2 Position)> _last = new();
    private readonly Queue<(EntityUid Grid, uint Id, TimeSpan Expires)> _marks = new();
    private float _elapsed;

    /// <summary>A decal sits on its bottom-left corner; this puts its middle on the point.</summary>
    private static readonly Vector2 DecalCentre = new(-0.5f, -0.5f);

    /// <summary>How far in from the hull's side a track runs.</summary>
    private const float TrackInset = 0.4f;

    public override void Initialize()
    {
        SubscribeLocalEvent<WFVehicleEngineComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnShutdown(Entity<WFVehicleEngineComponent> ent, ref ComponentShutdown args)
    {
        _last.Remove(ent.Owner);
    }

    public override void Update(float frameTime)
    {
        _elapsed += frameTime;
        if (_elapsed < 0.1f)
            return;
        _elapsed = 0f;

        while (_marks.TryPeek(out var mark) && (mark.Expires <= _timing.CurTime || _marks.Count > 2048))
        {
            _marks.Dequeue();
            if (!Deleted(mark.Grid))
                _decals.RemoveDecal(mark.Grid, mark.Id);
        }

        var query = EntityQueryEnumerator<WFVehicleEngineComponent, GridVehicleMoverComponent>();
        while (query.MoveNext(out var uid, out _, out var mover))
        {
            if (Transform(uid).GridUid is not { } grid)
            {
                _last.Remove(uid);
                continue;
            }
            if (!_last.TryGetValue(uid, out var last) || last.Grid != grid)
            {
                _last[uid] = (grid, mover.Position);
                continue;
            }
            var distance = Vector2.Distance(last.Position, mover.Position);
            if (distance < 0.6f)
                continue;
            _last[uid] = (grid, mover.Position);
            if (distance > 5f)
                continue;
            // One track under each side of the hull, across the way it went; the hull can sit off the vehicle's origin.
            var xform = Transform(uid);
            var rotation = xform.ParentUid == grid ? xform.LocalRotation : _transform.GetWorldRotation(uid) - _transform.GetWorldRotation(grid);
            var hull = HullBounds(uid);
            var across = Vector2.Normalize(new Vector2(-(mover.Position - last.Position).Y, (mover.Position - last.Position).X));
            var halfWidth = MathF.Abs(Vector2.Dot(across, rotation.RotateVec(Vector2.UnitX))) * hull.Width / 2f
                + MathF.Abs(Vector2.Dot(across, rotation.RotateVec(Vector2.UnitY))) * hull.Height / 2f;
            var middle = mover.Position + rotation.RotateVec(hull.Center);
            var side = across * MathF.Max(0.3f, halfWidth - TrackInset);
            foreach (var offset in new[] { side, -side })
            {
                var coordinates = new EntityCoordinates(grid, middle + offset + DecalCentre);
                if (_decals.TryAddDecal("WFVehicleTrack", coordinates, out var id,
                        Color.FromHex("#35302688"), mover.Heading, cleanable: true))
                    _marks.Enqueue((grid, id, _timing.CurTime + TimeSpan.FromMinutes(3)));
            }

            var treads = new WFVehicleTreadsEvent(uid, grid, middle + side, middle - side);
            RaiseLocalEvent(ref treads);
        }
    }

    /// <summary>The hull's hard fixtures in the vehicle's own frame, or a tile round its origin.</summary>
    private Box2 HullBounds(EntityUid uid)
    {
        if (!TryComp(uid, out FixturesComponent? fixtures))
            return Box2.CenteredAround(Vector2.Zero, Vector2.One);

        Box2? bounds = null;
        foreach (var fixture in fixtures.Fixtures.Values)
        {
            if (!fixture.Hard)
                continue;
            for (var i = 0; i < fixture.Shape.ChildCount; i++)
            {
                var box = fixture.Shape.ComputeAABB(PhysicsTransform.Empty, i);
                bounds = bounds?.Union(box) ?? box;
            }
        }

        return bounds ?? Box2.CenteredAround(Vector2.Zero, Vector2.One);
    }
}
