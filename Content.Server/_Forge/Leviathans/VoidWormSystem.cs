using System.Linq;
using System.Numerics;
using Content.Shared._Forge.Leviathans;
using Content.Shared._Forge.Leviathans.Components;
using Content.Shared.Damage;
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
using Robust.Shared.Physics.Events;
using Robust.Shared.Physics.Components;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Forge.Leviathans;

public sealed partial class VoidWormSystem : EntitySystem
{
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private LeviathanHuntSystem _hunt = default!;
    [Dependency] private LeviathanSmashSystem _smash = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private MobThresholdSystem _thresholds = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedStunSystem _stun = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<VoidWormComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<VoidWormComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<VoidWormComponent, MobStateChangedEvent>(OnMobState);
        SubscribeLocalEvent<VoidWormSegmentComponent, MobStateChangedEvent>(OnSegmentMobState);
        SubscribeLocalEvent<VoidWormComponent, VoidWormWarpBreachEvent>(OnWarp);
        SubscribeLocalEvent<VoidWormComponent, VoidWormCoilSmashEvent>(OnCoil);
        SubscribeLocalEvent<VoidWormComponent, VoidWormBroodBurstEvent>(OnBrood);
        SubscribeLocalEvent<VoidWormComponent, StartCollideEvent>(OnRamCollide);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<VoidWormComponent>();
        while (query.MoveNext(out var uid, out var worm))
        {
            if (_mobState.IsDead(uid))
            {
                DeleteSegments(worm);
                continue;
            }

            UpdateChain(uid, worm);
            PruneBrood(worm);
            TryRam(uid, worm);
            TryWrapCrush(uid, worm);

            if (IsPlayerControlled(uid) || _timing.CurTime < worm.NextNpcCheck)
                continue;

            worm.NextNpcCheck = _timing.CurTime + TimeSpan.FromSeconds(1.4);

            var hunting = _hunt.TryGetHuntTarget(uid, worm.HuntRange, out _, out var dest);
            var hasPrey = TryGetPrey(uid, worm.WarpRange, out var prey);
            if (!hunting && !hasPrey)
                continue;

            if (_timing.CurTime >= worm.NextCoil)
                TryCoil(uid, worm);

            if (_timing.CurTime >= worm.NextWarp)
            {
                if (hunting)
                    TryWarp(uid, worm, _transform.ToCoordinates(dest));
                else
                    TryWarp(uid, worm, Transform(prey).Coordinates);
            }

            if (_timing.CurTime >= worm.NextBrood)
                TryBrood(uid, worm);
        }
    }

    private void OnMapInit(Entity<VoidWormComponent> ent, ref MapInitEvent args)
    {
        LeviathanVitals.RollHealth(_thresholds, _random, ent, ent.Comp.MinHealth, ent.Comp.MaxHealth);

        if (ent.Comp.IsFragment)
        {
            ent.Comp.SegmentCount = ent.Comp.Segments.Count;
            foreach (var segment in ent.Comp.Segments)
            {
                if (TryComp<VoidWormSegmentComponent>(segment, out var segComp))
                    segComp.OwnerWorm = ent;
            }

            SnapChain(ent, ent.Comp, _transform.GetWorldPosition(ent.Owner), _transform.GetWorldRotation(ent.Owner).ToVec());
            return;
        }

        RollLength(ent.Comp);
        EnsureSegments(ent, ent.Comp);
        SnapChain(ent, ent.Comp, _transform.GetWorldPosition(ent.Owner), _transform.GetWorldRotation(ent.Owner).ToVec());
    }

    private void RollLength(VoidWormComponent worm)
    {
        var min = MathF.Min(worm.MinLength, worm.MaxLength);
        var max = MathF.Max(worm.MinLength, worm.MaxLength);
        var length = min >= max ? min : _random.NextFloat(min, max);
        var spacing = MathF.Max(0.5f, worm.SegmentSpacing);
        worm.SegmentCount = Math.Max(8, (int)MathF.Round(length / spacing));
    }

    private void OnShutdown(Entity<VoidWormComponent> ent, ref ComponentShutdown args)
    {
        DeleteSegments(ent.Comp);
    }

    private void OnMobState(Entity<VoidWormComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Dead)
            return;

        _audio.PlayPvs(ent.Comp.SoundRoar, ent);
        PromoteRemainder(ent, ent.Comp);
    }

    private void OnSegmentMobState(Entity<VoidWormSegmentComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Dead)
            return;

        var owner = ent.Comp.OwnerWorm;
        if (!Exists(owner) || Deleted(owner) || !TryComp<VoidWormComponent>(owner, out var worm))
        {
            QueueDel(ent);
            return;
        }

        SplitAt(owner, worm, ent);
    }

    private void OnWarp(Entity<VoidWormComponent> ent, ref VoidWormWarpBreachEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = TryWarp(ent, ent.Comp, args.Target);
    }

    private void OnCoil(Entity<VoidWormComponent> ent, ref VoidWormCoilSmashEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = TryCoil(ent, ent.Comp);
    }

    private void OnBrood(Entity<VoidWormComponent> ent, ref VoidWormBroodBurstEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = TryBrood(ent, ent.Comp);
    }

    private void OnRamCollide(Entity<VoidWormComponent> ent, ref StartCollideEvent args)
    {
        if (_mobState.IsDead(ent) || _timing.CurTime < ent.Comp.NextRam)
            return;

        if (!IsGridOrStructure(args.OtherEntity))
            return;

        TryRam(ent, ent.Comp, force: true);
    }

    private bool TryWarp(EntityUid uid, VoidWormComponent worm, EntityCoordinates target)
    {
        if (_mobState.IsDead(uid))
            return false;

        var origin = _transform.GetMapCoordinates(uid);
        var dest = _transform.ToMapCoordinates(target);
        if (origin.MapId != dest.MapId)
            return false;

        var delta = dest.Position - origin.Position;
        var dist = delta.Length();
        if (dist < 0.5f)
            return false;

        if (dist > worm.WarpRange)
            dest = new MapCoordinates(origin.Position + delta / dist * worm.WarpRange, origin.MapId);

        _popup.PopupEntity(Loc.GetString("forge-leviathan-worm-warp"), uid, PopupType.LargeCaution);
        _audio.PlayPvs(worm.SoundWarp, uid);

        var fromCoords = Transform(uid).Coordinates;
        Spawn("EffectFlashBluespace", fromCoords);
        Burst(uid, worm, origin, fromCoords);

        _transform.SetMapCoordinates(uid, dest);
        _transform.AttachToGridOrMap(uid);

        var afterCoords = Transform(uid).Coordinates;
        Spawn("EffectFlashBluespace", afterCoords);
        Burst(uid, worm, dest, afterCoords);

        SnapChain(uid, worm, dest.Position, delta.LengthSquared() > 0.01f ? delta : Vector2.UnitX);
        worm.NextWarp = _timing.CurTime + worm.WarpCooldown;
        return true;
    }

    private bool TryCoil(EntityUid uid, VoidWormComponent worm)
    {
        if (_mobState.IsDead(uid))
            return false;

        _popup.PopupEntity(Loc.GetString("forge-leviathan-worm-coil"), uid, PopupType.LargeCaution);
        _audio.PlayPvs(worm.SoundCoil, uid);

        var hit = new HashSet<EntityUid>();
        BurstAround(uid, uid, worm.CoilRange, worm.CoilDamage, worm.CoilStun, hit);
        for (var i = 7; i < worm.Segments.Count; i += 8)
        {
            var segment = worm.Segments[i];
            if (Exists(segment) && !Deleted(segment))
                BurstAround(uid, segment, MathF.Min(worm.CoilRange, 4.5f), worm.CoilDamage, worm.CoilStun, hit);
        }

        worm.NextCoil = _timing.CurTime + worm.CoilCooldown;
        return true;
    }

    private void BurstAround(EntityUid user, EntityUid origin, float range, DamageSpecifier damage, TimeSpan stun, HashSet<EntityUid> hit)
    {
        foreach (var target in _lookup.GetEntitiesInRange(origin, range))
        {
            if (!hit.Add(target) || !CanAffect(user, target))
                continue;

            _damageable.TryChangeDamage(target, damage, origin: user);
            if (HasComp<MobStateComponent>(target))
                _stun.TryParalyze(target, stun, true);
        }
    }

    private bool TryBrood(EntityUid uid, VoidWormComponent worm)
    {
        if (_mobState.IsDead(uid))
            return false;

        PruneBrood(worm);
        if (worm.Brood.Count >= worm.MaxBrood)
            return false;

        _popup.PopupEntity(Loc.GetString("forge-leviathan-worm-brood"), uid, PopupType.MediumCaution);
        _audio.PlayPvs(worm.SoundRoar, uid);

        var origin = Transform(uid).Coordinates;
        var toSpawn = Math.Min(worm.BroodCount, worm.MaxBrood - worm.Brood.Count);
        for (var i = 0; i < toSpawn; i++)
        {
            var offset = _random.NextVector2(0.6f, worm.BroodSpread);
            var spawned = Spawn(worm.BroodPrototype, origin.Offset(offset));
            worm.Brood.Add(spawned);
        }

        worm.NextBrood = _timing.CurTime + worm.BroodCooldown;
        return true;
    }

    private void Burst(EntityUid uid, VoidWormComponent worm, MapCoordinates mapCoords, EntityCoordinates spawnCoords)
    {
        Spawn("EffectEmpBlast", spawnCoords);

        foreach (var target in _lookup.GetEntitiesInRange(mapCoords, worm.WarpBurstRange))
        {
            if (!CanAffect(uid, target))
                continue;

            _damageable.TryChangeDamage(target, worm.WarpDamage, origin: uid);
        }
    }

    private void TryRam(EntityUid uid, VoidWormComponent worm, bool force = false)
    {
        if (!force && _timing.CurTime < worm.NextRam)
            return;

        worm.NextRam = _timing.CurTime + worm.RamInterval;
        _smash.Chew(uid, _transform.GetMapCoordinates(uid), worm.RamRadius);
    }

    private void TryWrapCrush(EntityUid uid, VoidWormComponent worm)
    {
        if (_timing.CurTime < worm.NextCrush)
            return;

        worm.NextCrush = _timing.CurTime + worm.CrushInterval;

        var grids = new Dictionary<EntityUid, WrapTally>();
        TallyWrap(uid, worm.WrapMargin, grids);
        foreach (var segment in worm.Segments)
        {
            if (!Exists(segment) || Deleted(segment))
                continue;

            TallyWrap(segment, worm.WrapMargin, grids);
        }

        var minHits = Math.Max(6, (int)MathF.Ceiling(worm.SegmentCount * worm.WrapCoverage));
        foreach (var (gridUid, tally) in grids)
        {
            if (tally.Count < minHits || tally.Quadrants != 0b1111)
                continue;

            if (!HasComp<MapGridComponent>(gridUid))
                continue;

            if (_timing.CurTime >= worm.NextCrushPopup)
            {
                _popup.PopupEntity(Loc.GetString("forge-leviathan-worm-crush"), uid, PopupType.LargeCaution);
                _audio.PlayPvs(worm.SoundCoil, uid);
                worm.NextCrushPopup = _timing.CurTime + TimeSpan.FromSeconds(8);
            }

            CrushAround(uid, uid, worm);
            foreach (var segment in worm.Segments)
            {
                if (Exists(segment) && !Deleted(segment))
                    CrushAround(uid, segment, worm);
            }

            break;
        }
    }

    private void CrushAround(EntityUid user, EntityUid origin, VoidWormComponent worm)
    {
        _smash.Crush(user, _transform.GetMapCoordinates(origin), worm.CrushRadius, worm.CrushDamage);
    }

    private void TallyWrap(EntityUid uid, float margin, Dictionary<EntityUid, WrapTally> grids)
    {
        var pos = _transform.GetWorldPosition(uid);
        var nearby = new List<Entity<MapGridComponent>>();
        var box = new Box2(pos - new Vector2(margin, margin), pos + new Vector2(margin, margin));
        _map.FindGridsIntersecting(Transform(uid).MapID, box, ref nearby, includeMap: false);
        foreach (var found in nearby)
            Accrue(found.Owner, found.Comp, pos, margin, grids);
    }

    private void Accrue(EntityUid gridUid, MapGridComponent grid, Vector2 worldPos, float margin, Dictionary<EntityUid, WrapTally> grids)
    {
        if (!_smash.TryGetGridAabb(gridUid, grid, out var aabb))
            return;

        var expanded = aabb.Enlarged(margin);
        if (!expanded.Contains(worldPos))
            return;

        if (!grids.TryGetValue(gridUid, out var tally))
            tally = new WrapTally();

        tally.Count++;
        var center = aabb.Center;
        if (worldPos.X >= center.X)
            tally.Quadrants |= 0b0001;
        else
            tally.Quadrants |= 0b0010;
        if (worldPos.Y >= center.Y)
            tally.Quadrants |= 0b0100;
        else
            tally.Quadrants |= 0b1000;

        grids[gridUid] = tally;
    }

    private void UpdateChain(EntityUid uid, VoidWormComponent worm)
    {
        FaceMovement(uid);

        var prev = _transform.GetWorldPosition(uid);
        var prevRot = _transform.GetWorldRotation(uid);

        for (var i = 0; i < worm.Segments.Count; i++)
        {
            var segment = worm.Segments[i];
            if (!Exists(segment) || Deleted(segment) || _mobState.IsDead(segment))
                continue;

            var current = _transform.GetWorldPosition(segment);
            var away = current - prev;
            if (away.LengthSquared() < 0.0001f)
                away = -prevRot.ToVec();

            var pos = prev + away.Normalized() * worm.SegmentSpacing;
            _transform.SetWorldPosition(segment, pos);
            var towardHead = prev - pos;
            if (towardHead.LengthSquared() > 0.0001f)
                _transform.SetWorldRotation(segment, towardHead.ToWorldAngle());

            prev = pos;
            prevRot = _transform.GetWorldRotation(segment);
        }
    }

    private void SnapChain(EntityUid uid, VoidWormComponent worm, Vector2 headPos, Vector2 forward)
    {
        if (forward.LengthSquared() < 0.0001f)
            forward = Vector2.UnitX;
        else
            forward = forward.Normalized();

        _transform.SetWorldPosition(uid, headPos);
        _transform.SetWorldRotation(uid, forward.ToWorldAngle());

        var back = -forward;
        for (var i = 0; i < worm.Segments.Count; i++)
        {
            var segment = worm.Segments[i];
            if (!Exists(segment) || Deleted(segment))
                continue;

            var pos = headPos + back * worm.SegmentSpacing * (i + 1);
            _transform.SetWorldPosition(segment, pos);
            _transform.SetWorldRotation(segment, forward.ToWorldAngle());
        }
    }

    private void FaceMovement(EntityUid uid)
    {
        if (!TryComp<PhysicsComponent>(uid, out var physics))
            return;

        var vel = physics.LinearVelocity;
        if (vel.LengthSquared() < 0.04f)
            return;

        _transform.SetWorldRotation(uid, vel.ToWorldAngle());
    }

    private void EnsureSegments(EntityUid uid, VoidWormComponent worm)
    {
        worm.Segments.RemoveAll(ent => !Exists(ent) || Deleted(ent));

        while (worm.Segments.Count < worm.SegmentCount)
        {
            var index = worm.Segments.Count;
            var proto = index == worm.SegmentCount - 1
                ? worm.TailPrototype
                : (index % 2 == 0 ? worm.SegmentPrototype : worm.SegmentAltPrototype);
            var segment = Spawn(proto, Transform(uid).Coordinates);
            var segComp = EnsureComp<VoidWormSegmentComponent>(segment);
            segComp.OwnerWorm = uid;
            worm.Segments.Add(segment);
            LeviathanVitals.RollHealth(_thresholds, _random, segment, worm.SegmentMinHealth, worm.SegmentMaxHealth);
        }

        while (worm.Segments.Count > worm.SegmentCount)
        {
            var extra = worm.Segments[^1];
            worm.Segments.RemoveAt(worm.Segments.Count - 1);
            QueueDel(extra);
        }
    }

    private void SplitAt(EntityUid head, VoidWormComponent worm, EntityUid deadSegment)
    {
        var idx = worm.Segments.IndexOf(deadSegment);
        if (idx < 0)
        {
            QueueDel(deadSegment);
            return;
        }

        var front = new List<EntityUid>();
        var back = new List<EntityUid>();
        for (var i = 0; i < worm.Segments.Count; i++)
        {
            var segment = worm.Segments[i];
            if (i == idx || !IsLivingSegment(segment))
                continue;

            if (i < idx)
                front.Add(segment);
            else
                back.Add(segment);
        }

        worm.Segments.Clear();
        worm.Segments.AddRange(front);
        worm.SegmentCount = worm.Segments.Count;

        _popup.PopupEntity(Loc.GetString("forge-leviathan-worm-split"), head, PopupType.LargeCaution);
        _audio.PlayPvs(worm.SoundRoar, head);
        QueueDel(deadSegment);

        if (back.Count == 0)
            return;

        var seed = back[0];
        var coords = Transform(seed).Coordinates;
        var rest = back.GetRange(1, back.Count - 1);
        QueueDel(seed);
        SpawnFragment(worm, coords, rest);
    }

    private void PromoteRemainder(EntityUid deadHead, VoidWormComponent worm)
    {
        PruneDeadSegments(worm);
        if (worm.Segments.Count == 0)
            return;

        var seed = worm.Segments[0];
        var rest = worm.Segments.Skip(1).ToList();
        var coords = Transform(seed).Coordinates;
        worm.Segments.Clear();
        worm.SegmentCount = 0;
        QueueDel(seed);
        SpawnFragment(worm, coords, rest);
    }

    private void SpawnFragment(VoidWormComponent source, EntityCoordinates coords, List<EntityUid> segments)
    {
        var living = segments.Where(IsLivingSegment).ToList();
        var uid = EntityManager.CreateEntityUninitialized(source.HeadPrototype, coords);
        var worm = EnsureComp<VoidWormComponent>(uid);
        worm.IsFragment = true;
        worm.HeadPrototype = source.HeadPrototype;
        worm.SegmentCount = living.Count;
        worm.HuntSpeed = source.HuntSpeed;
        worm.MinHealth = source.MinHealth;
        worm.MaxHealth = source.MaxHealth;
        worm.SegmentMinHealth = source.SegmentMinHealth;
        worm.SegmentMaxHealth = source.SegmentMaxHealth;
        worm.Segments.Clear();
        worm.Segments.AddRange(living);
        foreach (var segment in living)
        {
            if (TryComp<VoidWormSegmentComponent>(segment, out var segComp))
                segComp.OwnerWorm = uid;
        }

        EntityManager.InitializeAndStartEntity(uid);
    }

    private void PruneDeadSegments(VoidWormComponent worm)
    {
        worm.Segments.RemoveAll(ent => !IsLivingSegment(ent));
        worm.SegmentCount = worm.Segments.Count;
    }

    private bool IsLivingSegment(EntityUid uid)
    {
        return Exists(uid) && !Deleted(uid) && !_mobState.IsDead(uid);
    }

    private void DeleteSegments(VoidWormComponent worm)
    {
        foreach (var segment in worm.Segments)
        {
            if (Exists(segment))
                QueueDel(segment);
        }

        worm.Segments.Clear();
    }

    private void PruneBrood(VoidWormComponent worm)
    {
        worm.Brood.RemoveAll(ent => !Exists(ent) || Deleted(ent) || _mobState.IsDead(ent));
    }

    private bool TryGetPrey(EntityUid uid, float range, out EntityUid prey)
    {
        prey = default;
        foreach (var target in _lookup.GetEntitiesInRange(uid, range))
        {
            if (!CanAffect(uid, target))
                continue;

            if (!TryComp<MobStateComponent>(target, out var mob) || mob.CurrentState == MobState.Dead)
                continue;

            prey = target;
            return true;
        }

        return false;
    }

    private bool IsGridOrStructure(EntityUid uid)
    {
        return HasComp<MapGridComponent>(uid) ||
               Transform(uid).Anchored ||
               HasComp<DamageableComponent>(uid);
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

    private struct WrapTally
    {
        public int Count;
        public int Quadrants;
    }
}
