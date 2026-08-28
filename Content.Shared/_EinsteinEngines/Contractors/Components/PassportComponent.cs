using Content.Shared.Preferences;
using Robust.Shared.GameStates;

namespace Content.Shared._EE.Contractors.Components;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class PassportComponent : Component
{
    /// <summary>
    /// True while the booklet is closed. Opening the UI opens the booklet;
    /// closing the last UI session closes it again.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool IsClosed = true;

    [ViewVariables]
    public HumanoidCharacterProfile? OwnerProfile;
}
