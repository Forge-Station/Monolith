using System;
using System.Numerics;
using Robust.Shared.GameStates;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Vehicles.Grid.Components;

/// <summary>Grid-locked mover state and tuning for a vehicle; <see cref="GridVehicleMoverSystem"/> rewrites the transform from it each step.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(GridVehicleMoverSystem), Other = AccessPermissions.ReadWrite)]
public sealed partial class GridVehicleMoverComponent : Component
{
    /// <summary>
    /// current tile occupied by the vehicle on its grid.
    /// </summary>
    [AutoNetworkedField]
    public Vector2i CurrentTile;

    /// <summary>
    /// target tile used by prediction & smoothing
    /// </summary>
    [AutoNetworkedField]
    public Vector2i TargetTile;

    /// <summary>
    /// current local grid position of the vehicle
    /// </summary>
    [AutoNetworkedField]
    public Vector2 Position;

    /// <summary>
    /// target local grid position used by prediction & smoothing
    /// </summary>
    [AutoNetworkedField]
    public Vector2 TargetPosition;

    /// <summary>
    /// current cardinal facing direction of the vehicle
    /// </summary>
    [AutoNetworkedField]
    public Vector2i CurrentDirection;

    /// <summary>
    /// current signed movement speed in grid units / second
    /// </summary>
    [AutoNetworkedField]
    public float CurrentSpeed;

    /// <summary>Grid-local velocity retained when traction cannot follow the chassis immediately.</summary>
    [AutoNetworkedField]
    public Vector2 TravelVelocity;

    /// <summary>Collision momentum integrated through the same swept clearance checks as driving.</summary>
    [AutoNetworkedField]
    public Vector2 ImpactVelocity;

    /// <summary>Collision angular velocity in degrees per second.</summary>
    [AutoNetworkedField]
    public float ImpactSpin;

    /// <summary>Lateral tire or track grip in tiles per second squared.</summary>
    [DataField]
    public float Grip = 12f;

    /// <summary>
    /// whether the vehicle steers like a car (throttle and steering, any heading) rather than snapping to the four directions
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool FreeSteering;

    /// <summary>
    /// heading in the grid's frame of a freely steered vehicle; valid once it has a facing
    /// </summary>
    [AutoNetworkedField]
    public Angle Heading;

    /// <summary>
    /// current signed chassis turn rate of a freely steered vehicle, in degrees / second
    /// </summary>
    [AutoNetworkedField]
    public float AngularVelocityDegrees;

    /// <summary>
    /// highest chassis turn rate while steering, in degrees / second
    /// </summary>
    [DataField, AutoNetworkedField]
    public float MaxRotationSpeedDegrees = 22.5f;

    /// <summary>
    /// chassis turn acceleration while steering, in degrees / second squared
    /// </summary>
    [DataField, AutoNetworkedField]
    public float RotationAccelerationDegrees = 45f;

    /// <summary>
    /// chassis turn deceleration once steering is released, in degrees / second squared
    /// </summary>
    [DataField, AutoNetworkedField]
    public float RotationDecelerationDegrees = 60f;

    /// <summary>
    /// largest angle off a cardinal that is straightened out for the driver at parking speed; zero turns the assist off
    /// </summary>
    [DataField, AutoNetworkedField]
    public float AlignmentAssistDegrees;

    /// <summary>
    /// highest speed at which the alignment assist acts
    /// </summary>
    [DataField, AutoNetworkedField]
    public float AlignmentAssistMaxSpeed = 1.5f;

    /// <summary>
    /// maximum forward driving speed in grid units / second
    /// </summary>
    [DataField, AutoNetworkedField]
    public float MaxSpeed = 11f;

    /// <summary>
    /// forward acceleration in grid units / second squared
    /// </summary>
    [DataField, AutoNetworkedField]
    public float Acceleration = 7f;

    /// <summary>
    /// speed loss per second when slowing down or stoping
    /// </summary>
    [DataField, AutoNetworkedField]
    public float Deceleration = 12f;

    /// <summary>
    /// maximum reverse driving speed in grid units / second
    /// </summary>
    [DataField, AutoNetworkedField]
    public float MaxReverseSpeed = 4f;

    /// <summary>
    /// reverse acceleration in grid units / second squared
    /// </summary>
    [DataField, AutoNetworkedField]
    public float ReverseAcceleration = 4f;

    /// <summary>
    /// maximum sideways lane offset used for normal lane correction
    /// </summary>
    [DataField, AutoNetworkedField]
    public float TileOffsetLimit = 1f;

    /// <summary>
    /// sideways lane offset sampling step for finding clear lanes
    /// </summary>
    [DataField, AutoNetworkedField]
    public float TileOffsetStep = 0.05f;

    /// <summary>
    /// number of tiles checked ahead when choosing a clear lane
    /// </summary>
    [DataField, AutoNetworkedField]
    public int TileOffsetLookahead = 3;

    /// <summary>
    /// maximum sideways correction speed in grid units / second
    /// </summary>
    [DataField, AutoNetworkedField]
    public float LaneCorrectionSpeed = 4f;

    /// <summary>
    /// distance between continuous collision probes while moving
    /// </summary>
    [DataField, AutoNetworkedField]
    public float MovementProbeStep = 0.1f;

    /// <summary>
    /// inset applied to non-mob movement collision checks
    /// </summary>
    [DataField, AutoNetworkedField]
    public float MovementCollisionInset = 0.05f;

    /// <summary>
    /// delay after changing facing direction
    /// </summary>
    [DataField, AutoNetworkedField]
    public float TurnDelay = 0.08f;

    /// <summary>
    /// whether the vehicle can rotate without moving forward
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool TurnInPlace = false;

    /// <summary>
    /// highest speed where in-place turning is allowed
    /// </summary>
    [DataField, AutoNetworkedField]
    public float TurnInPlaceMaxSpeed = 0.35f;

    /// <summary>
    /// maximum local nudge distance when finding room to turn
    /// </summary>
    [DataField, AutoNetworkedField]
    public float TurnNudgeLimit = 0.45f;

    /// <summary>
    /// local sampling step used when finding room to turn
    /// </summary>
    [DataField, AutoNetworkedField]
    public float TurnNudgeStep = 0.1f;

    /// <summary>
    /// forward grace distance used to clear transient turn blockers
    /// </summary>
    [DataField, AutoNetworkedField]
    public float TurnCollisionGraceDistance = 1f;

    /// <summary>
    /// next time this vehicle may turn
    /// </summary>
    [AutoNetworkedField]
    public TimeSpan NextTurnTime;

    /// <summary>
    /// time until movement is blocked after an in-place turn
    /// </summary>
    [AutoNetworkedField]
    public TimeSpan InPlaceTurnBlockUntil;

    /// <summary>
    /// whether movement has committed for the current simulation step
    /// </summary>
    [AutoNetworkedField]
    public bool IsCommittedToMove;

    /// <summary>
    /// whether the vehicle is currently moving
    /// </summary>
    [AutoNetworkedField]
    public bool IsMoving;

    [DataField, AutoNetworkedField]
    public VehicleWeightClass WeightClass = VehicleWeightClass.Weak;

    /// <summary>
    /// whether this vehicle can push other grid vehicles
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool CanPushVehicles = false;

    /// <summary>
    /// ram damage per (tile / second) squared that a fitted plow does to what it can break
    /// </summary>
    [DataField, AutoNetworkedField]
    public float PlowRamDamage = 75f;

    /// <summary>
    /// slowest contact at which a fitted plow rams; a plow chassis still grinds below it
    /// </summary>
    [DataField, AutoNetworkedField]
    public float PlowRamMinSpeed;

    /// <summary>
    /// least time between two plow hits on the same target, in seconds
    /// </summary>
    [DataField, AutoNetworkedField]
    public float PlowRamCooldown = 0.5f;

    /// <summary>
    /// grid that the mover state is currently synced to
    /// </summary>
    [NonSerialized]
    public EntityUid? SyncedGrid;

    /// <summary>
    /// rotation in the grid's frame that a vehicle with no facing yet is held to; follows deliberate turns, and is
    /// restored when physics spins the body
    /// </summary>
    [NonSerialized]
    public Angle? RestRotation;

    /// <summary>
    /// the steering switch the current driver was given
    /// </summary>
    [NonSerialized]
    public EntityUid? SteeringAction;

    /// <summary>
    /// earliest time the next impact is reported; a hull scraping along a wall meets it every step
    /// </summary>
    [NonSerialized]
    public TimeSpan NextImpactTime;

    /// <summary>
    /// number of fixed steps to skip turn nudge queries after a failed search
    /// </summary>
    [NonSerialized]
    public int TurnNudgeSkipSteps;

    /// <summary>
    /// desired facing direction that triggered the current turn nudge skip
    /// </summary>
    [NonSerialized]
    public Vector2i TurnNudgeCacheDir;

    /// <summary>
    /// active multiplier applied to movement speed after smashing objects
    /// </summary>
    [AutoNetworkedField]
    public float SmashSlowdownMultiplier = 1f;

    /// <summary>
    /// time when the active smash slowdown expires
    /// </summary>
    [AutoNetworkedField]
    public TimeSpan SmashSlowdownUntil;
}

/// <summary>How heavy a vehicle is; decides what it can smash or push through.</summary>
[Serializable, NetSerializable]
public enum VehicleWeightClass : byte
{
    Weak,
    Light,
    Medium,
    Heavy,
}
