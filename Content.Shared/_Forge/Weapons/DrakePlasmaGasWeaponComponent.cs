using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._Forge.Weapons;

[Serializable, NetSerializable]
public enum DrakePlasmaTankVisuals : byte
{
    TankLoaded,
}

/// <summary>
/// Drake industrial tools that consume gaseous plasma from an inserted gas tank.
/// Unlike <see cref="PneumaticCannon.PneumaticCannonComponent"/>, only plasma moles are used.
/// </summary>
[RegisterComponent, NetworkedComponent, Access(typeof(SharedDrakePlasmaGasWeaponSystem))]
public sealed partial class DrakePlasmaGasWeaponComponent : Component
{
    public const string TankSlotId = "gas_tank";

    [DataField]
    public float GasUsage = 0.12f;
}
