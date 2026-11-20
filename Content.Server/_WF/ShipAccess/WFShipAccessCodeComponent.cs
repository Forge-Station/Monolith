namespace Content.Server._WF.ShipAccess;

/// <summary>
/// The ship code and the keypad lockout table. Server only and never networked: the owner's console gets the
/// codes through a directed event. Lives on the grid; the code saves with it, the lockouts do not.
/// </summary>
[RegisterComponent]
public sealed partial class WFShipAccessCodeComponent : Component
{
    /// <summary>Four digits that open every Code door without a code of its own, null when unset.</summary>
    [DataField]
    public string? ShipCode;

    /// <summary>Keypad misses and lockouts per character name, this round only.</summary>
    [ViewVariables]
    public Dictionary<string, WFShipAccessLockout> Lockouts = new();

    /// <summary>Failed keypad attempts on this ship this round.</summary>
    [ViewVariables]
    public int Misses;
}

/// <summary>One person's keypad misses on one ship.</summary>
public sealed class WFShipAccessLockout
{
    /// <summary>Misses inside the current window.</summary>
    public int Misses;

    /// <summary>When the current window of misses began.</summary>
    public TimeSpan WindowStart;

    /// <summary>Keypads refuse this person until then.</summary>
    public TimeSpan LockedUntil;
}

/// <summary>A door's own four-digit code. Server only, saved with the grid; the door's rule component only carries HasOwnCode.</summary>
[RegisterComponent]
public sealed partial class WFDoorCodeComponent : Component
{
    [DataField]
    public string Code = string.Empty;
}
