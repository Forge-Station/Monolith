using Robust.Shared.GameStates;

namespace Content.Shared._Forge.Clothing;

/// <summary>
/// Neck mantle that switches equipped sprite based on what the wearer holds in their hands.
/// Visual updates are handled on the client (HandsOpenMantleClothingSystem).
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class HandsOpenMantleClothingComponent : Component
{
    [DataField]
    public string ClosedState = "equipped-NECK";

    [DataField]
    public string RightHandState = "open-1";

    [DataField]
    public string BothHandsState = "open-2";

    [DataField]
    public string LeftHandState = "open-3";
}
