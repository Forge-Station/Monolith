using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.AsteroidMining;

/// <summary>
/// Ore an ore magnet has locked onto. It glows while the magnet draws an arc to it, then flies into the magnet's storage
/// at <see cref="End"/>.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WFOreMagnetPulledComponent : Component
{
    [DataField, AutoNetworkedField] public EntityUid Magnet;

    [DataField, AutoNetworkedField] public TimeSpan Start;

    [DataField, AutoNetworkedField] public TimeSpan End;
}

[Serializable, NetSerializable]
public enum WFOreMagnetVisuals : byte
{
    /// <summary>The magnet is switched on and powered.</summary>
    Active,
}

[Serializable, NetSerializable]
public enum WFOreMagnetVisualLayers : byte
{
    Base,
    Glow,
}
