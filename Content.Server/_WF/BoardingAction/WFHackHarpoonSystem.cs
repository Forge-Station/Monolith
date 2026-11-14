using System.Numerics;
using Content.Server._Mono.FireControl;
using Content.Server._Mono.Projectiles.TargetGuided;
using Content.Server._Mono.Projectiles.TargetSeeking;
using Content.Server._WF.BoardingAction.Components;
using Content.Server._WF.Tether;
using Content.Server.Power.EntitySystems;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared._WF.BoardingAction;
using Content.Shared._WF.Tether;
using Content.Shared._WF.Tether.Harpoon;
using Content.Shared.Administration.Logs;
using Content.Shared.Database;
using Content.Shared.Popups;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Server.Audio;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.BoardingAction;

/// <summary>
/// A hacking harpoon in a hull. FTL drops on the latch; after a delay the ship is held in park and its guns and missiles
/// can't be laid on the hacker. Until the cable is gone none of the hacker's own ship guns can fire.
/// </summary>
public sealed partial class WFHackHarpoonSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _protos = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private ISharedAdminLogManager _adminLog = default!;
    [Dependency] private AudioSystem _audio = default!;
    [Dependency] private PowerReceiverSystem _power = default!;
    [Dependency] private RopeSystem _rope = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private ShuttleSystem _shuttle = default!;
    [Dependency] private TargetSeekingSystem _seeking = default!;
    [Dependency] private WFBoardingHelmSystem _helm = default!;
    [Dependency] private WFFtlDisruptionSystem _disruption = default!;

    /// <summary>How far past a gun's aim point a refused shot is followed, in metres.</summary>
    private const float ShotReach = 4000f;

    /// <summary>Clearance kept around a locked-out hull, in metres.</summary>
    private const float HullMargin = 2f;

    /// <summary>How close to a locked-out hull a guided missile may be steered, in metres.</summary>
    private const float GuidanceMargin = 16f;

    /// <summary>How long a gunner waits between being told why their shots are refused.</summary>
    private static readonly TimeSpan GunnerWarningCooldown = TimeSpan.FromSeconds(2);

    private readonly List<EntityUid> _ended = new();
    private readonly HashSet<EntityUid> _docked = new();
    private readonly List<WFHackSourceGridComponent> _sources = new();
    private readonly Dictionary<EntityUid, TimeSpan> _gunnerWarned = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFHackHarpoonGunComponent, RopeAttachedEvent>(OnCableTied);
        SubscribeLocalEvent<WFHackHarpoonGunComponent, RopeDetachedEvent>(OnCableGone);
        SubscribeLocalEvent<WFHackHarpoonGunComponent, ComponentShutdown>(OnGunShutdown);
        SubscribeLocalEvent<FireControllableComponent, ShotAttemptedEvent>(OnShipGunShot);
    }

    /// <summary>The hack itself starts on the next tick, with the rope finished and the dart settled in the hull.</summary>
    private void OnCableTied(Entity<WFHackHarpoonGunComponent> gun, ref RopeAttachedEvent args)
    {
        gun.Comp.Cable = args.Rope;
        gun.Comp.Dart = args.Other;
    }

    private void OnCableGone(Entity<WFHackHarpoonGunComponent> gun, ref RopeDetachedEvent args)
    {
        if (gun.Comp.Cable == args.Rope)
            EndHack(gun);
    }

    private void OnGunShutdown(Entity<WFHackHarpoonGunComponent> gun, ref ComponentShutdown args)
    {
        EndHack(gun);
    }

    /// <summary>Stops every ship gun on a hacker or docked to one, and the guns of a locked-down ship from firing across the hacker's hull.</summary>
    private void OnShipGunShot(Entity<FireControllableComponent> gun, ref ShotAttemptedEvent args)
    {
        if (RefusedShot(gun, args.Used.Comp.ShootCoordinates) != null)
            args.Cancel();
    }

    /// <summary>The string saying why a ship gun can't take a shot, or null if it can.</summary>
    private string? RefusedShot(EntityUid gun, EntityCoordinates? aim)
    {
        if (Transform(gun).GridUid is not { } grid)
            return null;

        FindSources(grid);
        if (_sources.Count > 0)
            return "wf-boarding-gunner-locked";

        if (!TryComp<WFHackedGridComponent>(grid, out var hacked) || aim is not { } target)
            return null;

        var from = _transform.GetWorldPosition(gun);
        var to = _transform.ToMapCoordinates(target).Position;
        foreach (var ship in hacked.LockedOut)
        {
            if (CrossesHull(from, to, ship))
                return "wf-boarding-gunner-lockout";
        }

        return null;
    }

    /// <summary>Tells a gunner why the guns they just fired stay silent, and returns whether it did.</summary>
    public bool WarnGunner(EntityUid console, EntityUid actor, List<NetEntity> weapons, NetCoordinates aim)
    {
        var now = _timing.CurTime;
        if (_gunnerWarned.TryGetValue(actor, out var next) && now < next)
            return false;

        var target = GetCoordinates(aim);
        foreach (var weapon in weapons)
        {
            if (!TryGetEntity(weapon, out var gun) || RefusedShot(gun.Value, target) is not { } reason)
                continue;

            _gunnerWarned[actor] = now + GunnerWarningCooldown;
            _popup.PopupEntity(Loc.GetString(reason), console, actor, PopupType.Medium);
            return true;
        }

        return false;
    }

    /// <summary>Collects the ships with a cable out among a ship and everything docked to it.</summary>
    private void FindSources(EntityUid ship)
    {
        _sources.Clear();
        var any = EntityQueryEnumerator<WFHackSourceGridComponent>();
        if (!any.MoveNext(out _, out _))
            return;

        _docked.Clear();
        _disruption.GetDockedShips(ship, _docked);
        foreach (var docked in _docked)
        {
            if (TryComp<WFHackSourceGridComponent>(docked, out var source))
                _sources.Add(source);
        }
    }

    /// <summary>Whether a shot from one point through another runs into a ship's bounds.</summary>
    private bool CrossesHull(Vector2 from, Vector2 through, EntityUid ship)
    {
        if (!TryComp<MapGridComponent>(ship, out var grid))
            return false;

        var delta = through - from;
        var length = delta.Length();
        if (length < 0.001f)
            return false;

        var world = _transform.GetInvWorldMatrix(ship);
        var origin = Vector2.Transform(from, world);
        var direction = Vector2.TransformNormal(delta / length, world);
        return WFBoardingMath.RayHitsBox(origin, direction, ShotReach, grid.LocalAABB.Enlarged(HullMargin));
    }

    /// <summary>Whether a hull's locked-down crew can't target a ship with its seekers.</summary>
    public bool IsLockedOut(EntityUid? firingGrid, EntityUid ship)
    {
        return TryComp<WFHackedGridComponent>(firingGrid, out var hacked) && hacked.LockedOut.Contains(ship);
    }

    /// <summary>Whether a guided missile from a locked-down ship is being steered at the hacker.</summary>
    public bool RefusesGuidance(EntityUid missile, EntityCoordinates aim)
    {
        if (!TryComp<ProjectileComponent>(missile, out var projectile) ||
            !TryComp(projectile.Shooter, out TransformComponent? shooter) ||
            !TryComp<WFHackedGridComponent>(shooter.GridUid, out var hacked))
            return false;

        var point = _transform.ToMapCoordinates(aim).Position;
        foreach (var ship in hacked.LockedOut)
        {
            if (_disruption.DistanceToHull(point, ship) <= GuidanceMargin)
                return true;
        }

        return false;
    }

    /// <summary>The ship a cable from this ship, or anything docked to it, is in.</summary>
    public EntityUid? CableTarget(EntityUid ship)
    {
        FindSources(ship);
        foreach (var source in _sources)
        {
            foreach (var gun in source.Guns)
            {
                if (TryComp<WFHackHarpoonGunComponent>(gun, out var comp) && comp.Target is { } target)
                    return target;
            }
        }

        return null;
    }

    /// <summary>Lets go of every cable a ship, or anything docked to it, has out.</summary>
    public void ReleaseCables(EntityUid ship)
    {
        FindSources(ship);
        _ended.Clear();
        foreach (var source in _sources)
        {
            foreach (var gun in source.Guns)
            {
                if (TryComp<WFHackHarpoonGunComponent>(gun, out var comp) && comp.Cable is { } cable)
                    _ended.Add(cable);
            }
        }

        foreach (var cable in _ended)
        {
            _rope.BreakRope(cable);
        }
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var now = _timing.CurTime;
        _ended.Clear();
        var query = EntityQueryEnumerator<WFHackHarpoonGunComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var gun, out var xform))
        {
            if (gun.Cable is not { } cable)
                continue;

            var ent = new Entity<WFHackHarpoonGunComponent>(uid, gun);
            if (gun.Target == null ? !TryLatch(ent, xform) : !StillLatched(ent, xform) ||
                gun.MaxDuration > TimeSpan.Zero && now >= gun.Latched + gun.MaxDuration)
            {
                _ended.Add(cable);
                if (gun.Dart is { } dart && HostGone(dart))
                    QueueDel(dart);

                continue;
            }

            PayOut(ent, cable);
            if (!gun.Lockdown && now >= gun.Latched + gun.LockdownDelay)
                LandLockdown(ent);

            ArcEngines(ent, now);
        }

        foreach (var cable in _ended)
        {
            _rope.BreakRope(cable);
        }
    }

    /// <summary>Starts the hack if the dart is in a ship that can be hacked.</summary>
    private bool TryLatch(Entity<WFHackHarpoonGunComponent> gun, TransformComponent xform)
    {
        if (gun.Comp.Dart is not { } dart || TerminatingOrDeleted(dart) || xform.GridUid is not { } source ||
            Transform(dart).GridUid is not { } target || target == source || !_disruption.IsTackleable(target))
            return false;

        var now = _timing.CurTime;
        gun.Comp.Target = target;
        gun.Comp.Source = source;
        gun.Comp.Latched = now;
        gun.Comp.NextArc = now;
        gun.Comp.Lockdown = false;
        var hacked = EnsureComp<WFHackedGridComponent>(target);
        hacked.Hackers[gun] = source;
        if (hacked.Engines.Count == 0)
            FindEngines(target, hacked);

        EnsureComp<WFHackSourceGridComponent>(source).Guns.Add(gun);
        _disruption.Disrupt(target, gun);
        SoundAlarm(gun, target);
        _adminLog.Add(LogType.Action, LogImpact.Medium,
            $"{ToPrettyString(source):source} latched a hacking cable into {ToPrettyString(target):target}");

        _helm.Refresh(source);
        _helm.Refresh(target);
        return true;
    }

    /// <summary>
    /// Whether the gun can still work the dart: powered, on the ship it fired from, the dart still in a hull that can
    /// be held. A ship that docks to a station or protected grid slips the hack.
    /// </summary>
    private bool StillLatched(Entity<WFHackHarpoonGunComponent> gun, TransformComponent xform)
    {
        return xform.GridUid == gun.Comp.Source && _power.IsPowered(gun.Owner) && gun.Comp.Dart is { } dart &&
               !TerminatingOrDeleted(dart) && Transform(dart).GridUid == gun.Comp.Target &&
               TryComp<ShipHarpoonComponent>(dart, out var harpoon) && harpoon.Embedded && !HostGone(dart) &&
               gun.Comp.Target is { } target && _disruption.IsTackleable(target);
    }

    /// <summary>Whether what the dart was sunk into has been destroyed, leaving it loose on the hull.</summary>
    private bool HostGone(EntityUid dart)
    {
        return TryComp<ShipHarpoonComponent>(dart, out var harpoon) && harpoon.Embedded &&
               TryComp<EmbeddableProjectileComponent>(dart, out var embedded) && embedded.EmbeddedIntoUid == null;
    }

    /// <summary>Keeps the cable slack as the ships move, up to its full length. Past that it parts on its own.</summary>
    private void PayOut(Entity<WFHackHarpoonGunComponent> gun, EntityUid cable)
    {
        if (gun.Comp.Dart is not { } dart || !TryComp<RopeComponent>(cable, out var rope) ||
            !_protos.TryIndex(rope.RopeType, out var type))
            return;

        var distance = Vector2.Distance(_rope.GetAnchorPosition(gun), _rope.GetAnchorPosition(dart));
        var wanted = MathF.Min(distance + gun.Comp.CableSpare, type.MaxLength);
        if (MathF.Abs(wanted - rope.Length) > gun.Comp.CableSpare / 4f)
            _rope.SetLength(cable, wanted);
    }

    /// <summary>The rest of the hack lands: the ship parks, and whatever its missiles hold on the hacker lets go.</summary>
    private void LandLockdown(Entity<WFHackHarpoonGunComponent> gun)
    {
        if (gun.Comp.Target is not { } target || gun.Comp.Source is not { } source ||
            !TryComp<WFHackedGridComponent>(target, out var hacked))
            return;

        gun.Comp.Lockdown = true;
        hacked.LockedOut.Add(source);
        _shuttle.WfSetLockdownDamping(target, true);
        SoundAlarm(gun, target);

        var seekers = EntityQueryEnumerator<TargetSeekingComponent, ProjectileComponent>();
        while (seekers.MoveNext(out var uid, out var seeker, out var projectile))
        {
            if (seeker.CurrentTarget == source && TryComp(projectile.Shooter, out TransformComponent? shooter) &&
                shooter.GridUid == target)
                _seeking.SetSeekerTarget((uid, seeker), null);
        }

        var guided = EntityQueryEnumerator<TargetGuidedComponent>();
        while (guided.MoveNext(out var uid, out var missile))
        {
            if (missile.TargetPosition is { } aim && RefusesGuidance(uid, aim))
                missile.TargetPosition = null;
        }

        _helm.Refresh(target);
    }

    private void FindEngines(EntityUid ship, WFHackedGridComponent hacked)
    {
        var query = EntityQueryEnumerator<ThrusterComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.GridUid == ship)
                hacked.Engines.Add(uid);
        }
    }

    /// <summary>
    /// Starts a looping arc on one more of the hacked ship's engines each tick, which keeps them from all pulsing
    /// together, and throws a burst of sparks off a random engine now and then.
    /// </summary>
    private void ArcEngines(Entity<WFHackHarpoonGunComponent> gun, TimeSpan now)
    {
        if (!TryComp<WFHackedGridComponent>(gun.Comp.Target, out var hacked) || hacked.Engines.Count == 0)
            return;

        if (hacked.EnginesArcing < hacked.Engines.Count)
        {
            var engine = hacked.Engines[hacked.EnginesArcing++];
            if (!TerminatingOrDeleted(engine))
            {
                var arc = Spawn(gun.Comp.ArcEffect, new EntityCoordinates(engine, Vector2.Zero));
                _transform.SetLocalRotation(arc, _random.NextAngle());
                hacked.Arcs.Add(arc);
            }
        }

        if (now < gun.Comp.NextArc)
            return;

        gun.Comp.NextArc = now + gun.Comp.ArcInterval;
        var sparking = _random.Pick(hacked.Engines);
        if (TerminatingOrDeleted(sparking))
            return;

        var coordinates = Transform(sparking).Coordinates;
        Spawn(gun.Comp.SparkEffect, coordinates);
        _audio.PlayPvs(gun.Comp.ArcSound, coordinates);
    }

    private void SoundAlarm(Entity<WFHackHarpoonGunComponent> gun, EntityUid ship)
    {
        var query = EntityQueryEnumerator<ShuttleConsoleComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.GridUid == ship)
                _audio.PlayPvs(gun.Comp.AlarmSound, uid);
        }
    }

    /// <summary>Undoes everything the hack did to both ships. The target stays parked until its pilot says otherwise.</summary>
    private void EndHack(Entity<WFHackHarpoonGunComponent> gun)
    {
        var target = gun.Comp.Target;
        var source = gun.Comp.Source;
        gun.Comp.Cable = null;
        gun.Comp.Dart = null;
        gun.Comp.Target = null;
        gun.Comp.Source = null;
        gun.Comp.Lockdown = false;
        _gunnerWarned.Clear();

        if (target is { } hull)
        {
            _disruption.Release(hull, gun);
            if (TryComp<WFHackedGridComponent>(hull, out var hacked))
            {
                var wasLocked = hacked.LockedOut.Count > 0;
                hacked.Hackers.Remove(gun);
                hacked.LockedOut.Clear();
                foreach (var (other, ship) in hacked.Hackers)
                {
                    if (TryComp<WFHackHarpoonGunComponent>(other, out var comp) && comp.Lockdown)
                        hacked.LockedOut.Add(ship);
                }

                if (wasLocked && hacked.LockedOut.Count == 0)
                    _shuttle.WfSetLockdownDamping(hull, false);

                if (hacked.Hackers.Count == 0)
                {
                    foreach (var arc in hacked.Arcs)
                    {
                        if (!TerminatingOrDeleted(arc))
                            QueueDel(arc);
                    }

                    RemComp<WFHackedGridComponent>(hull);
                }
            }
        }

        if (source is { } ship2 && TryComp<WFHackSourceGridComponent>(ship2, out var hacker))
        {
            hacker.Guns.Remove(gun);
            if (hacker.Guns.Count == 0)
                RemComp<WFHackSourceGridComponent>(ship2);
        }

        _helm.Refresh(target);
        _helm.Refresh(source);
    }
}
