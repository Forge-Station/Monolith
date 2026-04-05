using System; // Forge-Change
using Content.Shared.Preferences;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._EE.Contractors.Components;

[RegisterComponent, NetworkedComponent]
public sealed partial class PassportComponent : Component
{
    public bool IsClosed;

    /// <summary>
    /// Until when toggling open/closed is blocked (anti-spam + prediction correctness).
    /// </summary>
    /// Forge-Change
    public TimeSpan ToggleCooldownEnd;

    [ViewVariables]
    public HumanoidCharacterProfile OwnerProfile;
}
