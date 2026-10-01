using Content.Shared.Chemistry.Reagent;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Wolfmed.Wounds;

/// <summary>
/// UI4: one treatment procedure, as structured rows rather than a block of numbered text. The analyzer's
/// procedure window draws one row per <see cref="WolfmedTreatmentStep"/>, with that step's tool sprite
/// beside it, and greys the row once <see cref="WolfmedTreatmentStep.Done"/> holds for the patient.
/// </summary>
/// <remarks>
/// Ids are derived, not chosen: a wound's procedure is its wound prototype id, a chassis variant appends
/// <c>Mechanical</c>, and a condition's is <c>Cond</c> plus the condition name.
/// <see cref="WolfmedTreatmentAdvice"/> owns the derivation and the coverage test iterates every wound
/// prototype, so a wound shipped without a procedure fails rather than showing an empty window.
/// </remarks>
[Prototype("wolfmedTreatmentProcedure")]
public sealed partial class WolfmedTreatmentProcedurePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>The one-line summary under the title. The same key the analyzer uses as a tooltip.</summary>
    [DataField(required: true)]
    public LocId Summary;

    [DataField(required: true)]
    public List<WolfmedTreatmentStep> Steps = new();

    /// <summary>"Do not" lines, drawn under the steps in the warning colour. Never greyed.</summary>
    [DataField]
    public List<LocId> Avoid = new();
}

/// <summary>One row of a procedure: what to do, what it is done with, and how to tell it has been done.</summary>
[DataDefinition]
public sealed partial class WolfmedTreatmentStep
{
    [DataField(required: true)]
    public LocId Text;

    /// <summary>The item the step uses. Drawn as that entity's sprite, named in the icon's tooltip.</summary>
    [DataField]
    public EntProtoId? Tool;

    /// <summary>A reagent rather than an item. Drawn with the chem glyph tinted the reagent's colour.</summary>
    [DataField]
    public ProtoId<ReagentPrototype>? Reagent;

    /// <summary>Done through the surgery UI on an open incision. Decides the icon when no tool fits.</summary>
    [DataField]
    public bool Surgery;

    /// <summary>All of these must hold before the row greys. Empty means the row is never greyed alone.</summary>
    [DataField]
    public List<WolfmedStepCheck> Done = new();
}
