using System.Numerics;
using Content.Server.Physics.Controllers;
using Content.Shared._WF.Tether;
using Robust.Server.GameStates;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Controllers;
using Robust.Shared.Physics.Dynamics.Joints;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WF.Tether;

/// <summary>
/// Ropes between two attach points. The engine's DistanceJoint supplies the inextensible hard
/// limit only (stiffness 0, so it acts purely as an upper bound); the one-sided spring-damper that
/// makes a rope feel like a rope is applied here as impulses at the anchor points.
/// </summary>
public sealed partial class RopeSystem : VirtualController
{
    [Dependency] private SharedJointSystem _joints = default!;
    [Dependency] private PvsOverrideSystem _pvs = default!;
    [Dependency] private IPrototypeManager _protos = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private IGameTiming _timing = default!;

    /// <summary>Seconds above the break force before a rope snaps. Twice the force snaps instantly.</summary>
    private const float OverloadDuration = 0.25f;

    /// <summary>Strain is networked in these steps so a moving rope does not send a state every tick.</summary>
    private const float StrainQuantum = 0.02f;

    /// <summary>Fallback end positions are re-sent after this much movement, at most this often.</summary>
    private const float WorldSyncDistance = 0.1f;
    private static readonly TimeSpan WorldSyncInterval = TimeSpan.FromSeconds(0.1);

    private readonly List<(EntityUid Rope, bool Refund)> _pendingBreaks = new();

    /// <summary>Guards the bookkeeping while an attach point is tearing down its own ropes.</summary>
    private readonly HashSet<EntityUid> _detaching = new();

    public override void Initialize()
    {
        // Read the tick's thruster forces before the engine integrates them, as the tractor beam does.
        UpdatesAfter.Add(typeof(MoverController));
        base.Initialize();

        SubscribeLocalEvent<RopeComponent, ComponentShutdown>(OnRopeShutdown);
        SubscribeLocalEvent<RopeAttachPointComponent, ComponentShutdown>(OnAttachPointShutdown);
        InitializeInteraction();
    }

    #region Public API

    /// <summary>
    /// Ties a rope between two attach points. Fails when either point is full, the points share an
    /// entity, they are on different maps, or the rope type is unknown.
    /// </summary>
    public bool TryCreateRope(EntityUid a, EntityUid b, ProtoId<RopeTypePrototype> type, float length, out EntityUid? rope)
    {
        rope = null;
        if (a == b || TerminatingOrDeleted(a) || TerminatingOrDeleted(b) ||
            !TryComp<RopeAttachPointComponent>(a, out var pointA) ||
            !TryComp<RopeAttachPointComponent>(b, out var pointB) ||
            !_protos.TryIndex(type, out var proto) ||
            pointA.Ropes.Count >= pointA.MaxRopes || pointB.Ropes.Count >= pointB.MaxRopes ||
            !float.IsFinite(length))
            return false;

        var xformA = Transform(a);
        if (xformA.MapID == MapId.Nullspace || xformA.MapID != Transform(b).MapID)
            return false;

        length = Math.Clamp(length, RopeMath.MinLength, MathF.Max(RopeMath.MinLength, proto.MaxLength));
        var uid = Spawn(null, xformA.Coordinates);
        var comp = EnsureComp<RopeComponent>(uid);
        comp.EndA = GetNetEntity(a);
        comp.EndB = GetNetEntity(b);
        comp.RopeType = type;
        comp.Length = length;
        Dirty(uid, comp);
        // Both hulls must see the rope regardless of which one the viewer is standing on.
        _pvs.AddGlobalOverride(uid);

        var net = GetNetEntity(uid);
        pointA.Ropes.Add(net);
        Dirty(a, pointA);
        pointB.Ropes.Add(net);
        Dirty(b, pointB);
        RefreshJoint(uid, comp, proto);

        var attachedA = new RopeAttachedEvent(uid, b, type);
        RaiseLocalEvent(a, ref attachedA);
        var attachedB = new RopeAttachedEvent(uid, a, type);
        RaiseLocalEvent(b, ref attachedB);
        rope = uid;
        return true;
    }

    /// <summary>Changes the rest length, clamping to [0.5 m, the type's max length]. Used for reeling.</summary>
    public bool SetLength(EntityUid rope, float length)
    {
        if (!TryComp<RopeComponent>(rope, out var comp) || !float.IsFinite(length) ||
            !_protos.TryIndex(comp.RopeType, out var proto))
            return false;

        var clamped = Math.Clamp(length, RopeMath.MinLength, MathF.Max(RopeMath.MinLength, proto.MaxLength));
        if (MathHelper.CloseTo(clamped, comp.Length))
            return false;

        comp.Length = clamped;
        Dirty(rope, comp);
        if (TryGetJoint(comp, out var joint))
        {
            joint.MinLength = 0f;
            joint.MaxLength = clamped * (1f + proto.MaxStretch);
            joint.Length = clamped;
        }

        return true;
    }

    /// <summary>
    /// Removes a rope. <paramref name="refund"/> gives the rope back as a coil (untying); otherwise
    /// the rope snaps with its break sound and nothing is recovered.
    /// </summary>
    public void BreakRope(EntityUid rope, bool refund = false, EntityUid? user = null)
    {
        if (!TryComp<RopeComponent>(rope, out var comp) || TerminatingOrDeleted(rope))
            return;

        TryGetEntity(comp.EndA, out var endA);
        TryGetEntity(comp.EndB, out var endB);
        var proto = _protos.TryIndex(comp.RopeType, out var indexed) ? indexed : null;
        var origin = endA ?? endB ?? rope;

        if (refund && comp.Refundable && proto != null)
            RefundCoil(comp, proto, origin, user);
        else if (proto != null && !comp.Carried)
            _audio.PlayPvs(proto.BreakSound, origin);

        var ev = new RopeBrokenEvent(rope, endA ?? EntityUid.Invalid, endB ?? EntityUid.Invalid, comp.RopeType, refund);
        if (endA is { } a && !TerminatingOrDeleted(a))
            RaiseLocalEvent(a, ref ev);
        if (endB is { } b && !TerminatingOrDeleted(b))
            RaiseLocalEvent(b, ref ev);
        RaiseLocalEvent(ref ev);

        Del(rope);
    }

    /// <summary>Last computed tension in newtons. Zero while the rope is slack.</summary>
    public float GetTension(EntityUid rope)
    {
        return TryComp<RopeComponent>(rope, out var comp) && float.IsFinite(comp.Tension) ? comp.Tension : 0f;
    }

    /// <summary>World position the rope leaves an attach point at, including its local offset.</summary>
    public Vector2 GetAnchorPosition(EntityUid point)
    {
        var position = TransformSystem.GetWorldPosition(point);
        if (TryComp<RopeAttachPointComponent>(point, out var attach) && attach.LocalOffset != Vector2.Zero)
            position += TransformSystem.GetWorldRotation(point).RotateVec(attach.LocalOffset);

        return position;
    }

    #endregion

    #region Lifecycle

    private void OnRopeShutdown(Entity<RopeComponent> ent, ref ComponentShutdown args)
    {
        RemoveRopeJoint(ent.Comp);
        _pvs.RemoveGlobalOverride(ent.Owner);
        TryGetEntity(ent.Comp.EndA, out var endA);
        TryGetEntity(ent.Comp.EndB, out var endB);
        Detach(endA, ent.Owner, endB, ent.Comp.RopeType);
        Detach(endB, ent.Owner, endA, ent.Comp.RopeType);
        CancelCarryFor(ent.Owner);
    }

    /// <summary>A deleted attach point severs everything tied to it; joints cannot outlive a body.</summary>
    private void OnAttachPointShutdown(Entity<RopeAttachPointComponent> ent, ref ComponentShutdown args)
    {
        if (ent.Comp.Ropes.Count == 0 || !_detaching.Add(ent.Owner))
            return;

        foreach (var net in ent.Comp.Ropes.ToArray())
        {
            if (TryGetEntity(net, out var rope) && !TerminatingOrDeleted(rope.Value))
                BreakRope(rope.Value);
        }

        ent.Comp.Ropes.Clear();
        _detaching.Remove(ent.Owner);
    }

    private void Detach(EntityUid? point, EntityUid rope, EntityUid? other, ProtoId<RopeTypePrototype> type)
    {
        if (point is not { } uid || !TryComp<RopeAttachPointComponent>(uid, out var attach))
            return;

        if (!attach.Ropes.Remove(GetNetEntity(rope)))
            return;

        if (!TerminatingOrDeleted(uid))
        {
            Dirty(uid, attach);
            var ev = new RopeDetachedEvent(rope, other ?? EntityUid.Invalid, type);
            RaiseLocalEvent(uid, ref ev);
        }
    }

    #endregion

    #region Joints

    private bool TryGetJoint(RopeComponent rope, out DistanceJoint joint)
    {
        joint = default!;
        if (rope.JointId is not { } id || rope.BodyA is not { } body || TerminatingOrDeleted(body) ||
            !TryComp<JointComponent>(body, out var joints) ||
            !joints.GetJoints.TryGetValue(id, out var found) || found is not DistanceJoint distance)
            return false;

        joint = distance;
        return true;
    }

    private void RemoveRopeJoint(RopeComponent rope)
    {
        var id = rope.JointId;
        var body = rope.BodyA;
        rope.JointId = null;
        rope.BodyA = null;
        rope.BodyB = null;
        if (id == null || body is not { } uid || TerminatingOrDeleted(uid) || !HasComp<JointComponent>(uid))
            return;

        _joints.RemoveJoint(uid, id);
    }

    /// <summary>
    /// Creates or re-creates the hard-limit joint. A carried rope, a non load bearing cord, ends
    /// sharing one body, and ends on different maps all correctly end up with no joint at all.
    /// </summary>
    private void RefreshJoint(EntityUid uid, RopeComponent rope, RopeTypePrototype proto)
    {
        RemoveRopeJoint(rope);
        if (rope.Carried || !proto.LoadBearing ||
            !TryGetEntity(rope.EndA, out var endA) || !TryGetEntity(rope.EndB, out var endB) ||
            !TryGetBody(endA.Value, out var bodyA, out var anchorA) ||
            !TryGetBody(endB.Value, out var bodyB, out var anchorB) ||
            bodyA == bodyB || Transform(bodyA).MapID != Transform(bodyB).MapID)
            return;

        var id = $"wf-rope-{GetNetEntity(uid)}";
        var joint = _joints.CreateDistanceJoint(bodyA, bodyB, anchorA, anchorB, id: id);
        // Stiffness 0 with distinct limits skips the engine's two-sided soft spring entirely and
        // leaves only the upper bound, which is exactly the inextensible part of a rope.
        joint.MinLength = 0f;
        joint.MaxLength = rope.Length * (1f + proto.MaxStretch);
        joint.Length = rope.Length;
        joint.Stiffness = 0f;
        joint.Damping = 0f;
        rope.JointId = id;
        rope.BodyA = bodyA;
        rope.BodyB = bodyB;
    }

    /// <summary>
    /// The body a rope pulls on: the grid while the point is anchored or has no dynamic body of
    /// its own, otherwise the point itself so loose crates can be towed.
    /// </summary>
    public bool TryGetBody(EntityUid point, out EntityUid body, out Vector2 localAnchor)
    {
        body = default;
        localAnchor = default;
        if (TerminatingOrDeleted(point))
            return false;

        var xform = Transform(point);
        if (!xform.Anchored && TryComp<PhysicsComponent>(point, out var own) && own.BodyType == BodyType.Dynamic)
            body = point;
        else if (HasComp<MapGridComponent>(point) && HasComp<PhysicsComponent>(point))
            body = point;
        else if (xform.GridUid is { } grid && HasComp<PhysicsComponent>(grid))
            body = grid;
        else
            return false;

        if (!Matrix3x2.Invert(TransformSystem.GetWorldMatrix(body), out var inverse))
            return false;

        localAnchor = Vector2.Transform(GetAnchorPosition(point), inverse);
        return float.IsFinite(localAnchor.X) && float.IsFinite(localAnchor.Y);
    }

    #endregion

    #region Physics

    public override void UpdateBeforeSolve(bool prediction, float frameTime)
    {
        if (prediction || frameTime <= 0f || !float.IsFinite(frameTime))
            return;

        _pendingBreaks.Clear();
        var query = EntityQueryEnumerator<RopeComponent>();
        while (query.MoveNext(out var uid, out var rope))
        {
            if (Paused(uid))
                continue;

            if (!_protos.TryIndex(rope.RopeType, out var proto) ||
                !TryGetEntity(rope.EndA, out var endA) || !TryGetEntity(rope.EndB, out var endB) ||
                TerminatingOrDeleted(endA.Value) || TerminatingOrDeleted(endB.Value))
            {
                _pendingBreaks.Add((uid, false));
                continue;
            }

            var map = Transform(endA.Value).MapID;
            // Joints cannot span maps, so an FTL jump by one end is a severing event.
            if (map == MapId.Nullspace || map != Transform(endB.Value).MapID)
            {
                _pendingBreaks.Add((uid, false));
                continue;
            }

            if (!UpdateRope(uid, rope, proto, endA.Value, endB.Value, frameTime))
                _pendingBreaks.Add((uid, false));
        }

        foreach (var (rope, refund) in _pendingBreaks)
        {
            BreakRope(rope, refund);
        }

        _pendingBreaks.Clear();
        UpdateCarriers(frameTime);
    }

    /// <summary>
    /// Networks coarse end positions for clients that have one end outside PVS range. Throttled,
    /// and skipped while nothing moved, so a parked rope costs no bandwidth.
    /// </summary>
    private void SyncWorldEnds(EntityUid uid, RopeComponent rope, Vector2 anchorA, Vector2 anchorB)
    {
        if (_timing.CurTime < rope.NextWorldSync ||
            (Vector2.DistanceSquared(rope.WorldA, anchorA) < WorldSyncDistance * WorldSyncDistance &&
             Vector2.DistanceSquared(rope.WorldB, anchorB) < WorldSyncDistance * WorldSyncDistance))
            return;

        rope.WorldA = anchorA;
        rope.WorldB = anchorB;
        rope.NextWorldSync = _timing.CurTime + WorldSyncInterval;
        Dirty(uid, rope);
    }

    /// <summary>Returns false when the rope must snap this tick.</summary>
    private bool UpdateRope(
        EntityUid uid,
        RopeComponent rope,
        RopeTypePrototype proto,
        EntityUid endA,
        EntityUid endB,
        float frameTime)
    {
        var anchorA = GetAnchorPosition(endA);
        var anchorB = GetAnchorPosition(endB);
        var delta = anchorB - anchorA;
        var distance = delta.Length();
        if (!float.IsFinite(distance))
            return false;

        SyncWorldEnds(uid, rope, anchorA, anchorB);
        var strain = RopeMath.Strain(distance, rope.Length, proto.MaxStretch);
        var quantized = MathF.Round(strain / StrainQuantum) * StrainQuantum;
        if (!MathHelper.CloseTo(rope.Strain, quantized, 0.001f))
        {
            rope.Strain = quantized;
            Dirty(uid, rope);
        }

        // A carried loose end and a visual-only rope never pull. Neither does a power cord, which
        // simply parts once it is stretched past its limit.
        if (rope.Carried)
            return true;

        if (!proto.LoadBearing)
        {
            RemoveRopeJoint(rope);
            rope.Tension = 0f;
            return distance <= rope.Length * (1f + proto.MaxStretch);
        }

        var hasA = TryGetBody(endA, out var bodyA, out _);
        var hasB = TryGetBody(endB, out var bodyB, out _);
        var joinable = hasA && hasB && bodyA != bodyB && Transform(bodyA).MapID == Transform(bodyB).MapID;
        if (!joinable)
        {
            // Both ends on one hull, or a body we cannot resolve: visual only, never a broken rope.
            RemoveRopeJoint(rope);
            rope.Tension = 0f;
            rope.OverloadTime = 0f;
            return true;
        }

        if (rope.BodyA != bodyA || rope.BodyB != bodyB || !TryGetJoint(rope, out _))
            RefreshJoint(uid, rope, proto);

        var extension = RopeMath.Extension(distance, rope.Length);
        var reaction = TryGetJoint(rope, out var joint) ? joint.GetReactionForce(1f / frameTime).Length() : 0f;
        if (!float.IsFinite(reaction))
            reaction = 0f;

        var spring = 0f;
        if (extension > 0f && distance > 0.0001f)
        {
            var direction = delta / distance;
            spring = ApplySpring(bodyA, bodyB, anchorA, anchorB, direction, extension, rope, proto, frameTime);
            // A rope only does its job while both ends stay awake; sleeping grids drift apart.
            PhysicsSystem.WakeBody(bodyA);
            PhysicsSystem.WakeBody(bodyB);
        }

        rope.Tension = spring + reaction;
        if (proto.BreakForce <= 0f || !float.IsFinite(proto.BreakForce))
        {
            rope.OverloadTime = 0f;
            return true;
        }

        if (rope.Tension > proto.BreakForce * 2f)
            return false;

        if (rope.Tension > proto.BreakForce)
        {
            rope.OverloadTime += frameTime;
            return rope.OverloadTime < OverloadDuration;
        }

        rope.OverloadTime = 0f;
        return true;
    }

    /// <summary>Applies the one-sided spring-damper at both anchors. Returns the force in newtons.</summary>
    private float ApplySpring(
        EntityUid bodyA,
        EntityUid bodyB,
        Vector2 anchorA,
        Vector2 anchorB,
        Vector2 direction,
        float extension,
        RopeComponent rope,
        RopeTypePrototype proto,
        float frameTime)
    {
        if (!TryComp<PhysicsComponent>(bodyA, out var physicsA) || !TryComp<PhysicsComponent>(bodyB, out var physicsB))
            return 0f;

        var armA = anchorA - WorldCenter(bodyA, physicsA);
        var armB = anchorB - WorldCenter(bodyB, physicsB);
        var velocityA = AnchorVelocity(bodyA, physicsA, armA);
        var velocityB = AnchorVelocity(bodyB, physicsB, armB);
        var separating = Vector2.Dot(velocityB - velocityA, direction);
        var effective = RopeMath.EffectiveMass(direction, armA, armB,
            physicsA.InvMass, physicsB.InvMass, physicsA.InvI, physicsB.InvI);
        var impulse = RopeMath.SpringImpulse(extension, rope.Length, separating, effective,
            proto.Stiffness, proto.DampingRatio, proto.MaxStretch, frameTime);
        if (impulse <= 0f || !float.IsFinite(impulse))
            return 0f;

        // Equal and opposite along the rope, plus the torque each anchor arm generates.
        var world = direction * impulse;
        ApplyAtAnchor(bodyA, physicsA, world, armA);
        ApplyAtAnchor(bodyB, physicsB, -world, armB);
        return impulse / frameTime;
    }

    private void ApplyAtAnchor(EntityUid uid, PhysicsComponent body, Vector2 worldImpulse, Vector2 arm)
    {
        if (body.BodyType != BodyType.Dynamic)
            return;

        // ApplyLinearImpulse works in the body's parent frame; the cross product is frame invariant.
        var parent = Transform(uid).ParentUid;
        var local = parent.IsValid()
            ? (-TransformSystem.GetWorldRotation(parent)).RotateVec(worldImpulse)
            : worldImpulse;
        PhysicsSystem.ApplyLinearImpulse(uid, local, body: body);
        PhysicsSystem.ApplyAngularImpulse(uid, arm.X * worldImpulse.Y - arm.Y * worldImpulse.X, body: body);
    }

    private Vector2 WorldCenter(EntityUid uid, PhysicsComponent body)
    {
        return TransformSystem.ToMapCoordinates(new EntityCoordinates(uid, body.LocalCenter)).Position;
    }

    private Vector2 AnchorVelocity(EntityUid uid, PhysicsComponent body, Vector2 arm)
    {
        var linear = PhysicsSystem.GetMapLinearVelocity(uid, body);
        var angular = PhysicsSystem.GetMapAngularVelocity(uid, body);
        return linear + new Vector2(-angular * arm.Y, angular * arm.X);
    }

    #endregion
}
