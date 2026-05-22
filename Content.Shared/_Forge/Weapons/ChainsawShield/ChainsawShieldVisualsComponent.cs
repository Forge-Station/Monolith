using Content.Shared.Hands.Components;
using Robust.Shared.GameStates;
using Robust.Shared.Utility;

namespace Content.Shared._Forge.Weapons.ChainsawShield;

[RegisterComponent, NetworkedComponent]
public sealed partial class ChainsawShieldVisualsComponent : Component
{
    [DataField]
    public Dictionary<HandLocation, List<PrototypeLayerData>> InactiveInhandVisuals = new();

    [DataField]
    public Dictionary<HandLocation, List<PrototypeLayerData>> ActiveInhandVisuals = new();
}
