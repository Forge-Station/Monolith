using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Forge.Weapons;

/// <summary>
/// Maps gun selective fire modes to battery projectile settings configured in YAML.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class ForgeSelectiveFireBatteryComponent : Component
{
    [DataField(required: true)]
    public List<ForgeSelectiveFireBatteryMode> Modes = new();
}

[DataDefinition, Serializable, NetSerializable]
public sealed partial class ForgeSelectiveFireBatteryMode
{
    [DataField(required: true)]
    public SelectiveFire Mode;

    [DataField(required: true)]
    public EntProtoId Proto = default!;

    [DataField]
    public float FireCost = 70;

    /// <summary>
    /// Optional gun spread override for this fire mode. Degrees.
    /// </summary>
    [DataField]
    public Angle? MinAngle;

    [DataField]
    public Angle? MaxAngle;

    /// <summary>
    /// Optional fire rate override for this fire mode. Shots per second.
    /// </summary>
    [DataField]
    public float? FireRate;
}
