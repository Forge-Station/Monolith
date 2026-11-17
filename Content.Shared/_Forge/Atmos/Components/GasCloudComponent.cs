using Content.Shared._Forge.Atmos.Systems;
using Content.Shared._NF.Atmos.Systems;
using Content.Shared.Damage;
using Robust.Shared.GameStates;

namespace Content.Shared._Forge.Atmos.Components;

/// <summary>
/// Marks a gas deposit as a visible hazardous cloud that can be harvested from anywhere inside its radius.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
[Access(typeof(SharedGasCloudSystem), typeof(SharedGasDepositSystem))]
public sealed partial class GasCloudComponent : Component
{
    /// <summary>
    /// Maximum radius of the cloud in tiles when the deposit is full.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float Radius = 8f;

    /// <summary>
    /// Current visual and gameplay radius. Shrinks as the deposit is depleted.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float CurrentRadius = 8f;

    [DataField, AutoNetworkedField]
    public Color Color = Color.FromHex("#88AACC");

    /// <summary>
    /// Walk speed multiplier applied to entities inside the cloud.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float WalkSpeedModifier = 0.7f;

    /// <summary>
    /// Sprint speed multiplier applied to entities inside the cloud.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float SprintSpeedModifier = 0.65f;

    /// <summary>
    /// Multiplier on hunger decay while inside the cloud. 1 is no extra hunger.
    /// </summary>
    [DataField]
    public float HungerMultiplier = 2f;

    /// <summary>
    /// Apparent atmospheric pressure in kPa while inside the cloud. 0 disables the pressure effect.
    /// </summary>
    [DataField]
    public float Pressure = 800f;

    /// <summary>
    /// Target environmental temperature in Kelvin. Null disables the temperature pull.
    /// </summary>
    [DataField]
    public float? Temperature;

    /// <summary>
    /// Fraction of the temperature gap applied per second. Clothing heat resistance still applies.
    /// </summary>
    [DataField]
    public float TemperatureTransfer = 0.12f;

    /// <summary>
    /// Toxin / asphyxiation damage dealt per second while inside the cloud. Empty means none.
    /// </summary>
    [DataField]
    public DamageSpecifier ToxinDamage = new();

    /// <summary>
    /// If true, a working internals setup (mask + tank) blocks <see cref="ToxinDamage"/>.
    /// </summary>
    [DataField]
    public bool InternalsBlockToxins = true;

    /// <summary>
    /// Snapshot of the deposit's moles when the cloud spawned, used to scale radius as it depletes.
    /// </summary>
    [DataField]
    public float InitialMoles;
}
