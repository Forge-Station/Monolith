using System.Threading;
using Content.Shared._WF.NpcVehicleCrew;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.NpcVehicleCrew;

/// <summary>A vehicle driven and fought by an NPC crew: who sits where, what it is doing and where it is going.</summary>
[RegisterComponent, Access(typeof(WFVehicleCrewSystem))]
public sealed partial class WFVehicleCrewComponent : Component
{
    [ViewVariables]
    public ProtoId<WFVehicleCrewPrototype> Prototype;

    /// <summary>The crew group label shared with the crewmen's <c>WFCrewComponent.Group</c>.</summary>
    [ViewVariables]
    public string Group = string.Empty;

    [ViewVariables]
    public WFVehicleBehaviour Behaviour;

    /// <summary>What to go back to after a hunt, a flight or a retreat.</summary>
    [ViewVariables]
    public WFVehicleBehaviour PreviousBehaviour;

    /// <summary>Whether a target sends this vehicle hunting.</summary>
    [ViewVariables]
    public bool Pursues;

    [ViewVariables]
    public MapCoordinates Home;

    /// <summary>Patrol points, given or generated round home.</summary>
    [ViewVariables]
    public List<MapCoordinates> Waypoints = new();

    [ViewVariables]
    public int WaypointIndex;

    /// <summary>Where the patrol's current leg began: the last point it reached, or where it set out from.</summary>
    [ViewVariables]
    public MapCoordinates? PatrolFrom;

    /// <summary>The entity escorted.</summary>
    [ViewVariables]
    public EntityUid? Lead;

    [ViewVariables]
    public EntityUid? Target;

    /// <summary>Whether the gunner saw the target this gunnery tick.</summary>
    [ViewVariables]
    public bool TargetVisible;

    [ViewVariables]
    public MapCoordinates? LastSeenTarget;

    [ViewVariables]
    public TimeSpan LastSeenAt;

    /// <summary>When the current target was picked; the first shot waits the skill's delay from it.</summary>
    [ViewVariables]
    public TimeSpan AcquiredAt;

    /// <summary>Whoever hurt the vehicle or a crewman, with when they last did.</summary>
    [ViewVariables]
    public Dictionary<EntityUid, TimeSpan> Attackers = new();

    /// <summary>Strangers inside the guard radius, with when the horn warned them.</summary>
    [ViewVariables]
    public Dictionary<EntityUid, TimeSpan> Warned = new();

    [ViewVariables]
    public EntityUid? Driver;

    [ViewVariables]
    public EntityUid? Gunner;

    [ViewVariables]
    public List<EntityUid> Troopers = new();

    [ViewVariables]
    public EntityUid? DriverSeat;

    /// <summary>When a trooper takes the dead driver's seat; null while nobody is waiting to.</summary>
    [ViewVariables]
    public TimeSpan? ReseatAt;

    [ViewVariables]
    public bool Bailed;

    [ViewVariables]
    public bool OutOfAmmo;

    /// <summary>The mounted gun the gunner works.</summary>
    [ViewVariables]
    public EntityUid? Weapon;

    /// <summary>Whether that gun's turret could be laid on the target; false makes the driver turn the hull.</summary>
    [ViewVariables]
    public bool CanBear = true;

    [ViewVariables]
    public TimeSpan NextThink;

    [ViewVariables]
    public TimeSpan NextGunnery;

    /// <summary>When the vehicle was last hit, for a fleeing vehicle to calm down.</summary>
    [ViewVariables]
    public TimeSpan LastHit;

    /// <summary>Where the driver is headed; null stops it.</summary>
    [ViewVariables]
    public MapCoordinates? Goal;

    /// <summary>Where a stopped vehicle whose gun cannot traverse turns its hull to; null leaves it.</summary>
    [ViewVariables]
    public MapCoordinates? Face;

    /// <summary>The path to the goal, nearest point first.</summary>
    [ViewVariables]
    public List<MapCoordinates> Route = new();

    /// <summary>The goal the route was planned for.</summary>
    [ViewVariables]
    public MapCoordinates? RouteGoal;

    [ViewVariables]
    public TimeSpan NextRepath;

    [ViewVariables]
    public CancellationTokenSource? PathCancel;

    /// <summary>The point after the goal on a patrol that rolls on to it without stopping; null stops at the goal.</summary>
    [ViewVariables]
    public MapCoordinates? NextGoal;

    /// <summary>The route from the goal on to <see cref="NextGoal"/>, planned before the vehicle gets there.</summary>
    [ViewVariables]
    public List<MapCoordinates> NextRoute = new();

    /// <summary>The next goal <see cref="NextRoute"/> was planned for.</summary>
    [ViewVariables]
    public MapCoordinates? NextRouteGoal;

    [ViewVariables]
    public CancellationTokenSource? NextPathCancel;

    /// <summary>When throttle was held with the vehicle not moving; null while it moves.</summary>
    [ViewVariables]
    public TimeSpan? StuckSince;

    /// <summary>Until when the driver backs out of a stuck spot.</summary>
    [ViewVariables]
    public TimeSpan ReverseUntil;

    [ViewVariables]
    public float ReverseSteering;

    /// <summary>Stucks on the current leg; three skip it.</summary>
    [ViewVariables]
    public int StuckCount;

    /// <summary>Until when the vehicle waits at a patrol point.</summary>
    [ViewVariables]
    public TimeSpan PauseUntil;

    /// <summary>Burst discipline of automatic guns: firing until, then resting until.</summary>
    [ViewVariables]
    public TimeSpan BurstUntil;

    [ViewVariables]
    public TimeSpan RestUntil;
}

/// <summary>A crewman of an NPC vehicle: which vehicle, which seat and what he does there.</summary>
[RegisterComponent, Access(typeof(WFVehicleCrewSystem))]
public sealed partial class WFVehicleCrewMemberComponent : Component
{
    [ViewVariables]
    public EntityUid Vehicle;

    [ViewVariables]
    public EntityUid? Seat;

    [ViewVariables]
    public WFVehicleCrewRole Role;

    /// <summary>The HTN's player-distance wake range before he was mounted, put back when he dismounts.</summary>
    [ViewVariables]
    public float? SleepRange;
}

/// <summary>A crewman's place in an NPC vehicle.</summary>
public enum WFVehicleCrewRole : byte
{
    Driver,
    Gunner,
    Trooper,
}

/// <summary>A marker that spawns an NPC vehicle crew where it stands at map init, then deletes itself.</summary>
[RegisterComponent]
public sealed partial class WFVehicleCrewSpawnerComponent : Component
{
    [DataField(required: true)]
    public ProtoId<WFVehicleCrewPrototype> Crew;

    /// <summary>Overrides the crew's own behaviour.</summary>
    [DataField]
    public WFVehicleBehaviour? Behaviour;

    /// <summary>Crew group label; left out, a new one is made.</summary>
    [DataField]
    public string? Group;
}
