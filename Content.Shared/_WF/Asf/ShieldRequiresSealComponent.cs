using Robust.Shared.GameStates;

namespace Content.Shared._WF.Asf;

/// <summary>
/// On a modsuit control unit with a personal shield: the shield only switches on while the suit is sealed, and
/// drops when it unseals.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class ShieldRequiresSealComponent : Component
{
    /// <summary>Popup shown when the shield is switched on with the suit unsealed.</summary>
    [DataField]
    public LocId UnsealedPopup = "wf-asf-modsuit-shield-unsealed";
}
