using Robust.Shared.GameStates;
using System.Numerics;

namespace Content.Shared._WF.Vehicles.Weapons;

/// <summary>Where the shot leaves a mounted gun, relative to what the gun sits in.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class GunMuzzleOffsetComponent : Component
{
    [DataField, AutoNetworkedField]
    public Vector2 Offset = Vector2.Zero;

    [DataField, AutoNetworkedField]
    public bool UseDirectionalOffsets = false;

    [DataField, AutoNetworkedField]
    public bool RotateDirectionalOffsets = false;

    [DataField, AutoNetworkedField]
    public Vector2 OffsetNorth = Vector2.Zero;

    [DataField, AutoNetworkedField]
    public Vector2 OffsetEast = Vector2.Zero;

    [DataField, AutoNetworkedField]
    public Vector2 OffsetSouth = Vector2.Zero;

    [DataField, AutoNetworkedField]
    public Vector2 OffsetWest = Vector2.Zero;

    [DataField, AutoNetworkedField]
    public Vector2 MuzzleOffset = Vector2.Zero;

    [DataField, AutoNetworkedField]
    public Angle AngleOffset = Angle.Zero;

    [DataField, AutoNetworkedField]
    public bool UseContainerOwner = true;

    [DataField, AutoNetworkedField]
    public bool UseAimDirection = false;

    [DataField, AutoNetworkedField]
    public bool ApplyToMuzzleFlash = true;
}
