using System.Numerics;
using Content.Shared._WF.Vehicles.Weapons;
using Robust.Shared.GameStates;

namespace Content.Shared._WF.Vehicles.Turret;

/// <summary>A turret or a gun on a turret: how it rotates and how its overlay is drawn.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(VehicleWeaponsSystem), typeof(VehicleTurretSystem), typeof(VehicleTurretMuzzleSystem))]
public sealed partial class VehicleTurretComponent : Component
{
    [DataField, AutoNetworkedField]
    public bool RotateToCursor = false;

    [DataField, AutoNetworkedField]
    public float FireWhileRotatingGraceDegrees = 0f;

    [DataField, AutoNetworkedField]
    public bool UseBarrelDirectionForShots = false;

    [DataField, AutoNetworkedField]
    public float MaxShotCurvatureDegrees = 0f;

    [DataField, AutoNetworkedField]
    public bool StabilizedRotation = true;

    [DataField, AutoNetworkedField]
    public float RotationSpeed = 0f;

    [DataField, AutoNetworkedField]
    public float ReverseDirectionDelay = 0.06f;

    [DataField, AutoNetworkedField]
    public float RotationInputDeadzoneDegrees = 1.5f;

    [DataField, AutoNetworkedField]
    public bool ShowOverlay = false;

    /// <summary>Draw order relative to the main turret; hull-mounted weapons sit below its barrel.</summary>
    [DataField, AutoNetworkedField]
    public int OverlayDepthOffset;

    /// <summary>Compensates a full-hull overlay whose painted pivot is away from the image centre.</summary>
    [DataField, AutoNetworkedField]
    public bool OffsetSpritePivot;

    [DataField, AutoNetworkedField]
    public bool OffsetRotatesWithTurret = false;

    [DataField, AutoNetworkedField]
    public Vector2 PixelOffset = Vector2.Zero;

    [DataField, AutoNetworkedField]
    public string OverlayRsi = string.Empty;

    [DataField, AutoNetworkedField]
    public string OverlayState = string.Empty;

    [DataField, AutoNetworkedField]
    public string OverlayDamagedState = string.Empty;

    [DataField, AutoNetworkedField]
    public bool UseDirectionalOffsets = false;

    [DataField, AutoNetworkedField]
    public Vector2 PixelOffsetNorth = Vector2.Zero;

    [DataField, AutoNetworkedField]
    public Vector2 PixelOffsetEast = Vector2.Zero;

    [DataField, AutoNetworkedField]
    public Vector2 PixelOffsetSouth = Vector2.Zero;

    [DataField, AutoNetworkedField]
    public Vector2 PixelOffsetWest = Vector2.Zero;

    [DataField, AutoNetworkedField]
    public Angle WorldRotation = Angle.Zero;

    [DataField, AutoNetworkedField]
    public Angle TargetRotation = Angle.Zero;

    [NonSerialized]
    public EntityUid? VisualEntity;

    [NonSerialized]
    public Angle? PendingTargetRotation;

    [NonSerialized]
    public TimeSpan PendingTargetApplyAt = TimeSpan.Zero;

    [NonSerialized]
    public int PendingDirectionSign = 0;

    [NonSerialized]
    public int LastAppliedDirectionSign = 0;

    [AutoNetworkedField]
    public Angle LastVehicleRotation;

    [AutoNetworkedField]
    public bool LastVehicleRotationValid;
}
