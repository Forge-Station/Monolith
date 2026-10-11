using System.Linq;
using System.Numerics;
using Content.Server._Mono.FireControl;
using Content.Server._Mono.NPC.HTN;
using Content.Server._WF.NpcCrew.Components;
using Content.Server._WF.Cockpit;
using Content.Server._WF.NpcCrew.HTN;
using Content.Server.NPC.HTN;
using Content.Server.Power.EntitySystems;
using Content.Shared._Mono.FireControl;
using Content.Shared._WF.NpcCrew;
using Content.Shared.Interaction;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC.Systems;
using Content.Shared.Weapons.Hitscan.Components;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;
using Robust.Shared.Player;
using Robust.Shared.Spawners;
using Robust.Shared.Timing;

namespace Content.Server._WF.NpcCrew.Systems;

/// <summary>Runs ship targeting only while a living gunner operates an available console.</summary>
public sealed partial class WFGunnerDutySystem : EntitySystem
{
    [Dependency] private WFCrewAlertSystem _alerts = default!;
    [Dependency] private Robust.Shared.Random.IRobustRandom _skillRandom = default!;
    [Dependency] private WFCrewObjectiveSystem _objectives = default!;
    [Dependency] private WFCrewSecuritySystem _security = default!;
    [Dependency] private WFCrewEscortSystem _escorts = default!;
    [Dependency] private WFCrewShipStatusSystem _status = default!;
    [Dependency] private WFCrewSystem _crew = default!;
    [Dependency] private ShipTargetingSystem _targeting = default!;
    [Dependency] private PowerReceiverSystem _power = default!;
    [Dependency] private MobStateSystem _mobs = default!;
    [Dependency] private NpcFactionSystem _factions = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private SharedUserInterfaceSystem _ui = default!;
    [Dependency] private SharedGunSystem _guns = default!;
    [Dependency] private IComponentFactory _factory = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private Robust.Shared.Physics.Systems.SharedPhysicsSystem _physics = default!;

    public const string ConsoleKey = "WFCrewGunneryConsole";
    public const string CoordinatesKey = "WFCrewGunneryCoordinates";

    private static readonly TimeSpan SelectionInterval = TimeSpan.FromSeconds(0.25);
    private static readonly TimeSpan ReachInterval = TimeSpan.FromSeconds(10);

    /// <summary>A target a little past the measured reach still counts, so one on the edge isn't dropped and picked up again.</summary>
    private const float ReachSlack = 1.1f;

    private readonly Dictionary<EntityUid, EntityUid> _occupants = new();
    private readonly Dictionary<EntityUid, (TimeSpan Next, EntityUid? Target)> _selections = new();
    private readonly HashSet<EntityUid> _driven = new();

    /// <summary>How often a gunner shares his guns out among his targets.</summary>
    private static readonly TimeSpan BatteryInterval = TimeSpan.FromSeconds(1);

    /// <summary>The most ships one gunner fires on at once, and the guns he wants on each before he takes on another.</summary>
    private const int MaxTargets = 4;
    private const int GunsPerTarget = 2;

    /// <summary>Per gunner: his guns, shared out among the ships he fires on. The first is the one he chose.</summary>
    private readonly Dictionary<EntityUid, (TimeSpan Next, List<(EntityUid Target, List<EntityUid> Guns)> Groups)> _batteries = new();

    public override void Initialize()
    {
        base.Initialize();
        UpdatesBefore.Add(typeof(ShipTargetingSystem));
        SubscribeLocalEvent<WFGunnerDutyComponent, ComponentShutdown>(OnDutyShutdown);
    }

    /// <summary>A gunner that is deleted mid-duty leaves no selection or console claim behind.</summary>
    private void OnDutyShutdown(Entity<WFGunnerDutyComponent> ent, ref ComponentShutdown args)
    {
        _batteries.Remove(ent);
        _selections.Remove(ent.Owner);
        if (ent.Comp.Console is { } console && _occupants.TryGetValue(console, out var holder) && holder == ent.Owner)
            _occupants.Remove(console);
    }

    /// <summary>Chooses the nearest powered, unoccupied gunnery console on the crewman's grid.</summary>
    public bool TryFindConsole(EntityUid mob, out EntityUid console)
    {
        console = default;
        if (Transform(mob).GridUid is not { } grid)
            return false;
        var best = float.MaxValue;
        var query = EntityQueryEnumerator<FireControlConsoleComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var transform))
        {
            if (!Usable(uid, grid, mob))
                continue;
            var distance = (_transform.GetWorldPosition(transform) - _transform.GetWorldPosition(mob)).LengthSquared();
            if (distance >= best)
                continue;
            best = distance;
            console = uid;
        }
        return best < float.MaxValue;
    }

    private bool Usable(EntityUid console, EntityUid grid, EntityUid mob)
    {
        if (TerminatingOrDeleted(console) || !HasComp<FireControlConsoleComponent>(console)
            || Transform(console).GridUid != grid || !Transform(console).Anchored || !_power.IsPowered(console))
            return false;
        // Only an authorized operator at the screen takes the console from the gunner.
        foreach (var actor in _ui.GetActors(console, FireControlConsoleUiKey.Key)
                     .Concat(EntityManager.System<WFCockpitGunnerySystem>().GetActors(console)))
        {
            if (actor != mob && (_crew.SameCrew(mob, actor) || _security.IsAuthorized(mob, actor)))
                return false;
        }
        if (!_occupants.TryGetValue(console, out var other) || other == mob)
            return true;
        if (!TerminatingOrDeleted(other) && TryComp<WFGunnerDutyComponent>(other, out var duty)
            && duty.AtConsole && duty.Console == console)
            return false;
        _occupants.Remove(console);
        return true;
    }

    /// <summary>Takes a console after walking within normal interaction range.</summary>
    public bool TryTakeConsole(EntityUid mob, EntityUid console)
    {
        if (!TryComp<WFGunnerDutyComponent>(mob, out var duty) || Transform(mob).GridUid is not { } grid
            || !_mobs.IsAlive(mob) || HasComp<ActorComponent>(mob) || !Usable(console, grid, mob)
            || !_interaction.InRangeUnobstructed(mob, console))
            return false;
        if (duty.AtConsole && duty.Console is { } previous && previous != console
            && _occupants.TryGetValue(previous, out var holder) && holder == mob)
            _occupants.Remove(previous);
        duty.Console = console;
        duty.AtConsole = true;
        _occupants[console] = mob;
        EntityManager.System<WFCrewRoutineSystem>().Face(mob, console);
        EntityManager.System<WFCrewSpeechSystem>().Say(mob, "gunnery");
        return true;
    }

    /// <summary>Stops all targeting owned by this gunner and releases the console.</summary>
    public void Release(EntityUid mob)
    {
        _targeting.Stop(mob);
        _selections.Remove(mob);
        if (!TryComp<WFGunnerDutyComponent>(mob, out var duty))
            return;
        duty.AtConsole = false;
        if (duty.Console is { } console && _occupants.TryGetValue(console, out var holder) && holder == mob)
            _occupants.Remove(console);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var now = _timing.CurTime;
        _driven.Clear();
        var query = EntityQueryEnumerator<WFGunnerDutyComponent, WFCrewComponent, HTNComponent>();
        while (query.MoveNext(out var uid, out var duty, out var crew, out var htn))
        {
            if (!duty.AtConsole)
            {
                _targeting.Stop(uid);
                _selections.Remove(uid);
                _batteries.Remove(uid);
                continue;
            }
            if (crew.Duty != WFCrewDuties.Gunnery || !htn.Enabled || !_mobs.IsAlive(uid) || HasComp<ActorComponent>(uid)
                || duty.Console is not { } console || Transform(uid).GridUid is not { } grid
                || !Usable(console, grid, uid) || !_interaction.InRangeUnobstructed(uid, console)
                || htn.Plan is { } plan && !plan.Tasks.Any(task => task.Operator is WFTakeGunneryOperator))
            {
                Release(uid);
                continue;
            }
            if (now >= duty.NextReach)
            {
                duty.NextReach = now + ReachInterval;
                duty.Reach = GunReach(grid);
            }
            // Target selection runs four times a second; the chosen ship is kept in between while still engageable.
            var here = _transform.GetMapCoordinates(grid);
            if (!_selections.TryGetValue(uid, out var selection) || now >= selection.Next
                || selection.Target is { } kept
                    && Engageable(uid, grid, crew, duty, here, kept, _alerts.IsHostileShip(grid, crew.Group, kept)) == null)
            {
                selection = (now + SelectionInterval, SelectTarget(uid, grid, crew, duty, here));
                _selections[uid] = selection;
            }
            if (selection.Target is { } hostile && _driven.Add(grid))
            {
                // A less skilled gunner lays the guns off the target and leads it poorly.
                var skill = WFCrewSkills.Of(crew.Skill);
                if (_timing.CurTime >= duty.NextAimError)
                {
                    duty.NextAimError = _timing.CurTime + TimeSpan.FromSeconds(3);
                    duty.AimError = skill.GunneryError > 0f
                        ? _skillRandom.NextAngle().ToVec() * _skillRandom.NextFloat(skill.GunneryError)
                        : Vector2.Zero;
                }

                // A ship with guns to spare fires on several attackers at once, each gun on the one nearest it.
                if (!_batteries.TryGetValue(uid, out var battery) || now >= battery.Next || battery.Groups.Count == 0
                    || battery.Groups[0].Target != hostile)
                {
                    battery = (now + BatteryInterval, Allot(uid, grid, crew, duty, here, hostile));
                    _batteries[uid] = battery;
                }

                if (_targeting.Target(uid, new EntityCoordinates(hostile, Lay(grid, hostile, duty))) is { } aim)
                {
                    aim.LeadingAccuracy = skill.Leading;
                    aim.OffgridLeadingAccuracy = skill.Leading;
                    // The guns are this gunner's to share out: the targeting system is kept from taking them all back.
                    var cannons = aim.Cannons;
                    cannons.Clear();
                    if (battery.Groups.Count > 0)
                        cannons.AddRange(battery.Groups[0].Guns);
                    aim.WeaponCheckAccum = 3600f;
                }

                if (battery.Groups.Count > 1 && TryComp<PhysicsComponent>(grid, out var body))
                {
                    for (var i = 1; i < battery.Groups.Count; i++)
                    {
                        var (other, guns) = battery.Groups[i];
                        if (Engageable(uid, grid, crew, duty, here, other, true) == null)
                            continue;

                        var at = _transform.ToMapCoordinates(new EntityCoordinates(other, Lay(grid, other, duty)));
                        _targeting.FireWeapons(grid, guns, at, body.LinearVelocity, _physics.GetMapLinearVelocity(other) * skill.Leading, uid);
                    }
                }
            }
            else
            {
                _targeting.Stop(uid);
                _batteries.Remove(uid);
            }
        }
    }

    /// <summary>Where the guns are laid on a ship, in its own frame: off it by the gunner's error, and well clear for a warning shot.</summary>
    private Vector2 Lay(EntityUid grid, EntityUid target, WFGunnerDutyComponent duty)
    {
        return _alerts.IsWarningShot(grid, target) ? duty.AimError + WarningShotOffset : duty.AimError;
    }

    /// <summary>
    /// Shares a ship's guns out among the ships it may fire on: the gunner's chosen target first, then the nearest
    /// others, one more for every <see cref="GunsPerTarget"/> guns up to <see cref="MaxTargets"/>. Each gun takes
    /// the target nearest it, so a ship beset from both sides answers both. A ship with few guns keeps them together.
    /// </summary>
    private List<(EntityUid Target, List<EntityUid> Guns)> Allot(EntityUid uid, EntityUid grid, WFCrewComponent crew,
        WFGunnerDutyComponent duty, MapCoordinates here, EntityUid primary)
    {
        var guns = new List<(EntityUid Gun, Vector2 At)>();
        var mounted = EntityQueryEnumerator<FireControllableComponent, TransformComponent>();
        while (mounted.MoveNext(out var gun, out _, out var xform))
        {
            if (xform.GridUid == grid && xform.Anchored)
                guns.Add((gun, _transform.GetWorldPosition(xform)));
        }

        var targets = new List<(EntityUid Target, Vector2 At)> { (primary, _transform.GetWorldPosition(primary)) };
        var wanted = Math.Clamp(guns.Count / GunsPerTarget, 1, MaxTargets);
        if (wanted > 1)
        {
            var others = new List<(EntityUid Target, float Distance)>();
            foreach (var ship in _alerts.GetHostileShips(grid, crew.Group))
            {
                if (ship != primary && Engageable(uid, grid, crew, duty, here, ship, true) is { } distance)
                    others.Add((ship, distance));
            }

            others.Sort((a, b) => a.Distance.CompareTo(b.Distance));
            foreach (var (ship, _) in others)
            {
                if (targets.Count >= wanted)
                    break;
                targets.Add((ship, _transform.GetWorldPosition(ship)));
            }
        }

        var groups = new List<(EntityUid Target, List<EntityUid> Guns)>();
        foreach (var (target, _) in targets)
        {
            groups.Add((target, new List<EntityUid>()));
        }

        foreach (var (gun, at) in guns)
        {
            var nearest = 0;
            var best = float.MaxValue;
            for (var i = 0; i < targets.Count; i++)
            {
                var distance = (targets[i].At - at).LengthSquared();
                if (distance >= best)
                    continue;
                best = distance;
                nearest = i;
            }

            groups[nearest].Guns.Add(gun);
        }

        // The chosen target is never left without a gun: it takes one from the best armed of the others.
        if (groups[0].Guns.Count == 0 && guns.Count > 0)
        {
            var donor = 1;
            for (var i = 2; i < groups.Count; i++)
            {
                if (groups[i].Guns.Count > groups[donor].Guns.Count)
                    donor = i;
            }

            var spare = groups[donor].Guns;
            groups[0].Guns.Add(spare[^1]);
            spare.RemoveAt(spare.Count - 1);
        }

        groups.RemoveAll(group => group.Target != primary && group.Guns.Count == 0);
        return groups;
    }

    /// <summary>The ships a gunner is firing on right now, his chosen target first, with the guns on each.</summary>
    public IReadOnlyList<(EntityUid Target, List<EntityUid> Guns)> Batteries(EntityUid gunner)
    {
        return _batteries.TryGetValue(gunner, out var battery) ? battery.Groups : Array.Empty<(EntityUid, List<EntityUid>)>();
    }

    /// <summary>
    /// The nearest live threat in reach: an assigned target, or a ship that attacked, a hostile docker or a hostile
    /// faction. Ships that fired on this one come before intruders a patrol zone merely reported.
    /// </summary>
    private EntityUid? SelectTarget(EntityUid uid, EntityUid grid, WFCrewComponent crew, WFGunnerDutyComponent duty,
        MapCoordinates here)
    {
        EntityUid? best = null;
        var bestDistance = float.MaxValue;
        var bestZone = false;
        if (_objectives.AttackTarget(grid, crew.Group) is { } assigned
            && Engageable(uid, grid, crew, duty, here, assigned, _alerts.IsHostileShip(grid, crew.Group, assigned)) is { } first)
        {
            best = assigned;
            bestDistance = first;
        }
        foreach (var ship in _alerts.GetHostileShips(grid, crew.Group))
        {
            if (Engageable(uid, grid, crew, duty, here, ship, true) is not { } distance)
                continue;
            var zone = _alerts.IsZoneThreat(grid, crew.Group, ship);
            var better = best == null || (!zone && bestZone) || (zone == bestZone && distance < bestDistance);
            if (!better)
                continue;
            best = ship;
            bestDistance = distance;
            bestZone = zone;
        }
        return best;
    }

    /// <summary>How far off a vessel a warning shot is laid, in its own frame.</summary>
    private static readonly Vector2 WarningShotOffset = new(0f, 90f);

    /// <summary>Squared distance to a ship this gunner may fire on, or null when it must hold fire.</summary>
    private float? Engageable(EntityUid uid, EntityUid grid, WFCrewComponent crew, WFGunnerDutyComponent duty,
        MapCoordinates here, EntityUid ship, bool hostile)
    {
        if (ship == grid || TerminatingOrDeleted(ship))
            return null;
        // An assigned target starts out of range, so only its state releases the gunner, as for the pilot.
        var assigned = _objectives.IsAttackTarget(grid, crew.Group, ship);
        if (_status.ShouldDisengage(grid, ship, range: !assigned))
            return null;
        // Reported attackers stay targets even inside the formation for their attack window.
        if (!hostile && (_escorts.AreInFormation(grid, ship)
                || _factions.IsEntityFriendly(uid, ship) && !assigned
                    && !_security.IsHostileDockingTarget(grid, crew.Group, ship)))
            return null;
        var there = _transform.GetMapCoordinates(ship);
        if (there.MapId != here.MapId)
            return null;
        var distance = (there.Position - here.Position).LengthSquared();
        // Rounds that cannot reach the aim point are skipped by the targeting system, so don't hold a target past them.
        var limit = MathF.Min(duty.Range, duty.Reach * ReachSlack);
        return distance <= limit * limit ? distance : (float?) null;
    }

    /// <summary>The farthest any armed gun of the grid can hit, from its ammunition; unlimited when none can be measured.</summary>
    private float GunReach(EntityUid grid)
    {
        var reach = 0f;
        var guns = EntityQueryEnumerator<FireControllableComponent, GunComponent, TransformComponent>();
        while (guns.MoveNext(out var uid, out _, out var gun, out var xform))
        {
            if (xform.GridUid != grid || !xform.Anchored || !_guns.TryNextShootPrototype((uid, gun), out var proto))
                continue;

            float distance;
            if (proto.TryGetComponent<HitscanAmmoComponent>(out _, _factory))
            {
                distance = proto.TryGetComponent<HitscanBasicRaycastComponent>(out var raycast, _factory)
                    ? raycast.MaxDistance
                    : float.MaxValue;
            }
            else
            {
                distance = _guns.GetBulletPrototype(proto).TryGetComponent<TimedDespawnComponent>(out var despawn, _factory)
                    ? gun.ProjectileSpeedModified * despawn.Lifetime
                    : float.MaxValue;
            }

            reach = MathF.Max(reach, distance);
        }

        return reach > 0f ? reach : float.MaxValue;
    }
}
