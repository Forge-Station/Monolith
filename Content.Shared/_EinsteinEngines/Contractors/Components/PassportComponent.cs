using System; // Forge-Change
using Content.Shared.Preferences;
using Robust.Shared.GameStates;

namespace Content.Shared._EE.Contractors.Components;

// Forge-change-start: networked booklet state for in-hand passport UI.
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(raiseAfterAutoHandleState: true)]
public sealed partial class PassportComponent : Component
{
    /// <summary>
    /// True while the booklet is closed. Opening the UI opens the booklet;
    /// closing the last UI session closes it again.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool IsClosed = true;

    /// <summary>
    /// Until when toggling open/closed is blocked (anti-spam + prediction correctness).
    /// </summary>
    public TimeSpan ToggleCooldownEnd; // Forge-Change

    [ViewVariables]
    public HumanoidCharacterProfile? OwnerProfile;
}
// Forge-change-end
