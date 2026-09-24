using System.Linq;
using System.Numerics;
using Content.Shared.Actions;
using Content.Shared.Coordinates;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.DoAfter;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Pulling.Systems;
using Content.Shared.Movement.Systems;
using Content.Shared.Physics;
using Content.Shared.Popups;
using Content.Shared.Standing;
using Content.Shared.Stealth;
using Content.Shared._White.Standing;
using Content.Shared.Stealth.Components;
using Content.Shared.Stunnable;
using Content.Shared.Throwing;
using Content.Shared.Weapons.Melee.Events;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Events;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Content.Shared.Coordinates.Helpers;

namespace Content.Shared._Forge.Xenomorphs;

public sealed class ForgeXenoAbilitySystem : EntitySystem
{
    [Dependency] private readonly DamageableSystem _damage = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly SharedGunSystem _gun = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedStealthSystem _stealth = default!;
    [Dependency] private readonly SharedStunSystem _stun = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SharedLayingDownSystem _laying = default!;
    [Dependency] private readonly StandingStateSystem _standing = default!;
    [Dependency] private readonly ThrowingSystem _throwing = default!;
    [Dependency] private readonly ForgeXenoPlasmaSystem _plasma = default!;
    [Dependency] private readonly MovementSpeedModifierSystem _movement = default!;
    [Dependency] private readonly PullingSystem _pulling = default!;
    [Dependency] private readonly SharedContentEyeSystem _eye = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly INetManager _net = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ForgeXenoRestActionEvent>(OnRest);
        SubscribeLocalEvent<ForgeXenoHideActionEvent>(OnHide);
        SubscribeLocalEvent<ForgeXenoFortifyActionEvent>(OnFortify);
        SubscribeLocalEvent<ForgeXenoZoomActionEvent>(OnZoom);
        SubscribeLocalEvent<ForgeXenoPheromonesActionEvent>(OnPheromones);
        SubscribeLocalEvent<ForgeXenoScreechActionEvent>(OnScreech);
        SubscribeLocalEvent<ForgeXenoStompActionEvent>(OnStomp);
        SubscribeLocalEvent<ForgeXenoTailStabActionEvent>(OnTailStab);
        SubscribeLocalEvent<ForgeXenoAcidActionEvent>(OnAcid);
        SubscribeLocalEvent<ForgeXenoPunchActionEvent>(OnPunch);
        SubscribeLocalEvent<ForgeXenoFlingActionEvent>(OnFling);
        SubscribeLocalEvent<ForgeXenoTransferPlasmaActionEvent>(OnTransfer);
        SubscribeLocalEvent<ForgeXenoGutActionEvent>(OnGut);
        SubscribeLocalEvent<ForgeXenoSpitActionEvent>(OnSpit);
        SubscribeLocalEvent<ForgeXenoLeapActionEvent>(OnLeap);
        SubscribeLocalEvent<ForgeXenoChargeComponent, PreventCollideEvent>(OnChargeCollide);
        SubscribeLocalEvent<ForgeXenoConstructActionEvent>(OnConstruct);
        SubscribeLocalEvent<ForgeXenoConstructDoAfterEvent>(OnConstructFinished);

        SubscribeLocalEvent<ForgeXenoFortifyComponent, DamageModifyEvent>(OnFortifyDamage);
        SubscribeLocalEvent<ForgeXenoComponent, MeleeAttackEvent>(OnMeleeAttack);
        SubscribeLocalEvent<ForgeXenoComponent, ShotAttemptedEvent>(OnShotAttempted);
    }

    private void OnRest(ForgeXenoRestActionEvent args)
    {
        BreakStealth(args.Performer);
        var uid = args.Performer;
        if (HasComp<ForgeXenoRestingComponent>(uid))
        {
            RemComp<ForgeXenoRestingComponent>(uid);
            _standing.Stand(uid);
            _popup.PopupClient(Loc.GetString("forge-xeno-rest-up"), uid, uid);
        }
        else if (_laying.TryLieDown(uid))
        {
            EnsureComp<ForgeXenoRestingComponent>(uid);
            _popup.PopupClient(Loc.GetString("forge-xeno-rest-down"), uid, uid);
        }

        args.Handled = true;
    }

    private void OnHide(ForgeXenoHideActionEvent args)
    {
        var uid = args.Performer;
        var stealth = EnsureComp<StealthComponent>(uid);
        var hidden = stealth.Enabled && _stealth.GetVisibility(uid, stealth) < 0f;
        if (hidden)
        {
            BreakStealth(uid);
        }
        else
        {
            _stealth.SetEnabled(uid, true, stealth);
            _stealth.SetVisibility(uid, -1.5f, stealth);
        }

        _popup.PopupClient(Loc.GetString(hidden ? "forge-xeno-hide-off" : "forge-xeno-hide-on"), uid, uid);
        args.Handled = true;
    }

    private void OnFortify(ForgeXenoFortifyActionEvent args)
    {
        BreakStealth(args.Performer);
        var uid = args.Performer;
        var fortify = EnsureComp<ForgeXenoFortifyComponent>(uid);
        fortify.Active = !fortify.Active;
        Dirty(uid, fortify);
        _movement.RefreshMovementSpeedModifiers(uid);
        _popup.PopupClient(Loc.GetString(fortify.Active ? "forge-xeno-fortify-on" : "forge-xeno-fortify-off"), uid, uid);
        args.Handled = true;
    }

    private void OnFortifyDamage(Entity<ForgeXenoFortifyComponent> ent, ref DamageModifyEvent args)
    {
        if (!ent.Comp.Active || !args.Damage.AnyPositive())
            return;

        args.Damage *= ent.Comp.DamageMultiplier;
    }

    private void OnZoom(ForgeXenoZoomActionEvent args)
    {
        BreakStealth(args.Performer);
        var uid = args.Performer;
        if (!TryComp<Content.Shared.Movement.Components.ContentEyeComponent>(uid, out var eye))
            return;

        var zoomed = eye.TargetZoom.X > 1.1f;
        _eye.SetZoom(uid, zoomed ? Vector2.One : new Vector2(1.6f, 1.6f), ignoreLimits: true, eye);
        args.Handled = true;
    }

    private void OnPheromones(ForgeXenoPheromonesActionEvent args)
    {
        BreakStealth(args.Performer);
        var uid = args.Performer;
        var phero = EnsureComp<ForgeXenoPheromonesComponent>(uid);
        phero.Active = !phero.Active;
        Dirty(uid, phero);
        _movement.RefreshMovementSpeedModifiers(uid);
        _popup.PopupClient(Loc.GetString(phero.Active ? "forge-xeno-pheromones-on" : "forge-xeno-pheromones-off"), uid, uid);
        args.Handled = true;
    }

    private void OnScreech(ForgeXenoScreechActionEvent args)
    {
        var uid = args.Performer;
        BreakStealth(uid);
        if (!_plasma.TryUse(uid, args.PlasmaCost))
        {
            Deny(uid);
            return;
        }

        _audio.PlayPredicted(new SoundPathSpecifier("/Audio/Effects/gib1.ogg"), uid, uid);
        foreach (var (ent, _) in GetNearbyMobs(uid, args.Range))
        {
            if (ent == uid || HasComp<ForgeXenoComponent>(ent))
                continue;

            _stun.TryParalyze(ent, TimeSpan.FromSeconds(args.StunSeconds), true);
        }

        args.Handled = true;
    }

    private void OnStomp(ForgeXenoStompActionEvent args)
    {
        var uid = args.Performer;
        BreakStealth(uid);
        if (!_plasma.TryUse(uid, args.PlasmaCost))
        {
            Deny(uid);
            return;
        }

        _audio.PlayPredicted(new SoundPathSpecifier("/Audio/Effects/gib2.ogg"), uid, uid);
        foreach (var (ent, _) in GetNearbyMobs(uid, args.Range))
        {
            if (ent == uid || HasComp<ForgeXenoComponent>(ent))
                continue;

            if (args.Damage != null)
                _damage.TryChangeDamage(ent, args.Damage, origin: uid);

            _stun.TryParalyze(ent, TimeSpan.FromSeconds(args.StunSeconds), true);
        }

        args.Handled = true;
    }

    private void OnTailStab(ForgeXenoTailStabActionEvent args)
    {
        BreakStealth(args.Performer);
        if (!TryMob(args.Target, args.Performer))
            return;

        if (!_plasma.TryUse(args.Performer, args.PlasmaCost))
        {
            Deny(args.Performer);
            return;
        }

        if (args.Damage != null)
            _damage.TryChangeDamage(args.Target, args.Damage, origin: args.Performer);

        _audio.PlayPredicted(new SoundPathSpecifier("/Audio/Weapons/Xeno/alien_claw_flesh2.ogg"), args.Target, args.Performer);
        args.Handled = true;
    }

    private void OnAcid(ForgeXenoAcidActionEvent args)
    {
        if (!TryShoot(args.Performer, args.Target, args.Projectile, args.Speed, args.PlasmaCost, "/Audio/Weapons/Xeno/alien_spitacid.ogg"))
            return;

        args.Handled = true;
    }

    private void OnPunch(ForgeXenoPunchActionEvent args)
    {
        BreakStealth(args.Performer);
        if (!TryMob(args.Target, args.Performer))
            return;

        if (!_plasma.TryUse(args.Performer, args.PlasmaCost))
        {
            Deny(args.Performer);
            return;
        }

        if (args.Damage != null)
            _damage.TryChangeDamage(args.Target, args.Damage, origin: args.Performer);

        _stun.TryParalyze(args.Target, TimeSpan.FromSeconds(args.StunSeconds), true);
        args.Handled = true;
    }

    private void OnFling(ForgeXenoFlingActionEvent args)
    {
        BreakStealth(args.Performer);
        if (!TryMob(args.Target, args.Performer))
            return;

        if (!_plasma.TryUse(args.Performer, args.PlasmaCost))
        {
            Deny(args.Performer);
            return;
        }

        var from = _transform.GetWorldPosition(args.Performer);
        var to = _transform.GetWorldPosition(args.Target);
        var dir = to - from;
        if (dir.LengthSquared() < 0.01f)
            dir = _transform.GetWorldRotation(args.Performer).ToWorldVec();

        var throwDir = dir.Normalized();
        _throwing.TryThrow(args.Target, throwDir * 3f, args.ThrowSpeed, args.Performer);
        if (TryComp<PhysicsComponent>(args.Target, out var body))
            _physics.SetLinearVelocity(args.Target, throwDir * args.ThrowSpeed, body: body);

        _stun.TryParalyze(args.Target, TimeSpan.FromSeconds(args.StunSeconds), true);
        args.Handled = true;
    }

    private void OnTransfer(ForgeXenoTransferPlasmaActionEvent args)
    {
        BreakStealth(args.Performer);
        if (!TryComp<ForgeXenoPlasmaComponent>(args.Target, out var targetPlasma))
            return;

        if (!TryComp<ForgeXenoPlasmaComponent>(args.Performer, out var source) || source.Plasma < args.Amount)
        {
            Deny(args.Performer);
            return;
        }

        _plasma.Add(args.Performer, -args.Amount);
        _plasma.Add(args.Target, args.Amount, targetPlasma);
        args.Handled = true;
    }

    private void OnGut(ForgeXenoGutActionEvent args)
    {
        BreakStealth(args.Performer);
        if (!TryMob(args.Target, args.Performer))
            return;

        if (!_plasma.TryUse(args.Performer, args.PlasmaCost))
        {
            Deny(args.Performer);
            return;
        }

        if (args.Damage != null)
            _damage.TryChangeDamage(args.Target, args.Damage, origin: args.Performer);

        _audio.PlayPredicted(new SoundPathSpecifier("/Audio/Effects/gib1.ogg"), args.Target, args.Performer);
        args.Handled = true;
    }

    private void OnSpit(ForgeXenoSpitActionEvent args)
    {
        if (!TryShoot(args.Performer, args.Target, args.Projectile, args.Speed, args.PlasmaCost, "/Audio/Weapons/Xeno/alien_spitacid.ogg"))
            return;

        args.Handled = true;
    }

    private void OnLeap(ForgeXenoLeapActionEvent args)
    {
        var uid = args.Performer;
        BreakStealth(uid);

        if (!TryComp<TransformComponent>(uid, out var xform))
            return;

        var origin = _transform.GetWorldPosition(xform);
        var aim = _transform.ToMapCoordinates(args.Target);
        if (xform.MapID != aim.MapId)
            return;

        var delta = aim.Position - origin;
        var length = delta.Length();
        if (length < 0.6f)
            return;

        if (!_plasma.TryUse(uid, args.PlasmaCost))
        {
            Deny(uid);
            return;
        }

        var dir = delta / length;
        if (args.Smash)
        {
            StartCharge(uid, dir, Math.Min(length, 8f), args);
            args.Handled = true;
            return;
        }

        var travel = Math.Min(length, 8f);
        var ray = new CollisionRay(origin, dir, (int) (CollisionGroup.Impassable | CollisionGroup.InteractImpassable));
        var hits = _physics.IntersectRay(xform.MapID, ray, travel, uid, false).ToList();

        Vector2 landing;
        if (hits.Count > 0)
            landing = hits.MinBy(hit => (hit.HitPos - origin).Length()).HitPos - dir * 0.6f;
        else
            landing = origin + dir * travel;

        _transform.SetWorldPosition(uid, landing);
        _audio.PlayPredicted(new SoundPathSpecifier("/Audio/Effects/gib2.ogg"), uid, uid);

        if (_net.IsServer)
            PinLeap(uid, new MapCoordinates(landing, xform.MapID), args);

        args.Handled = true;
    }

    private void StartCharge(EntityUid uid, Vector2 direction, float distance, ForgeXenoLeapActionEvent args)
    {
        var charge = EnsureComp<ForgeXenoChargeComponent>(uid);
        charge.Direction = direction;
        charge.Speed = 3f;
        charge.MaxSpeed = Math.Max(args.Speed, 8f);
        charge.Acceleration = 42f;
        charge.DistanceLeft = distance;
        charge.StunSeconds = args.StunSeconds;
        charge.HitDamage = args.HitDamage;
        charge.Hit.Clear();

        if (TryComp<PhysicsComponent>(uid, out var body))
        {
            _physics.SetBodyStatus(uid, body, BodyStatus.InAir);
            _physics.SetLinearVelocity(uid, direction * charge.Speed, body: body);
        }

        _transform.SetWorldRotation(uid, direction.ToWorldAngle());
        _audio.PlayPredicted(new SoundPathSpecifier("/Audio/Effects/gib2.ogg"), uid, uid);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<ForgeXenoChargeComponent, TransformComponent, PhysicsComponent>();
        var stopping = new List<(EntityUid, PhysicsComponent)>();
        while (query.MoveNext(out var uid, out var charge, out var xform, out var body))
        {
            if (charge.Direction == Vector2.Zero || charge.DistanceLeft <= 0f)
            {
                stopping.Add((uid, body));
                continue;
            }

            charge.Speed = MathF.Min(charge.MaxSpeed, charge.Speed + charge.Acceleration * frameTime);
            var step = charge.Speed * frameTime;
            var origin = _transform.GetWorldPosition(xform);
            var dir = charge.Direction;
            SmashAhead(uid, origin, dir, xform.MapID, charge);

            var stopAt = BarrierDistance(uid, xform.MapID, origin, dir, step + 0.45f);
            if (stopAt <= step + 0.05f)
            {
                var travel = MathF.Max(0f, stopAt - 0.4f);
                if (travel > 0.02f)
                    _transform.SetWorldPosition(uid, origin + dir * travel);

                stopping.Add((uid, body));
                continue;
            }

            _physics.SetBodyStatus(uid, body, BodyStatus.InAir);
            _physics.SetLinearVelocity(uid, dir * charge.Speed, body: body);
            charge.DistanceLeft -= step;
        }

        foreach (var (uid, body) in stopping)
            StopCharge(uid, body);
    }

    private void OnChargeCollide(Entity<ForgeXenoChargeComponent> ent, ref PreventCollideEvent args)
    {
        if (HasComp<ForgeXenoWeedsComponent>(args.OtherEntity) || HasComp<MobStateComponent>(args.OtherEntity))
            args.Cancelled = true;
    }

    private void SmashAhead(EntityUid uid, Vector2 origin, Vector2 dir, MapId mapId, ForgeXenoChargeComponent charge)
    {
        var nose = new MapCoordinates(origin + dir * 0.75f, mapId);
        foreach (var other in _lookup.GetEntitiesInRange(nose, 0.9f))
        {
            if (other == uid || !charge.Hit.Add(other))
                continue;

            if (HasComp<ForgeXenoWeedsComponent>(other))
            {
                if (_net.IsServer && charge.HitDamage != null)
                    _damage.TryChangeDamage(other, charge.HitDamage, origin: uid);
                continue;
            }

            if (!HasComp<MobStateComponent>(other))
                continue;

            if (!_net.IsServer)
                continue;

            if (charge.HitDamage != null)
                _damage.TryChangeDamage(other, charge.HitDamage, origin: uid);

            if (charge.StunSeconds > 0f)
                _stun.TryParalyze(other, TimeSpan.FromSeconds(charge.StunSeconds), true);

            _throwing.TryThrow(other, dir * 2.5f, 12f, uid, doSpin: false);
            if (TryComp<PhysicsComponent>(other, out var victim))
                _physics.SetLinearVelocity(other, dir * 12f, body: victim);
        }
    }

    private float BarrierDistance(EntityUid uid, MapId mapId, Vector2 origin, Vector2 dir, float maxDist)
    {
        var mask = (int) (CollisionGroup.Impassable | CollisionGroup.HighImpassable | CollisionGroup.MidImpassable | CollisionGroup.InteractImpassable);
        var ray = new CollisionRay(origin, dir, mask);
        var best = maxDist;

        foreach (var hit in _physics.IntersectRay(mapId, ray, maxDist, uid, false))
        {
            if (HasComp<ForgeXenoWeedsComponent>(hit.HitEntity) || HasComp<MobStateComponent>(hit.HitEntity))
                continue;

            if (hit.Distance < best)
                best = hit.Distance;
        }

        return best;
    }

    private void StopCharge(EntityUid uid, PhysicsComponent body)
    {
        _physics.SetLinearVelocity(uid, Vector2.Zero, body: body);
        _physics.SetBodyStatus(uid, body, BodyStatus.OnGround);
        RemComp<ForgeXenoChargeComponent>(uid);
    }

    private void PinLeap(EntityUid uid, MapCoordinates landing, ForgeXenoLeapActionEvent args)
    {
        EntityUid? best = null;
        var bestDist = 1.25f;
        foreach (var other in _lookup.GetEntitiesInRange(landing, 1.25f))
        {
            if (!TryMob(other, uid))
                continue;

            var dist = (_transform.GetWorldPosition(other) - landing.Position).Length();
            if (dist >= bestDist)
                continue;

            bestDist = dist;
            best = other;
        }

        if (best == null)
            return;

        if (args.HitDamage != null)
            _damage.TryChangeDamage(best.Value, args.HitDamage, origin: uid);

        if (args.StunSeconds > 0f)
            _stun.TryParalyze(best.Value, TimeSpan.FromSeconds(args.StunSeconds), true);

        if (args.PullOnHit)
            _pulling.TryStartPull(uid, best.Value);
    }

    private void OnMeleeAttack(Entity<ForgeXenoComponent> ent, ref MeleeAttackEvent args)
    {
        BreakStealth(ent);
    }

    private void OnShotAttempted(Entity<ForgeXenoComponent> ent, ref ShotAttemptedEvent args)
    {
        BreakStealth(ent);
    }

    private void OnConstruct(ForgeXenoConstructActionEvent args)
    {
        BreakStealth(args.Performer);
        if (!_timing.IsFirstTimePredicted)
            return;

        if (!TryComp<ForgeXenoPlasmaComponent>(args.Performer, out var plasma) || plasma.Plasma < args.PlasmaCost)
        {
            Deny(args.Performer);
            return;
        }

        var doAfterArgs = new DoAfterArgs(EntityManager, args.Performer, args.Delay, new ForgeXenoConstructDoAfterEvent
        {
            Prototype = args.Prototype,
            PlasmaCost = args.PlasmaCost,
            Target = GetNetCoordinates(args.Target),
        }, args.Performer)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = false,
        };

        if (_doAfter.TryStartDoAfter(doAfterArgs))
            args.Handled = true;
    }

    private void OnConstructFinished(ForgeXenoConstructDoAfterEvent args)
    {
        if (args.Cancelled)
            return;

        if (!_plasma.TryUse(args.User, args.PlasmaCost))
            return;

        var coords = GetCoordinates(args.Target).SnapToGrid(EntityManager);
        Spawn(args.Prototype, coords);
        args.Handled = true;
    }

    private bool TryShoot(EntityUid performer, EntityCoordinates target, EntProtoId projectile, float speed, float plasmaCost, string sound)
    {
        BreakStealth(performer);

        var origin = _transform.GetMapCoordinates(performer);
        var aim = _transform.ToMapCoordinates(target);
        if (origin.MapId != aim.MapId)
            return false;

        var direction = aim.Position - origin.Position;
        if (direction.LengthSquared() < 0.01f)
            return false;

        if (!_plasma.TryUse(performer, plasmaCost))
        {
            Deny(performer);
            return false;
        }

        if (_net.IsServer)
        {
            var ent = Spawn(projectile, origin);
            _gun.ShootProjectile(ent, direction, Vector2.Zero, performer, performer, speed);
        }

        _audio.PlayPredicted(new SoundPathSpecifier(sound), performer, performer);
        return true;
    }

    private void BreakStealth(EntityUid uid)
    {
        if (!TryComp<StealthComponent>(uid, out var stealth) || !stealth.Enabled)
            return;

        _stealth.SetEnabled(uid, false, stealth);
        _stealth.SetVisibility(uid, 1f, stealth);
    }

    private bool TryMob(EntityUid target, EntityUid user)
    {
        if (target == user || HasComp<ForgeXenoComponent>(target))
            return false;

        return HasComp<MobStateComponent>(target) && !_mobState.IsDead(target);
    }

    private void Deny(EntityUid uid)
    {
        _popup.PopupClient(Loc.GetString("forge-xeno-no-plasma"), uid, uid);
    }

    private IEnumerable<(EntityUid, MobStateComponent)> GetNearbyMobs(EntityUid uid, float range)
    {
        var coords = Transform(uid).Coordinates;
        var query = EntityQueryEnumerator<MobStateComponent, TransformComponent>();
        while (query.MoveNext(out var ent, out var mob, out var xform))
        {
            if (!xform.Coordinates.TryDistance(EntityManager, coords, out var dist) || dist > range)
                continue;

            yield return (ent, mob);
        }
    }
}
