using Content.Shared._WF.Hailing;

namespace Content.Server._WF.Hailing;

/// <summary>
/// Marks the stand-in entity that carries an admin's end of a hail made as God, and holds their window.
/// </summary>
[RegisterComponent, UnsavedComponent]
public sealed partial class AdminHailEndpointComponent : Component
{
    public AdminHailEui? Eui;
}

/// <summary>Raised on an admin's stand-in when its hail or call stops, with the reason if there is one to give.</summary>
[ByRefEvent]
public readonly record struct HailEndedEvent(HailEndReason? Reason, string Ship);
