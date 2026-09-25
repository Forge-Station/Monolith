using Content.Server.Emp;
using Content.Shared._Forge.Leviathans;
using Content.Shared._Forge.Leviathans.Components;
using Content.Shared.Damage;
using Content.Shared.Flash;
using Content.Shared.Ghost;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Stunnable;
using Content.Shared.Throwing;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Events;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Forge.Leviathans;

public sealed partial class VoidDrakeSystem : EntitySystem
{
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private EmpSystem _emp = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private LeviathanHuntSystem _hunt = default!;
    [Dependency] private LeviathanSmashSystem _smash = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private MobThresholdSystem _thresholds = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedFlashSystem _flash = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedStunSystem _stun = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private ThrowingSystem _throwing = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<VoidDrakeComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<VoidDrakeComponent, VoidDrakeGravityWellEvent>(OnGravityWell);
        SubscribeLocalEvent<VoidDrakeComponent, VoidDrakeWingTempestEvent>(OnWingTempest);
        SubscribeLocalEvent<VoidDrakeComponent, VoidDrakeSolarFlareEvent>(OnSolarFlare);
        SubscribeLocalEvent<VoidDrakeComponent, MobStateChangedEvent>(OnMobState);
        SubscribeLocalEvent<VoidDrakeComponent, StartCollideEvent>(OnRamCollide);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<VoidDrakeComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var drake, out _))
        {
            if (_mobState.IsDead(uid))
                continue;

            TryRam(uid, drake);

            if (IsPlayerControlled(uid) || _timing.CurTime < drake.NextNpcCheck)
                continue;

            drake.NextNpcCheck = _timing.CurTime + TimeSpan.FromSeconds(1.2);

            var nearShuttle = _hunt.TryGetHuntTarget(uid, 120f, out _, out _);
            if (!nearShuttle && !HasNearbyPrey(uid, drake.GravityRange + 2f))
                continue;

            if (_timing.CurTime >= drake.NextGravity)
                TryGravityWell(uid, drake);

            if (_timing.CurTime >= drake.NextTempest)
                TryWingTempest(uid, drake);

            if (_timing.CurTime >= drake.NextFlare)
                TrySolarFlare(uid, drake);
        }
    }

    private void OnMapInit(Entity<VoidDrakeComponent> ent, ref MapInitEvent args)
    {
        LeviathanVitals.RollHealth(_thresholds, _random, ent, ent.Comp.MinHealth, ent.Comp.MaxHealth);
    }

    private void OnRamCollide(Entity<VoidDrakeComponent> ent, ref StartCollideEvent args)
    {
        if (_mobState.IsDead(ent) || _timing.CurTime < ent.Comp.NextRam)
            return;

        if (!HasComp<MapGridComponent>(args.OtherEntity) && !Transform(args.OtherEntity).Anchored)
            return;

        TryRam(ent, ent.Comp, force: true);
    }

    private void TryRam(EntityUid uid, VoidDrakeComponent drake, bool force = false)
    {
        if (!force && _timing.CurTime < drake.NextRam)
            return;

        drake.NextRam = _timing.CurTime + drake.RamInterval;
        _smash.Chew(uid, _transform.GetMapCoordinates(uid), drake.RamRadius);
    }

    private void OnGravityWell(Entity<VoidDrakeComponent> ent, ref VoidDrakeGravityWellEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = TryGravityWell(ent, ent.Comp);
    }

    private void OnWingTempest(Entity<VoidDrakeComponent> ent, ref VoidDrakeWingTempestEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = TryWingTempest(ent, ent.Comp);
    }

    private void OnSolarFlare(Entity<VoidDrakeComponent> ent, ref VoidDrakeSolarFlareEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = TrySolarFlare(ent, ent.Comp);
    }

    private void OnMobState(Entity<VoidDrakeComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Dead)
            return;

        _audio.PlayPvs(ent.Comp.SoundRoar, ent);
    }

    private bool TryGravityWell(EntityUid uid, VoidDrakeComponent drake)
    {
        if (_mobState.IsDead(uid))
            return false;

        _popup.PopupEntity(Loc.GetString("forge-leviathan-drake-gravity"), uid, PopupType.LargeCaution);
        _audio.PlayPvs(drake.SoundRoar, uid);
        Spawn("EffectEmpBlast", Transform(uid).Coordinates);

        var origin = _transform.GetMapCoordinates(uid);
        foreach (var target in _lookup.GetEntitiesInRange(uid, drake.GravityRange))
        {
            if (!CanAffect(uid, target) || !TryComp<PhysicsComponent>(target, out var physics))
                continue;

            if (physics.BodyType == BodyType.Static)
                continue;

            var dest = _transform.GetMapCoordinates(target);
            if (origin.MapId != dest.MapId)
                continue;

            var delta = origin.Position - dest.Position;
            var length = delta.Length();
            if (length < 0.2f)
                continue;

            _throwing.TryThrow(target, delta.Normalized() * drake.GravityPull, 8f, uid);
            _damageable.TryChangeDamage(target, drake.GravityDamage, origin: uid);
        }

        drake.NextGravity = _timing.CurTime + drake.GravityCooldown;
        return true;
    }

    private bool TryWingTempest(EntityUid uid, VoidDrakeComponent drake)
    {
        if (_mobState.IsDead(uid))
            return false;

        _popup.PopupEntity(Loc.GetString("forge-leviathan-drake-tempest"), uid, PopupType.LargeCaution);
        _audio.PlayPvs(drake.SoundRoar, uid);

        var origin = _transform.GetMapCoordinates(uid);
        foreach (var target in _lookup.GetEntitiesInRange(uid, drake.TempestRange))
        {
            if (!CanAffect(uid, target) || !TryComp<PhysicsComponent>(target, out var physics))
                continue;

            if (physics.BodyType == BodyType.Static)
                continue;

            var dest = _transform.GetMapCoordinates(target);
            if (origin.MapId != dest.MapId)
                continue;

            var delta = dest.Position - origin.Position;
            if (delta.LengthSquared() < 0.01f)
                delta = _transform.GetWorldRotation(uid).ToVec();

            _throwing.TryThrow(target, delta.Normalized() * drake.TempestThrow, 12f, uid);
            _stun.TryKnockdown(target, drake.TempestStun, true);
        }

        drake.NextTempest = _timing.CurTime + drake.TempestCooldown;
        return true;
    }

    private bool TrySolarFlare(EntityUid uid, VoidDrakeComponent drake)
    {
        if (_mobState.IsDead(uid))
            return false;

        _popup.PopupEntity(Loc.GetString("forge-leviathan-drake-flare"), uid, PopupType.LargeCaution);
        _audio.PlayPvs(drake.SoundFlare, uid);
        _flash.FlashArea(uid, uid, drake.FlareRange, drake.FlareFlash, 0.6f, true);
        _emp.EmpPulse(_transform.GetMapCoordinates(uid), drake.FlareRange * 0.7f, drake.FlareEmpEnergy, TimeSpan.FromSeconds(8), uid);

        foreach (var target in _lookup.GetEntitiesInRange(uid, drake.FlareRange))
        {
            if (!CanAffect(uid, target))
                continue;

            _damageable.TryChangeDamage(target, drake.FlareDamage, origin: uid);
        }

        drake.NextFlare = _timing.CurTime + drake.FlareCooldown;
        return true;
    }

    private bool HasNearbyPrey(EntityUid uid, float range)
    {
        foreach (var target in _lookup.GetEntitiesInRange(uid, range))
        {
            if (!CanAffect(uid, target))
                continue;

            if (TryComp<MobStateComponent>(target, out var mob) && mob.CurrentState != MobState.Dead)
                return true;
        }

        return false;
    }

    private bool CanAffect(EntityUid user, EntityUid target)
    {
        if (target == user)
            return false;

        if (HasComp<GhostComponent>(target) ||
            HasComp<MapGridComponent>(target) ||
            HasComp<MapComponent>(target) ||
            HasComp<VoidWormSegmentComponent>(target) ||
            HasComp<VoidDrakeComponent>(target) ||
            HasComp<VoidWormComponent>(target) ||
            HasComp<VoidMedusaComponent>(target) ||
            HasComp<VoidMedusaPolypComponent>(target))
            return false;

        return true;
    }

    private bool IsPlayerControlled(EntityUid uid)
    {
        return TryComp<MindContainerComponent>(uid, out var mind) && mind.HasMind;
    }
}
