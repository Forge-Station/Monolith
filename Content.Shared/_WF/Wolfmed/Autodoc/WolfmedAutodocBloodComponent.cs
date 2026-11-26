namespace Content.Shared._WF.Wolfmed.Autodoc;

/// <summary>
/// The pod's blood reservoir: one slot for a Bloodpack stack, which the planner's transfusion step feeds into a low
/// occupant as their own blood at a steady rate, the way an IV drip does.
/// </summary>
[RegisterComponent]
public sealed partial class WolfmedAutodocBloodComponent : Component
{
    public const string SlotId = "autodoc_blood";

    // Runtime state, written on the server only.

    /// <summary>A transfusion out of the reservoir is under way.</summary>
    [ViewVariables]
    public bool Active;

    /// <summary>The occupant the transfusion or the fault belongs to; a new one starts from nothing.</summary>
    [ViewVariables]
    public EntityUid? Patient;

    /// <summary>The occupant needs blood and there is none loaded: "NO BLOOD LOADED".</summary>
    [ViewVariables]
    public bool NoBlood;

    /// <summary>The NO BLOOD LOADED line has been said for this occupant and this empty slot.</summary>
    [ViewVariables]
    public bool NoBloodSaid;

    /// <summary>
    /// Playtest 5: units left in the pack the pod has opened. A pack leaves the stack the moment it is opened, so a
    /// stack taken out and put back cannot start its top pack over, and the opened pack runs on with the slot empty.
    /// </summary>
    [ViewVariables]
    public float PackOpened;

    [ViewVariables]
    public TimeSpan NextUpdate;
}
