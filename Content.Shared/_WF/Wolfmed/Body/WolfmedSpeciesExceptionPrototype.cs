using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Wolfmed.Body;

/// <summary>
/// M4 (plan §9.1, §9.3): one check of what disables, kills and restores a round-start species. The conformance test
/// runs every check on every round-start species; a failure is a bug unless the species' exception excuses it.
/// </summary>
public enum WolfmedSpeciesCheck : byte
{
    /// <summary>The body is a wound host.</summary>
    WoundHost,

    /// <summary>Organic: there is a brain, and it carries WolfmedBrain (the clock) and WolfmedOrgan.</summary>
    BrainData,

    /// <summary>Organic: there is a heart. A heartless species has circulatory collapse instead.</summary>
    Heart,

    /// <summary>Organic: every heart carries WolfmedOrgan.</summary>
    HeartData,

    /// <summary>Organic, breathing (a respirator): there are lungs.</summary>
    Lungs,

    /// <summary>Organic: every lung carries WolfmedOrgan.</summary>
    LungData,

    /// <summary>Organic: 29% blood stops the heart (or collapses the circulation).</summary>
    BloodArrest,

    /// <summary>Organic: taking the brain out kills.</summary>
    BrainRemoval,

    /// <summary>Mechanical: every positronic core carries WolfmedOrgan.</summary>
    CoreData,

    /// <summary>Mechanical: every coolant pump carries WolfmedOrgan.</summary>
    PumpData,

    /// <summary>Mechanical: pulling the power source shuts the chassis down.</summary>
    PowerShutdown,

    /// <summary>Mechanical: taking the core out kills.</summary>
    CoreRemoval,
}

/// <summary>
/// M4 (plan §9.3): a deliberate difference a round-start species has from the reference profile, written down with
/// its reason. The id is the species id. A species on the machine ladder says so here; any conformance check it is
/// built to fail is listed, and the test fails on any failure not excused and on any excuse no longer needed.
/// </summary>
[Prototype("wolfmedSpeciesException")]
public sealed partial class WolfmedSpeciesExceptionPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>The species runs the machine ladder (plan §3.11): core, pump and power instead of brain, heart and blood.</summary>
    [DataField]
    public bool Mechanical;

    /// <summary>The checks this species is built to fail.</summary>
    [DataField]
    public List<WolfmedSpeciesCheck> Excuses = new();

    /// <summary>Why, in the plan's words.</summary>
    [DataField(required: true)]
    public string Reason = string.Empty;
}
