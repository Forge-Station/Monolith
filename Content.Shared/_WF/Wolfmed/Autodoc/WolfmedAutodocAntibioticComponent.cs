namespace Content.Shared._WF.Wolfmed.Autodoc;

/// <summary>
/// Playtest 5: the pod's antibiotic course. An infected occupant is dosed out of the reservoir's antibiotic a dose at a
/// time, beside the queue like the blood reservoir, until nothing on them is infected or the course is spent.
/// </summary>
[RegisterComponent]
public sealed partial class WolfmedAutodocAntibioticComponent : Component
{
    // Runtime state, written on the server only.

    /// <summary>A course is under way.</summary>
    [ViewVariables]
    public bool Active;

    /// <summary>The occupant the course or the fault belongs to; a new one starts from nothing.</summary>
    [ViewVariables]
    public EntityUid? Patient;

    /// <summary>The occupant is infected and no antibiotic is loaded: "NO ANTIBIOTIC LOADED".</summary>
    [ViewVariables]
    public bool NoAntibiotic;

    /// <summary>The NO ANTIBIOTIC LOADED line has been said for this occupant and this empty reservoir.</summary>
    [ViewVariables]
    public bool NoAntibioticSaid;

    /// <summary>Units given to this occupant so far, against <c>wolfmed.pod_antibiotic_course</c>.</summary>
    [ViewVariables]
    public float Given;

    [ViewVariables]
    public TimeSpan NextUpdate;

    /// <summary>When the next dose may go in.</summary>
    [ViewVariables]
    public TimeSpan NextDose;
}
