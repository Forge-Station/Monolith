using System.Numerics;
using Content.Server.Emp;
using Content.Server.Shuttles.Components;
using Content.Shared._Forge.Leviathans;
using Content.Shared._Forge.Leviathans.Components;
using Content.Shared.Ghost;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Stunnable;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Forge.Leviathans;

/// <summary>
/// Latch-tractor, ion EMP, stasis freeze and polyp rain — no hull chewing.
/// </summary>
public sealed partial class VoidMedusaSystem : EntitySystem
{
    [Dependency] private EmpSystem _emp = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private LeviathanHuntSystem _hunt = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private MobThresholdSystem _thresholds = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedStunSystem _stun = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    private List<Entity<MapGridComponent>> _grids = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<VoidMedusaComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<VoidMedusaComponent, VoidMedusaLatchEvent>(OnLatch);
        SubscribeLocalEvent<VoidMedusaComponent, VoidMedusaIonBloomEvent>(OnIonBloom);
        SubscribeLocalEvent<VoidMedusaComponent, VoidMedusaStasisVeilEvent>(OnStasisVeil);
        SubscribeLocalEvent<VoidMedusaComponent, VoidMedusaPolypRainEvent>(OnPolypRain);
        SubscribeLocalEvent<VoidMedusaPolypComponent, MobStateChangedEvent>(OnPolypMobState);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<VoidMedusaComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var medusa, out var xform))
        {
            if (_mobState.IsDead(uid))
            {
                medusa.LatchedGrid = null;
                continue;
            }

            TickLatch(uid, medusa, xform);

            if (IsPlayerControlled(uid) || _timing.CurTime < medusa.NextNpcCheck)
                continue;

            medusa.NextNpcCheck = _timing.CurTime + medusa.NpcAbilityRangeCheck;

            var near = _hunt.TryGetHuntTarget(uid, medusa.LatchRange + 8f, out _, out _);
            if (!near)
                continue;

            if (_timing.CurTime >= medusa.NextIon)
                TryIonBloom(uid, medusa);

            if (_timing.CurTime >= medusa.NextStasis)
                TryStasisVeil(uid, medusa);

            if (_timing.CurTime >= medusa.NextPolyp)
                TryPolypRain(uid, medusa);
        }
    }

    private void OnMapInit(Entity<VoidMedusaComponent> ent, ref MapInitEvent args)
    {
        LeviathanVitals.RollHealth(_thresholds, _random, ent, ent.Comp.MinHealth, ent.Comp.MaxHealth);
    }

    private void OnLatch(Entity<VoidMedusaComponent> ent, ref VoidMedusaLatchEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = TryLatch(ent, ent.Comp, announce: true);
    }

    private void OnIonBloom(Entity<VoidMedusaComponent> ent, ref VoidMedusaIonBloomEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = TryIonBloom(ent, ent.Comp);
    }

    private void OnStasisVeil(Entity<VoidMedusaComponent> ent, ref VoidMedusaStasisVeilEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = TryStasisVeil(ent, ent.Comp);
    }

    private void OnPolypRain(Entity<VoidMedusaComponent> ent, ref VoidMedusaPolypRainEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = TryPolypRain(ent, ent.Comp);
    }

    private void TickLatch(EntityUid uid, VoidMedusaComponent medusa, TransformComponent xform)
    {
        if (!_hunt.TryGetHuntTarget(uid, medusa.LatchRange, out _, out var dest))
        {
            medusa.LatchedGrid = null;
            return;
        }

        var origin = _transform.GetMapCoordinates(uid, xform);
        if (origin.MapId != dest.MapId)
        {
            medusa.LatchedGrid = null;
            return;
        }

        if (!TryFindGrid(dest, out var grid))
        {
            medusa.LatchedGrid = null;
            return;
        }

        var wasLatched = medusa.LatchedGrid == grid;
        medusa.LatchedGrid = grid;

        if (!wasLatched && _timing.CurTime >= medusa.NextLatchAnnounce)
        {
            medusa.NextLatchAnnounce = _timing.CurTime + medusa.LatchAnnounceCooldown;
            _popup.PopupEntity(Loc.GetString("forge-leviathan-medusa-latch"), uid, PopupType.LargeCaution);
            _audio.PlayPvs(medusa.SoundLatch, uid);
        }

        PullGrid(uid, medusa, grid, origin);

        if (_timing.CurTime < medusa.StasisUntil)
            DampGrid(grid, medusa.StasisGridDamp);
    }

    private bool TryLatch(EntityUid uid, VoidMedusaComponent medusa, bool announce)
    {
        if (!_hunt.TryGetHuntTarget(uid, medusa.LatchRange, out _, out var dest))
            return false;

        var origin = _transform.GetMapCoordinates(uid);
        if (origin.MapId != dest.MapId || !TryFindGrid(dest, out var grid))
            return false;

        medusa.LatchedGrid = grid;
        PullGrid(uid, medusa, grid, origin);

        if (announce)
        {
            _popup.PopupEntity(Loc.GetString("forge-leviathan-medusa-latch"), uid, PopupType.LargeCaution);
            _audio.PlayPvs(medusa.SoundLatch, uid);
        }

        return true;
    }

    private bool TryIonBloom(EntityUid uid, VoidMedusaComponent medusa)
    {
        var coords = Transform(uid).Coordinates;
        _emp.EmpPulse(coords, medusa.IonRange, medusa.IonEnergy, medusa.IonDuration, uid);
        _popup.PopupEntity(Loc.GetString("forge-leviathan-medusa-ion"), uid, PopupType.LargeCaution);
        _audio.PlayPvs(medusa.SoundIon, uid);
        Spawn("EffectEmpBlast", coords);
        medusa.NextIon = _timing.CurTime + medusa.IonCooldown;
        return true;
    }

    private bool TryStasisVeil(EntityUid uid, VoidMedusaComponent medusa)
    {
        foreach (var target in _lookup.GetEntitiesInRange(uid, medusa.StasisRange))
        {
            if (!CanAffect(uid, target) || !HasComp<MobStateComponent>(target))
                continue;

            if (TryComp<MobStateComponent>(target, out var mob) && mob.CurrentState == MobState.Dead)
                continue;

            _stun.TrySlowdown(
                target,
                medusa.StasisDuration,
                true,
                medusa.StasisWalkMultiplier,
                medusa.StasisRunMultiplier);
        }

        if (medusa.LatchedGrid is { } grid)
            DampGrid(grid, medusa.StasisGridDamp);

        medusa.StasisUntil = _timing.CurTime + medusa.StasisDuration;
        _popup.PopupEntity(Loc.GetString("forge-leviathan-medusa-stasis"), uid, PopupType.LargeCaution);
        _audio.PlayPvs(medusa.SoundStasis, uid);
        Spawn("EffectFlashBluespace", Transform(uid).Coordinates);
        medusa.NextStasis = _timing.CurTime + medusa.StasisCooldown;
        return true;
    }

    private bool TryPolypRain(EntityUid uid, VoidMedusaComponent medusa)
    {
        var origin = Transform(uid).Coordinates;
        for (var i = 0; i < medusa.PolypCount; i++)
        {
            var angle = _random.NextFloat() * MathF.Tau;
            var dist = 6f + _random.NextFloat() * medusa.PolypRadius;
            var offset = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * dist;
            Spawn(medusa.PolypPrototype, origin.Offset(offset));
        }

        _popup.PopupEntity(Loc.GetString("forge-leviathan-medusa-polyp"), uid, PopupType.MediumCaution);
        Spawn("EffectEmpBlast", origin);
        medusa.NextPolyp = _timing.CurTime + medusa.PolypCooldown;
        return true;
    }

    private void OnPolypMobState(Entity<VoidMedusaPolypComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Dead)
            return;

        var coords = Transform(ent).Coordinates;
        _emp.EmpPulse(coords, ent.Comp.EmpRange, ent.Comp.EmpEnergy, ent.Comp.EmpDuration, ent);
    }

    private void PullGrid(EntityUid _, VoidMedusaComponent medusa, EntityUid grid, MapCoordinates origin)
    {
        if (!HasComp<ShuttleComponent>(grid) || !TryComp<PhysicsComponent>(grid, out var body))
            return;

        if (body.BodyType != Robust.Shared.Physics.BodyType.Dynamic)
            return;

        var gridPos = _lookup.GetWorldAABB(grid).Center;
        var delta = origin.Position - gridPos;
        var dist = delta.Length();
        if (dist < 0.2f)
            return;

        var pull = (delta / dist) * medusa.LatchPull;
        _physics.SetLinearVelocity(grid, body.LinearVelocity * 0.82f + pull, body: body);
        _physics.SetAngularVelocity(grid, body.AngularVelocity * 0.72f, body: body);
    }

    private void DampGrid(EntityUid grid, float factor)
    {
        if (!TryComp<PhysicsComponent>(grid, out var body))
            return;

        _physics.SetLinearVelocity(grid, body.LinearVelocity * factor, body: body);
        _physics.SetAngularVelocity(grid, body.AngularVelocity * factor, body: body);
    }

    private bool TryFindGrid(MapCoordinates dest, out EntityUid grid)
    {
        grid = default;
        _grids.Clear();
        var half = new Vector2(10f, 10f);
        var box = new Box2(dest.Position - half, dest.Position + half);
        _map.FindGridsIntersecting(dest.MapId, box, ref _grids, includeMap: false);

        var best = float.MaxValue;
        var found = false;
        foreach (var foundGrid in _grids)
        {
            var dist = (_lookup.GetWorldAABB(foundGrid.Owner).Center - dest.Position).Length();
            if (dist >= best)
                continue;

            best = dist;
            grid = foundGrid.Owner;
            found = true;
        }

        return found;
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
