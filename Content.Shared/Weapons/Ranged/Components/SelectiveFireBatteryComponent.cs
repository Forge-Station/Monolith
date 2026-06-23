using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared.Weapons.Ranged.Components;

/// <summary>
/// Maps <see cref="GunComponent.SelectedMode"/> to per-mode battery projectile settings.
/// Forge-Change: used by energy weapons that change proto, fire cost, spread, and fire rate per selective fire mode.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class SelectiveFireBatteryComponent : Component
{
    [DataField(required: true)]
    public List<SelectiveFireBatteryMode> Modes = new();
}

/// <summary>
/// Forge-Change: per-mode battery projectile settings keyed by <see cref="SelectiveFire"/>.
/// </summary>
[DataDefinition, Serializable, NetSerializable]
public sealed partial class SelectiveFireBatteryMode
{
    [DataField(required: true)]
    public SelectiveFire Mode;

    [DataField(required: true)]
    public EntProtoId Proto = default!;

    [DataField]
    public float FireCost = 100;

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
