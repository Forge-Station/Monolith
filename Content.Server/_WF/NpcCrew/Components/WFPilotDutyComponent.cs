using Content.Server._WF.NpcCrew.Systems;
using Content.Shared._WF.NpcCrew;
using Robust.Shared.Map;

namespace Content.Server._WF.NpcCrew.Components;

/// <summary>
/// A crewman that can fly the ship: the helm it takes and the orders it flies once there. Worked by
/// <c>WFPilotDutySystem</c> when the crewman's duty is Pilot.
/// </summary>
[RegisterComponent]
public sealed partial class WFPilotDutyComponent : Component
{
    /// <summary>Independent flight limits supplied by a mission or scenario.</summary>
    [DataField]
    public WFCrewNavigationSettings Navigation = new();

    /// <summary>Orbit task whose live profile speed and minimum radius apply.</summary>
    public WFCrewObjectiveKind OrbitKind = WFCrewObjectiveKind.Loiter;
    public float RequestedLoiterRadius = 60f;
    public float? LoiterSpeedOverride;
    public float EscortSpacing = 100f;

    /// <summary>Flight order resumed after physically clearing the current docks.</summary>
    public WFPilotOrder? ResumeOrder;
    public List<EntityCoordinates> ResumeWaypoints = new();
    /// <summary>Target-relative formation slot retained until the escort order changes.</summary>
    public System.Numerics.Vector2? EscortOffset;
    public int EscortSlot;

    /// <summary>Fixed station captured when Hold is ordered, with hysteresis for drift correction.</summary>
    public EntityCoordinates? HoldPosition;
    public Angle HoldHeading;

    /// <summary>Without a captain aboard, whether this pilot evades attacking ships on their own.</summary>
    [DataField]
    public bool ReactToAttacks = true;

    /// <summary>Hold: turns the hull in place to put its guns on attackers or zone intruders; never chases.</summary>
    [DataField]
    public bool FaceThreats;

    /// <summary>A heading the steerer holds whatever the bearing to its target; set while close to an escort slot.</summary>
    public Angle? HeadingOverride;
    public bool CorrectingHold;

    /// <summary>Assigned helm. Null takes the nearest powered shuttle console on the crewman's grid.</summary>
    [DataField]
    public EntityUid? Console;

    [DataField]
    public WFPilotOrder Orders = WFPilotOrder.Hold;

    /// <summary>GoTo: the points flown in turn.</summary>
    [DataField]
    public List<EntityCoordinates> Waypoints = new();

    /// <summary>GoTo: the waypoint being flown to.</summary>
    [DataField]
    public int WaypointIndex;

    /// <summary>Loiter: the point circled.</summary>
    [DataField]
    public EntityCoordinates? LoiterCenter;

    [DataField]
    public float LoiterRadius = 60f;

    /// <summary>Orbit speed allowance above the circled grid's world speed, in m/s.</summary>
    [DataField]
    public float LoiterSpeed = 6f;

    /// <summary>Follow: the grid kept in range.</summary>
    [DataField]
    public EntityUid? FollowTarget;

    [DataField]
    public float FollowRange = 150f;

    /// <summary>Maximum cruise speed, in m/s; formation flight also accounts for the leader's velocity.</summary>
    [DataField]
    public float CruiseSpeed { get => Navigation.CruiseSpeed; set => Navigation.CruiseSpeed = value; }

    /// <summary>GoTo: how close to a waypoint counts as reaching it.</summary>
    [DataField]
    public float ArrivalRange = 60f;

    /// <summary>Dock: the grid docked with.</summary>
    [DataField]
    public EntityUid? DockTarget;

    /// <summary>
    /// Dock: how far out from the target dock the approach ends and the creep starts. Undock: how far to back off.
    /// </summary>
    [DataField]
    public float DockStandoff { get => Navigation.DockStandoff; set => Navigation.DockStandoff = value; }

    /// <summary>Dock: maximum approach speed, in m/s.</summary>
    [DataField]
    public float DockApproachSpeed { get => Navigation.DockApproachSpeed; set => Navigation.DockApproachSpeed = value; }

    /// <summary>Dock: maximum final approach speed, in m/s.</summary>
    [DataField]
    public float DockCreepSpeed { get => Navigation.DockCreepSpeed; set => Navigation.DockCreepSpeed = value; }

    /// <summary>Dock: seconds of creeping without the docks lining up before the attempt is abandoned.</summary>
    [DataField]
    public float DockCreepTimeout { get => Navigation.DockCreepTimeout; set => Navigation.DockCreepTimeout = value; }

    /// <summary>Dock: hand-flown attempts before giving up. Zero goes straight to the FTL fallback, or holds.</summary>
    [DataField]
    public int DockMaxAttempts = 3;

    /// <summary>Dock: the phase being flown.</summary>
    [ViewVariables]
    public WFDockPhase DockPhase;

    /// <summary>Dock: attempts failed so far.</summary>
    [ViewVariables]
    public int DockAttempts;

    /// <summary>Dock: seconds spent in the current phase.</summary>
    [ViewVariables]
    public float DockPhaseTime;

    /// <summary>Dock: seconds the current approach may take, its timeout plus the flight to the standoff.</summary>
    [ViewVariables]
    public float DockApproachBudget;

    /// <summary>Dock: the chosen dock pair and the poses flown to.</summary>
    [ViewVariables]
    public WFDockPlan? DockPlan;

    /// <summary>Dock: the grid the ship hit during the creep since the last update.</summary>
    [ViewVariables]
    public EntityUid? DockCollision;

    /// <summary>Whether the crewman holds the helm and is steering.</summary>
    [ViewVariables]
    public bool AtHelm;

    /// <summary>Whether the last GoTo, Dock or Undock was flown to its end.</summary>
    [ViewVariables]
    public bool OrdersCompleted;

    /// <summary>Undock: seconds to wait for crew posted to this ship who are off it before leaving without them.</summary>
    [DataField]
    public float AbsentCrewWait = 60f;

    /// <summary>Undock: seconds waited so far for absent crew.</summary>
    [ViewVariables]
    public float AbsentCrewWaited;

    /// <summary>Undock: whether crew were off the ship at the last check, re-read once a second.</summary>
    public bool CrewAway;
    public TimeSpan NextAbsentCheck;

    /// <summary>Whether any of the ship's docks was docked at the last check.</summary>
    [ViewVariables]
    public bool Docked;
    public TimeSpan NextDockCheck;

    /// <summary>Whether the held helm was in reach and unobstructed at the last check.</summary>
    public bool HelmInReach;
    public TimeSpan NextReachCheck;

    /// <summary>The map the crewman was on at the last update; a change re-issues map-bound orders.</summary>
    public MapId LastMap = MapId.Nullspace;

    /// <summary>Whether the grid the current order flies to is on another map, so the ship waits in place.</summary>
    [ViewVariables]
    public bool AwaitingTarget;

    /// <summary>The escorted grid's console taken as its forward, cached for <see cref="LeaderConsoleOf"/>.</summary>
    public EntityUid? LeaderConsole;
    public EntityUid? LeaderConsoleOf;
    public TimeSpan NextLeaderConsoleCheck;
}
