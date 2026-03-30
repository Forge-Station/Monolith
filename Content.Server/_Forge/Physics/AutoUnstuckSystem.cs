using System.Numerics;
using Content.Server.Shuttles.Components;
using Content.Shared._Forge;
using Robust.Shared.Configuration;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Random;

namespace Content.Server._Forge.Physics;

/// <summary>
///     After prolonged hard contact with static colliders, applies a short collision-off nudge so
///     mobs and items can escape wall clips and similar stuck states.
/// </summary>
public sealed class AutoUnstuckSystem : EntitySystem
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly SharedTransformSystem _xform = default!;

    private readonly Dictionary<EntityUid, float> _stuckSeconds = new();
    private readonly List<EntityUid> _clearTimer = new();
    private readonly List<EntityUid> _purgeStale = new();
    private readonly List<Entity<PhysicsComponent, TransformComponent>> _awakeSnapshot = new();

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!_cfg.GetCVar(ForgeVars.AutoUnstuckEnabled))
        {
            _stuckSeconds.Clear();
            return;
        }

        _purgeStale.Clear();
        foreach (var uid in _stuckSeconds.Keys)
        {
            if (TerminatingOrDeleted(uid))
                _purgeStale.Add(uid);
        }

        foreach (var uid in _purgeStale)
            _stuckSeconds.Remove(uid);

        var afterSeconds = _cfg.GetCVar(ForgeVars.AutoUnstuckAfterSeconds);
        var nudge = _cfg.GetCVar(ForgeVars.AutoUnstuckNudge);
        if (afterSeconds <= 0f || nudge <= 0f)
            return;

        _clearTimer.Clear();
        _awakeSnapshot.Clear();
        _awakeSnapshot.EnsureCapacity(_physics.AwakeBodies.Count);
        foreach (var awake in _physics.AwakeBodies)
        {
            _awakeSnapshot.Add(awake);
        }

        foreach (var awake in _awakeSnapshot)
        {
            var uid = awake.Owner;
            var body = awake.Comp1;

            if (body.BodyType == BodyType.Static || !body.CanCollide)
                continue;

            if (HasComp<MapGridComponent>(uid) || HasComp<MapComponent>(uid) || HasComp<ShuttleComponent>(uid))
                continue;

            if (IsPaused(uid))
                continue;

            var contacts = _physics.GetContacts(uid);
            var hasStaticHard = false;
            var awaySum = Vector2.Zero;

            while (contacts.MoveNext(out var contact))
            {
                if (!contact.IsTouching || !contact.Hard)
                    continue;

                var other = contact.OtherEnt(uid);
                var otherBody = contact.OtherBody(uid);
                if (otherBody.BodyType != BodyType.Static)
                    continue;

                hasStaticHard = true;

                var selfPos = _physics.GetPhysicsTransform(uid).Position;
                var otherPos = _physics.GetPhysicsTransform(other).Position;
                var delta = selfPos - otherPos;
                if (delta != Vector2.Zero)
                    awaySum += Vector2.Normalize(delta);
            }

            if (!hasStaticHard)
            {
                _clearTimer.Add(uid);
                continue;
            }

            var elapsed = _stuckSeconds.GetValueOrDefault(uid) + frameTime;
            _stuckSeconds[uid] = elapsed;

            if (elapsed < afterSeconds)
                continue;

            var xform = awake.Comp2;
            Vector2 offset;
            if (awaySum.LengthSquared() > 1e-4f)
                offset = Vector2.Normalize(awaySum) * nudge;
            else
                offset = _random.NextAngle().ToVec() * nudge;

            _physics.SetCanCollide(uid, false, body: body);
            _xform.SetCoordinates(uid, xform, xform.Coordinates.Offset(offset));
            _physics.SetCanCollide(uid, true, body: body);
            _physics.SetLinearVelocity(uid, Vector2.Zero, body: body);
            _physics.WakeBody(uid, body: body);

            _clearTimer.Add(uid);
        }

        foreach (var uid in _clearTimer)
            _stuckSeconds.Remove(uid);
    }
}
