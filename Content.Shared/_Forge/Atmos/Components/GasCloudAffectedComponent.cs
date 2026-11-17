using Content.Shared._Forge.Atmos.Systems;
using Robust.Shared.GameStates;

namespace Content.Shared._Forge.Atmos.Components;

/// <summary>
/// Applied to entities currently standing inside a <see cref="GasCloudComponent"/>.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(SharedGasCloudSystem))]
public sealed partial class GasCloudAffectedComponent : Component
{
    [DataField, AutoNetworkedField]
    public float WalkSpeedModifier = 1f;

    [DataField, AutoNetworkedField]
    public float SprintSpeedModifier = 1f;

    /// <summary>
    /// Apparent pressure in kPa used by barotrauma while inside the cloud.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float Pressure;

    [DataField]
    public float HungerMultiplier = 1f;
}
