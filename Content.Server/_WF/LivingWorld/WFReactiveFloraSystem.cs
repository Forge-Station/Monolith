using System.Linq;
using System.Numerics;
using Content.Server._WF.Dormancy;
using Content.Server.Body.Systems;
using Content.Server.Explosion.EntitySystems;
using Content.Server.Fluids.EntitySystems;
using Content.Server.Radiation.Systems;
using Content.Server.Spreader;
using Content.Shared._WF.LivingWorld;
using Content.Shared.Alert;
using Content.Shared.Chemistry.Components;
using Content.Shared.Coordinates.Helpers;
using Content.Shared.Damage;
using Content.Shared.Interaction;
using Content.Shared.Maps;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Systems;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Systems;
using Content.Shared.Popups;
using Content.Shared.Stunnable;
using Content.Shared.Throwing;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Server._WF.LivingWorld;

/// <summary>Runs <see cref="WFReactiveFloraComponent"/>: finds mobs near each plant when its timer runs out and reacts.</summary>
public sealed class WFReactiveFloraSystem : EntitySystem
{
    [Dependency] private AlertsSystem _alerts = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private BloodstreamSystem _bloodstream = default!;
    [Dependency] private DamageableSystem _damage = default!;
    [Dependency] private WFDormancySystem _dormancy = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private ExplosionSystem _explosion = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private MovementSpeedModifierSystem _speed = default!;
    [Dependency] private NpcFactionSystem _faction = default!;
    [Dependency] private PuddleSystem _puddle = default!;
    [Dependency] private RadiationSystem _radiation = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private SharedGunSystem _gun = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedStunSystem _stun = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SmokeSystem _smoke = default!;
    [Dependency] private SpreaderSystem _spreader = default!;
    [Dependency] private ThrowingSystem _throwing = default!;
    [Dependency] private TurfSystem _turf = default!;

    /// <summary>How often a plant with no player near looks again whether one has come.</summary>
    private static readonly TimeSpan DormantCheck = TimeSpan.FromSeconds(2);

    private readonly HashSet<Entity<MobStateComponent>> _near = new();
    private readonly List<(EntityUid Uid, float Distance)> _targets = new();
    private readonly List<EntityUid> _friends = new();

    // Idle plants wait on a timer queue; the 1,700 on a round's worlds are not walked every tick.
    private readonly PriorityQueue<EntityUid, TimeSpan> _queue = new();
    private readonly Dictionary<EntityUid, TimeSpan> _scheduled = new();
    private readonly HashSet<EntityUid> _busy = new();
    private readonly List<EntityUid> _busyNow = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFReactiveFloraComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<WFReactiveFloraComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<WFReactiveFloraComponent, DamageChangedEvent>(OnDamaged);
        SubscribeLocalEvent<WFFloraGrabbedComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshSpeed);
        SubscribeLocalEvent<WFFloraGrabbedComponent, WFFloraStruggleAlertEvent>(OnStruggle);
    }

    private void OnMapInit(Entity<WFReactiveFloraComponent> ent, ref MapInitEvent args)
    {
        ent.Comp.NextCheck = _timing.CurTime + ent.Comp.CheckInterval;
        SetState(ent, WFReactiveFloraState.Idle);
        Schedule(ent, ent.Comp.NextCheck);
    }

    private void OnShutdown(Entity<WFReactiveFloraComponent> ent, ref ComponentShutdown args)
    {
        Release(ent, false);
        _scheduled.Remove(ent);
        _busy.Remove(ent);
    }

    private void OnDamaged(Entity<WFReactiveFloraComponent> ent, ref DamageChangedEvent args)
    {
        if (!args.DamageIncreased || args.DamageDelta is not { } delta)
            return;

        if (ent.Comp.Held != null)
        {
            ent.Comp.DamageWhileHolding += delta.GetTotal().Float();
            if (ent.Comp.DamageWhileHolding >= ent.Comp.CutFreeDamage)
                Release(ent, true);
        }

        if (ent.Comp.Behaviour == WFReactiveFloraBehaviour.Explode && ent.Comp.BurstOnDamage && ent.Comp.State != WFReactiveFloraState.Spent)
            Burst(ent);
    }

    private void OnRefreshSpeed(Entity<WFFloraGrabbedComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
    {
        args.ModifySpeed(ent.Comp.Speed, ent.Comp.Speed);
    }

    private void OnStruggle(Entity<WFFloraGrabbedComponent> ent, ref WFFloraStruggleAlertEvent args)
    {
        if (args.Handled)
            return;
        args.Handled = true;
        var now = _timing.CurTime;
        if (now < ent.Comp.NextStruggle || !TryComp(ent.Comp.Plant, out WFReactiveFloraComponent? plant))
            return;

        ent.Comp.NextStruggle = now + TimeSpan.FromSeconds(0.4);
        ent.Comp.Until -= plant.StruggleStep;
        _popup.PopupEntity(Loc.GetString("wf-flora-struggle"), ent, ent);
        _alerts.ShowAlert(ent, plant.GrabAlert, cooldown: (ent.Comp.Since, ent.Comp.Until));
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var now = _timing.CurTime;

        // Plants mid-reaction run every tick until they are idle again.
        if (_busy.Count > 0)
        {
            _busyNow.Clear();
            _busyNow.AddRange(_busy);
            foreach (var uid in _busyNow)
            {
                if (!TryComp(uid, out WFReactiveFloraComponent? flora) || TerminatingOrDeleted(uid))
                {
                    _busy.Remove(uid);
                    continue;
                }

                if (MetaData(uid).EntityPaused)
                    continue;

                Step((uid, flora), Transform(uid), now);
                if (flora.State == WFReactiveFloraState.Idle && flora.Held == null)
                {
                    _busy.Remove(uid);
                    Schedule(uid, flora.NextCheck);
                }
            }
        }

        // Idle plants come off the queue only when their check is due.
        while (_queue.TryPeek(out var uid, out var due) && due <= now)
        {
            _queue.Dequeue();
            if (!_scheduled.TryGetValue(uid, out var at) || at != due)
                continue;

            _scheduled.Remove(uid);
            if (!TryComp(uid, out WFReactiveFloraComponent? flora) || TerminatingOrDeleted(uid) || _busy.Contains(uid))
                continue;

            if (MetaData(uid).EntityPaused)
            {
                Schedule(uid, now + DormantCheck);
                continue;
            }

            Step((uid, flora), Transform(uid), now);
            if (!_busy.Contains(uid))
                Schedule(uid, flora.NextCheck > now ? flora.NextCheck : now + flora.CheckInterval);
        }
    }

    /// <summary>One plant's turn: runs out its armed or spent state, keeps its hold, or looks for mobs when due.</summary>
    private void Step(Entity<WFReactiveFloraComponent> ent, TransformComponent xform, TimeSpan now)
    {
        var (uid, flora) = ent;
        switch (flora.State)
        {
            case WFReactiveFloraState.Spent:
                if (now >= flora.StateEnd)
                {
                    SetState(ent, WFReactiveFloraState.Idle);
                    flora.NextCheck = now + flora.Cooldown;
                }
                return;
            case WFReactiveFloraState.Armed when flora.Behaviour == WFReactiveFloraBehaviour.Explode:
                if (now >= flora.StateEnd)
                    Burst(ent);
                return;
            case WFReactiveFloraState.Armed when flora.Held == null && now >= flora.StateEnd:
                SetState(ent, WFReactiveFloraState.Idle);
                break;
        }

        if (flora.Held is { } held)
        {
            UpdateHold(ent, xform, held, now);
            return;
        }

        if (now < flora.NextCheck)
            return;

        if (!_dormancy.IsActive(uid, xform))
        {
            flora.NextCheck = now + DormantCheck;
            return;
        }

        flora.NextCheck = now + (React(ent, xform) ? flora.Cooldown : flora.CheckInterval);
    }

    /// <summary>Has an idle plant look for mobs on the next tick.</summary>
    public void CheckNow(Entity<WFReactiveFloraComponent> ent)
    {
        ent.Comp.NextCheck = _timing.CurTime;
        if (!_busy.Contains(ent))
            Schedule(ent, ent.Comp.NextCheck);
    }

    /// <summary>Puts an idle plant on the queue for its next check; an earlier entry for it goes stale.</summary>
    private void Schedule(EntityUid uid, TimeSpan due)
    {
        _scheduled[uid] = due;
        _queue.Enqueue(uid, due);
    }

    /// <summary>Looks for mobs in range and reacts to them; true if it did anything.</summary>
    private bool React(Entity<WFReactiveFloraComponent> ent, TransformComponent xform)
    {
        var flora = ent.Comp;
        if (xform.MapUid == null)
            return false;

        FindTargets(ent, xform, flora.Range);
        var acted = false;
        if (_friends.Count > 0 && (flora.HealDamage != null || flora.HealSolution != null))
        {
            foreach (var friend in Limit(_friends, flora.MaxTargets))
                Heal(ent, friend);
            acted = true;
        }

        if (_targets.Count == 0)
            return acted;

        var nearest = _targets[0].Uid;
        switch (flora.Behaviour)
        {
            case WFReactiveFloraBehaviour.Lash:
                foreach (var (target, _) in Limit(_targets, flora.MaxTargets))
                    Lash(ent, target);
                break;
            case WFReactiveFloraBehaviour.Grab:
                Grab(ent, nearest);
                break;
            case WFReactiveFloraBehaviour.Spit:
                foreach (var (target, _) in Limit(_targets, flora.MaxTargets))
                    Spit(ent, target);
                break;
            case WFReactiveFloraBehaviour.Cloud:
                ReleaseCloud(ent, flora.Solution);
                PopupTargets(ent, flora.Popup);
                break;
            case WFReactiveFloraBehaviour.Explode:
                Arm(ent);
                return true;
            case WFReactiveFloraBehaviour.Irradiate:
                var seconds = (float) flora.Cooldown.TotalSeconds;
                foreach (var (target, _) in Limit(_targets, flora.MaxTargets))
                    _radiation.IrradiateEntity(target, flora.RadsPerSecond, seconds);
                break;
            case WFReactiveFloraBehaviour.Inject:
                foreach (var (target, _) in Limit(_targets, flora.MaxTargets))
                {
                    if (flora.Solution != null)
                        _bloodstream.TryAddToChemicals(target, flora.Solution.Clone());
                    Notify(ent, target);
                }
                break;
        }

        _audio.PlayPvs(flora.Sound, ent);
        if (flora.Held == null)
        {
            SetState(ent, WFReactiveFloraState.Armed);
            flora.StateEnd = _timing.CurTime + flora.ActiveTime;
        }
        return true;
    }

    /// <summary>
    /// Fills <see cref="_targets"/> with mobs it would act on, nearest first, and <see cref="_friends"/> with mobs it
    /// tends. Ignored factions, the dead, the contained and anything behind a wall are left out.
    /// </summary>
    private void FindTargets(Entity<WFReactiveFloraComponent> ent, TransformComponent xform, float range)
    {
        _targets.Clear();
        _friends.Clear();
        _near.Clear();
        var centre = _transform.GetMapCoordinates(ent, xform);
        _lookup.GetEntitiesInRange(centre, range, _near, LookupFlags.Dynamic);
        foreach (var mob in _near)
        {
            if (mob.Owner == ent.Owner || !_mobState.IsAlive(mob, mob.Comp) || _containers.IsEntityInContainer(mob))
                continue;
            if (ent.Comp.IgnoreFactions.Count > 0 && _faction.IsMemberOfAny(mob.Owner, ent.Comp.IgnoreFactions))
                continue;
            if (ent.Comp.RequireLineOfSight && !_interaction.InRangeUnobstructed(ent.Owner, mob.Owner, 0f))
                continue;

            if (ent.Comp.HealInsteadFactions.Count > 0 && _faction.IsMemberOfAny(mob.Owner, ent.Comp.HealInsteadFactions))
            {
                _friends.Add(mob);
                continue;
            }

            var distance = Vector2.Distance(centre.Position, _transform.GetWorldPosition(mob));
            _targets.Add((mob, distance));
        }
        _targets.Sort((a, b) => a.Distance.CompareTo(b.Distance));
    }

    private static IEnumerable<T> Limit<T>(List<T> list, int max)
    {
        return max <= 0 ? list : list.Take(max);
    }

    private void Heal(Entity<WFReactiveFloraComponent> ent, EntityUid friend)
    {
        if (ent.Comp.HealDamage != null)
            _damage.TryChangeDamage(friend, ent.Comp.HealDamage, true, false, origin: ent);
        if (ent.Comp.HealSolution != null)
            _bloodstream.TryAddToChemicals(friend, ent.Comp.HealSolution.Clone());
        _audio.PlayPvs(ent.Comp.HealSound, ent);
    }

    private void Lash(Entity<WFReactiveFloraComponent> ent, EntityUid target)
    {
        var flora = ent.Comp;
        if (flora.Damage != null)
            _damage.TryChangeDamage(target, flora.Damage, origin: ent);
        if (flora.StunTime > TimeSpan.Zero)
            _stun.TryStun(target, flora.StunTime, true);
        if (flora.KnockdownTime > TimeSpan.Zero)
            _stun.TryKnockdown(target, flora.KnockdownTime, true);
        if (flora.Knockback > 0f)
        {
            var away = _transform.GetWorldPosition(target) - _transform.GetWorldPosition(ent);
            if (away.LengthSquared() < 0.0001f)
                away = Vector2.UnitX;
            _throwing.TryThrow(target, away.Normalized() * flora.Knockback, flora.KnockbackSpeed, ent, recoil: false, playSound: false, doSpin: false);
        }
        Notify(ent, target);
    }

    private void Grab(Entity<WFReactiveFloraComponent> ent, EntityUid target)
    {
        var flora = ent.Comp;
        if (HasComp<WFFloraGrabbedComponent>(target))
            return;

        var now = _timing.CurTime;
        var grabbed = EnsureComp<WFFloraGrabbedComponent>(target);
        grabbed.Plant = ent;
        grabbed.Speed = flora.HeldSpeed;
        grabbed.Since = now;
        grabbed.Until = now + flora.HoldTime;
        flora.Held = target;
        flora.DamageWhileHolding = 0f;
        _speed.RefreshMovementSpeedModifiers(target);
        _alerts.ShowAlert(target, flora.GrabAlert, cooldown: (grabbed.Since, grabbed.Until));
        if (flora.Damage != null)
            _damage.TryChangeDamage(target, flora.Damage, origin: ent);
        SetState(ent, WFReactiveFloraState.Armed);
        Notify(ent, target);
    }

    /// <summary>Lets go when the hold runs out, its catch dies or is moved off, or the catch is gone.</summary>
    private void UpdateHold(Entity<WFReactiveFloraComponent> ent, TransformComponent xform, EntityUid held, TimeSpan now)
    {
        if (TerminatingOrDeleted(held) || !TryComp(held, out WFFloraGrabbedComponent? grabbed) || grabbed.Plant != ent.Owner)
        {
            Release(ent, false);
            return;
        }

        var heldXform = Transform(held);
        var far = heldXform.MapID != xform.MapID
            || Vector2.Distance(_transform.GetWorldPosition(xform), _transform.GetWorldPosition(heldXform)) > ent.Comp.Range + 1.5f;
        if (now >= grabbed.Until || far || !_mobState.IsAlive(held))
            Release(ent, true);
    }

    /// <summary>Frees whoever the plant is holding.</summary>
    public void Release(Entity<WFReactiveFloraComponent> ent, bool sound)
    {
        if (ent.Comp.Held is not { } held)
            return;

        ent.Comp.Held = null;
        ent.Comp.NextCheck = _timing.CurTime + ent.Comp.Cooldown;
        if (!TerminatingOrDeleted(ent))
            SetState(ent, WFReactiveFloraState.Idle);
        if (TerminatingOrDeleted(held) || !TryComp(held, out WFFloraGrabbedComponent? grabbed) || grabbed.Plant != ent.Owner)
            return;

        RemComp<WFFloraGrabbedComponent>(held);
        _speed.RefreshMovementSpeedModifiers(held);
        _alerts.ClearAlert(held, ent.Comp.GrabAlert);
        _popup.PopupEntity(Loc.GetString("wf-flora-released"), held, held);
        if (sound && !TerminatingOrDeleted(ent))
            _audio.PlayPvs(ent.Comp.ReleaseSound, ent);
    }

    private void Spit(Entity<WFReactiveFloraComponent> ent, EntityUid target)
    {
        if (ent.Comp.Projectile is not { } proto)
            return;

        var from = _transform.GetMapCoordinates(ent);
        var direction = _transform.GetWorldPosition(target) - from.Position;
        if (direction.LengthSquared() < 0.0001f)
            return;

        // Start just outside the plant so the shot doesn't hit it.
        var start = from.Offset(direction.Normalized() * 0.4f);
        var shot = Spawn(proto, start);
        _gun.ShootProjectile(shot, direction, Vector2.Zero, ent.Owner, ent.Owner, ent.Comp.ProjectileSpeed);
        Notify(ent, target);
    }

    /// <summary>A cloud of the solution on the plant's tile, if the tile can hold one.</summary>
    private void ReleaseCloud(EntityUid uid, Solution? solution)
    {
        if (solution == null || !TryComp(uid, out WFReactiveFloraComponent? flora))
            return;

        var xform = Transform(uid);
        if (xform.GridUid is not { } grid || !_turf.TryGetTileRef(xform.Coordinates, out var tile) || tile.Value.Tile.IsEmpty)
            return;
        if (_spreader.RequiresFloorToSpread(flora.CloudPrototype.ToString()) && _turf.IsSpace(tile.Value))
            return;

        var coords = _map.MapToGrid(grid, _transform.GetMapCoordinates(uid, xform));
        var cloud = Spawn(flora.CloudPrototype, coords.SnapToGrid());
        if (!TryComp<SmokeComponent>(cloud, out var smoke))
        {
            Log.Error($"{flora.CloudPrototype} has no SmokeComponent");
            Del(cloud);
            return;
        }
        _smoke.StartSmoke(cloud, solution.Clone(), flora.CloudDuration, flora.CloudSpread, smoke);
    }

    /// <summary>Starts an Explode plant swelling.</summary>
    private void Arm(Entity<WFReactiveFloraComponent> ent)
    {
        SetState(ent, WFReactiveFloraState.Armed);
        ent.Comp.StateEnd = _timing.CurTime + ent.Comp.ArmDelay;
        _audio.PlayPvs(ent.Comp.ArmSound, ent);
        if (ent.Comp.ArmPopup is { } popup)
            _popup.PopupEntity(Loc.GetString(popup), ent, PopupType.MediumCaution);
    }

    /// <summary>Bursts an Explode plant: hurts mobs near it, spills and spawns its remains, then dies or goes spent.</summary>
    public void Burst(Entity<WFReactiveFloraComponent> ent)
    {
        var flora = ent.Comp;
        if (flora.State == WFReactiveFloraState.Spent || TerminatingOrDeleted(ent))
            return;

        // Set first: the damage below can come back round through OnDamaged.
        SetState(ent, WFReactiveFloraState.Spent);
        flora.StateEnd = _timing.CurTime + flora.RegrowTime;
        var xform = Transform(ent);
        var coords = xform.Coordinates;

        _audio.PlayPvs(flora.Sound, coords);
        if (flora.BurstDamage != null && xform.MapUid != null)
        {
            FindTargets(ent, xform, flora.BurstRadius);
            foreach (var (target, _) in _targets)
                _damage.TryChangeDamage(target, flora.BurstDamage, origin: ent);
            PopupTargets(ent, flora.Popup);
        }

        if (flora.BurstSpill != null)
            _puddle.TrySpillAt(coords, flora.BurstSpill.Clone(), out _, false);
        foreach (var spawn in flora.BurstSpawns)
            Spawn(spawn, coords);
        ReleaseCloud(ent, flora.Solution);
        if (flora.ExplosionType != null)
        {
            _explosion.QueueExplosion(ent, flora.ExplosionType, flora.ExplosionIntensity, flora.ExplosionSlope,
                flora.ExplosionMaxTileIntensity, tileBreakScale: 0f, maxTileBreak: 0, canCreateVacuum: false);
        }

        if (flora.DeleteOnBurst)
            QueueDel(ent);
    }

    private void Notify(Entity<WFReactiveFloraComponent> ent, EntityUid target)
    {
        if (ent.Comp.Popup is { } popup)
            _popup.PopupEntity(Loc.GetString(popup, ("plant", ent.Owner)), target, target, PopupType.MediumCaution);
    }

    private void PopupTargets(Entity<WFReactiveFloraComponent> ent, LocId? popup)
    {
        if (popup == null)
            return;
        foreach (var (target, _) in _targets)
            _popup.PopupEntity(Loc.GetString(popup.Value, ("plant", ent.Owner)), target, target, PopupType.MediumCaution);
    }

    private void SetState(Entity<WFReactiveFloraComponent> ent, WFReactiveFloraState state)
    {
        ent.Comp.State = state;
        if (state != WFReactiveFloraState.Idle)
            _busy.Add(ent);
        _appearance.SetData(ent, WFReactiveFloraVisuals.State, state);
    }
}
